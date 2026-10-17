using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // ------------------------------------------------------------------- devices

    /// <summary>
    /// The input device that delivers this step's input, chosen by the step's own settings: in
    /// front by default, posted at one window when the step names one, or through a driver.
    /// </summary>
    private IInputDevice Input(ExecutableStep step) => _devices.Inputs.For(Route(step));

    /// <summary>
    /// A click worked out after waiting for the screen. The input door is taken here rather than
    /// around the whole step, because the wait that found the place is not input and must not keep
    /// another macro from typing while it runs.
    /// </summary>
    private async Task ClickAt(ExecutableStep step, ScreenPoint at, CancellationToken token)
    {
        if (!ExclusiveInput)
        {
            Aim(at);
            Input(step).Click(Button(step), at.X, at.Y, 1, 0);
            return;
        }

        using var inside = await InputGate.Take(RunName, token);
        Aim(at);
        Input(step).Click(Button(step), at.X, at.Y, 1, 0);
    }

    /// <summary>Presses a key and remembers it, so the run can let it go if the macro never does.</summary>
    private void HoldKey(ExecutableStep step)
    {
        var device = Input(step);
        var key = ReadText(step, "key");
        device.KeyDown(key);
        if (!_heldKeys.Any(held => Same(held.Device, device) && Same(held.Name, key)))
        {
            _heldKeys.Add((device, key));
        }
    }

    /// <summary>Releases a key the macro asked to release, and stops counting it as held.</summary>
    private void LetGoKey(ExecutableStep step)
    {
        var device = Input(step);
        var key = ReadText(step, "key");
        device.KeyUp(key);
        _heldKeys.RemoveAll(held => Same(held.Device, device) && Same(held.Name, key));
    }

    /// <summary>The same, for a mouse button, remembering where it was pressed.</summary>
    private void HoldButton(ExecutableStep step)
    {
        var device = Input(step);
        var button = Button(step);
        var point = Point(step, "x", "y");
        device.MouseDown(button, point.X, point.Y);
        if (!_heldButtons.Any(held =>
                Same(held.Device, device) && Same(held.Button, button)))
        {
            _heldButtons.Add((device, button, point));
        }
    }

    /// <summary>Releases a mouse button the macro asked to release, wherever it is now held.</summary>
    private void LetGoButton(ExecutableStep step)
    {
        var device = Input(step);
        var button = Button(step);
        var point = Point(step, "x", "y");
        device.MouseUp(button, point.X, point.Y);
        _heldButtons.RemoveAll(held => Same(held.Device, device) && Same(held.Button, button));
    }

    /// <summary>True when two device handles or two names stand for the same thing.</summary>
    private static bool Same(IInputDevice device, IInputDevice other) => ReferenceEquals(device, other);

    private static bool Same(string name, string other)
        => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lets go of anything the macro left pressed. A key or a button held down when a run ends
    /// stays down on the machine until something else takes it up — the user's own typing goes
    /// to the wrong place and the mouse drags whatever it is over — so this happens however the
    /// run ended: finished, stopped, failed or cancelled. A release that fails is logged rather
    /// than raised, because the run already has its answer and a stuck key is not made better by
    /// throwing.
    /// </summary>
    private void LetGoOfHeldInput()
    {
        if (_heldKeys.Count == 0 && _heldButtons.Count == 0 && !_gamepadUsed)
        {
            return;
        }

        var keys = _heldKeys.ToList();
        var buttons = _heldButtons.ToList();
        _heldKeys.Clear();
        _heldButtons.Clear();

        var released = 0;
        foreach (var (device, key) in keys)
        {
            try
            {
                device.KeyUp(key);
                released++;
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, LogKeys.LetGoFailed, key, failure.Message);
            }
        }

        foreach (var (device, button, point) in buttons)
        {
            try
            {
                device.MouseUp(button, point.X, point.Y);
                released++;
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, LogKeys.LetGoFailed, button, failure.Message);
            }
        }

        // A controller left holding a button, or a stick pushed over, would keep a game walking
        // into a wall long after the macro that pushed it has finished.
        if (_gamepadUsed)
        {
            _gamepadUsed = false;
            try
            {
                _devices.Gamepad.ReleaseAll();
                Log(LogLevel.Info, 0, string.Empty, LogKeys.GamepadReleased);
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, LogKeys.GamepadLetGoFailed, failure.Message);
            }
        }

        if (released > 0)
        {
            Log(LogLevel.Info, 0, string.Empty, LogKeys.LetGo, released);
        }
    }

    /// <summary>Where a step wants its input to go. Anything unset means the front window.</summary>
    private InputRoute Route(ExecutableStep step)
    {
        var mode = DeliveryMode(step);
        if (mode == DriverMode)
        {
            return new InputRoute(InputDelivery.Driver, 0);
        }

        if (mode != BackgroundMode)
        {
            return InputRoute.Front;
        }

        // Posting messages needs a window to post them at. The step names it the way the window
        // actions name one — the text held up against the window the way it says to hold it up —
        // so a step that types into a window whose title moves with the document is not left
        // without a handle on it.
        var rule = WindowRuleOf(step, "targetWindow", "targetHow");
        if (rule.Text.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingTargetWindow);
        }

        var window = _devices.Windows.Find(rule.Text, rule.Match, rule.Compare, rule.Options)
                     ?? throw new StepFailure(RunKeys.WindowNotFound, rule.Text);

        return new InputRoute(InputDelivery.Background, window.Handle);
    }

    /// <summary>What a step's input mode says it wants, as the file writes it.</summary>
    private static string DeliveryMode(ExecutableStep step)
        => step.Text("inputMode").Trim().ToLowerInvariant();

    /// <summary>
    /// Whether this step's input is posted at a window as messages rather than driving the desktop.
    /// The two things this mode cannot do — report where the pointer is, and move it — are asked
    /// about before the window is looked up, so a step that cannot work is turned down on the
    /// strength of its own settings rather than of what happens to be open.
    /// </summary>
    private static bool PostsMessages(ExecutableStep step) => DeliveryMode(step) == BackgroundMode;

    /// <summary>What the input mode is written as when the messages are posted at a window.</summary>
    private const string BackgroundMode = "background";

    /// <summary>What the input mode is written as when a virtual device sends the input.</summary>
    private const string DriverMode = "driver";

    /// <summary>
    /// The corner a step's numbers are measured from, or <c>null</c> when they are screen pixels
    /// already. It is looked up afresh every time the step runs, which is what keeps a macro that
    /// names a window or a control pointing at the same place after the window has been dragged
    /// elsewhere or the list under the control has been scrolled.
    /// </summary>
    private ScreenPoint? Anchor(ExecutableStep step) => AnchorOf(step).Corner;

    /// <summary>
    /// The same, together with the window those numbers were counted from when there was one. The
    /// window is also where the step's pixels are to be read from: a step that says "a hundred
    /// pixels into the game window" and a step that says "read the game window" are saying the same
    /// thing about which window, and one of them should not have to say it twice.
    /// </summary>
    private (ScreenPoint? Corner, WindowInfo? Window) AnchorOf(ExecutableStep step)
    {
        var mode = ReadText(step, "anchorMode").Trim().ToLowerInvariant();

        if (mode == "element")
        {
            var selector = ReadText(step, "anchorSelector");
            var anchor = _devices.Ui.FindAll(AnchorQuery(step), 1).FirstOrDefault()
                         ?? throw new StepFailure(RunKeys.ElementNotFound, selector);

            return (anchor.Location, null);
        }

        if (mode is not ("window" or "client"))
        {
            return (null, null);
        }

        // The window is recognised the way the window actions recognise one, and no text at all
        // means the window in front, the same way every other window field reads.
        var rule = WindowRuleOf(step, "anchorWindow", "anchorHow");
        var window = _devices.Windows.Find(rule.Text, rule.Match, rule.Compare, rule.Options)
                     ?? throw new StepFailure(RunKeys.WindowNotFound, rule.Text);

        var corner = mode == "client" ? _devices.Windows.ClientOrigin(window.Handle)
            : window.Location;
        return (corner, window);
    }

    /// <summary>One written position, in the screen pixels the devices ask for.</summary>
    private ScreenPoint Place(ExecutableStep step, int x, int y) => Placed(Anchor(step), x, y);

    private static ScreenPoint Placed(ScreenPoint? origin, int x, int y)
        => origin is { } corner ? new ScreenPoint(corner.X + x, corner.Y + y) : new ScreenPoint(x, y);

    /// <summary>The colour of the pixel a step points at, counted from wherever it anchors.</summary>
    private PixelColor PixelAt(ExecutableStep step)
    {
        var (corner, window) = Spot(step);
        return PixelAt(step, corner, window);
    }

    /// <summary>
    /// The colour of one pixel. A pixel the step counts from the screen is read straight off the
    /// screen, which is a fraction of the cost of a picture; one it counts from a window is read
    /// out of a picture of that window, so a window another window covers still gives its own
    /// colour rather than the covering window's.
    /// </summary>
    private PixelColor PixelAt(ExecutableStep step, ScreenPoint point, WindowInfo? window)
    {
        if (window is null)
        {
            // A pixel of the desktop is read straight off it rather than out of a picture, so this
            // is the one reading that has to say for itself that it is not reading a window.
            Reading(null);
            return _devices.Screen.PixelAt(point.X, point.Y);
        }

        var shot = Shot(step, point.X, point.Y, 1, 1, window);
        if (shot.Origin != point)
        {
            // The picture came back from somewhere else, which means the window does not reach
            // that far: there is no such pixel, and reading a neighbour's colour would be a lie.
            throw new StepFailure(RunKeys.PixelOutsideScreen, $"{point.X},{point.Y}");
        }

        return shot.Frame[0, 0];
    }

    /// <summary>
    /// Whether this kind of step is one that looks at the screen, and so is one whose looking can
    /// be tried out on its own. It is what the dialog offers "try looking" for, and the only thing
    /// <see cref="LookOnce"/> will look at the screen for.
    /// </summary>
    public static bool CanLook(string stepType) => stepType is
        "vision.findImage" or "vision.waitImage" or "vision.clickImage"
        or "vision.detect" or "vision.waitDetect" or "vision.clickDetect"
        or "vision.findColor" or "vision.waitColor" or "vision.getPixel"
        or "vision.capture" or "vision.captureWindow"
        or "ocr.recognize" or "ocr.findText" or "ocr.clickText";

    /// <summary>
    /// Looks at the screen the way a step would and hands back what it saw, without doing anything
    /// about it: no key is pressed, no mouse button goes down, no variable is written. It answers
    /// "what does this step actually see", where running the step would answer it by clicking on
    /// the user's screen. Null when this kind of step does not look at the screen at all.
    /// <paramref name="watch"/> is told which window is being read as it happens, if anybody wants
    /// to be: it is the same callback a run hands over, so a trial can show what a run shows.
    /// </summary>
    public static LookOutcome LookOnce(ExecutableStep step, IDeviceLayer devices,
        IRunLooks? watch = null)
    {
        if (!CanLook(step.Type))
        {
            return new LookOutcome(null, RunKeys.NotALookingStep);
        }

        var asked = new Asked(watch);
        try
        {
            new MacroRunner(new VariableStore(), devices: devices, looks: asked).SeeOnce(step);
        }
        catch (ExpressionException failure)
        {
            return new LookOutcome(null, RunKeys.BadExpression, failure.Detail);
        }
        catch (RegexTimeoutException failure)
        {
            return new LookOutcome(null, RunKeys.RegexTimeout, failure.Pattern);
        }
        catch (DeviceUnavailableException failure)
        {
            return new LookOutcome(null, RunKeys.NoDevice, failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while looking, the answer is a message rather than
            // an exception in the dialog's face.
            return new LookOutcome(null, RunKeys.DeviceFailed, failure.Message);
        }

        return asked.Seen is { } look
            ? new LookOutcome(look)
            : new LookOutcome(null, RunKeys.NotALookingStep);
    }

    /// <summary>
    /// Answers one condition the way a run would, on the values in play at that moment and without
    /// carrying anything out. This is what a breakpoint's own condition is asked with: the debugger
    /// has the values the run has just written, and none of the run's own machinery. A condition
    /// that cannot be answered at all comes back saying why rather than throwing, so the caller can
    /// tell "no" apart from "could not be asked".
    /// </summary>
    public static ConditionOutcome AskCondition(ExecutableStep condition, VariableStore variables,
        IDeviceLayer devices, IMacroLibrary? macros = null)
    {
        try
        {
            return new ConditionOutcome(new MacroRunner(variables, devices: devices, macros: macros)
                .Check(condition, 0, CancellationToken.None));
        }
        catch (ExpressionException failure)
        {
            return new ConditionOutcome(false, RunKeys.BadExpression, failure.Detail);
        }
        catch (RegexTimeoutException failure)
        {
            return new ConditionOutcome(false, RunKeys.RegexTimeout, failure.Pattern);
        }
        catch (DeviceUnavailableException failure)
        {
            return new ConditionOutcome(false, RunKeys.NoDevice, failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while a condition is asked, the answer is a message
            // rather than an exception in the debugger's face.
            return new ConditionOutcome(false, RunKeys.DeviceFailed, failure.Message);
        }
    }

    /// <summary>Where a look goes when the caller only wanted the one.</summary>
    private sealed class Asked(IRunLooks? watch) : IRunLooks
    {
        public StepLook? Seen { get; private set; }

        public void Look(StepLook look) => Seen = look;

        /// <summary>Nothing is read from what a step came to; only the one look was wanted.</summary>
        public void Ran(StepRun run)
        {
        }

        public void Reading(WindowInfo? window) => watch?.Reading(window);
    }

    /// <summary>
    /// One look at the screen for a step, without the step: a search looks once rather than
    /// waiting, a wait does not wait, and the mouse is left alone.
    /// </summary>
    private void SeeOnce(ExecutableStep step)
    {
        switch (step.Type)
        {
            case "vision.findImage":
            case "vision.waitImage":
            case "vision.clickImage":
                Watching(() => Hits(step));
                break;

            case "vision.detect":
            case "vision.waitDetect":
            case "vision.clickDetect":
                Watching(() => Sightings(step));
                break;

            case "vision.findColor":
            case "vision.waitColor":
                Watching(() => Scan(step, PixelColor.Parse(ReadValue(step, "color").AsText()),
                    ReadValue(step, "tolerance").AsNumber(), measured: false));
                break;

            case "vision.findColors":
                Watching(() => PatternHits(step));
                break;

            case "vision.getPixel":
                Watching(() =>
                {
                    var (point, window) = Spot(step);
                    SawPixel(step, point, PixelAt(step, point, window).ToHex(), window);
                });
                break;

            case "vision.capture":
                Watching(() => Took(step, CaptureBox(step)));
                break;

            case "vision.captureWindow":
                Watching(() => Took(step, WindowBox(step)));
                break;

            case "ocr.recognize":
                Watching(() => WasRead(step, ReadBox(step)));
                break;

            case "ocr.findText":
            case "ocr.clickText":
                Watching(() => SawSought(step));
                break;

            default:
                break;
        }
    }

    /// <summary>Hands over a picture that was taken, without keeping it anywhere.</summary>
    private void Took(ExecutableStep step, (ImageFrame Frame, ScreenPoint Corner) taken)
        => Saw(step, LookKind.Capture, taken.Frame, taken.Corner,
            [
                new(new ImageMatch(0, taken.Corner,
                    new ScreenSize(taken.Frame.Width, taken.Frame.Height)), LookRole.Area),
            ],
            // The name the picture would be handed over under, which is what the run itself puts
            // here when it really takes one; a step asked to try its looking writes nothing.
            1, step.Return("image"));

    /// <summary>Hands over what was read off a picture, without writing the reading anywhere.</summary>
    private void WasRead(ExecutableStep step,
        (ScreenPoint Corner, ImageFrame Area, IReadOnlyList<TextSpan> Spans) read)
        => Saw(step, LookKind.Text, read.Area, read.Corner,
            Marks([(read.Area, read.Corner)], TextMarks(read.Spans, read.Corner), 0), 0,
            note: string.Join(' ', read.Spans.Select(span => span.Text)),
            minimum: Number(step, "minScore"));

    /// <summary>Hands over what a step looking for writing would find right now.</summary>
    private void SawSought(ExecutableStep step)
    {
        var wanted = ReadValue(step, "queryText").AsText();
        var way = TextWay(step, wanted);
        var readings = ReadSpans(step);
        var (_, chosen) = Counting(step, readings, wanted, way);

        SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen,
            wanted, minimum: Number(step, "minScore"));
    }

    /// <summary>
    /// Where the pixel a step watches sits on the screen, window anchoring and all, with the window
    /// the point was counted from when there was one, which is also the window its colour has to be
    /// read out of.
    /// </summary>
    private (ScreenPoint Corner, WindowInfo? Window) Spot(ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        return (RecordedPlace(step, "place")
                ?? Placed(anchor.Corner, Number(step, "x"), Number(step, "y")),
            anchor.Window);
    }

    /// <summary>
    /// Where a mouse action lands. An empty position means "wherever the pointer already is",
    /// which is what a click or a press with no coordinates written down means; a step that says so
    /// outright reads the pointer the same way, whatever its numbers hold. Asking for the pointer is
    /// answered before anything is measured from, so a step that says it works at the pointer does
    /// not need the window its numbers would have been counted from to be there at all. After the
    /// pointer comes the record, and the numbers last: a step that takes its place from the pointer
    /// works there whatever else it carries, and a step that takes it from a record does not read the
    /// numbers at all.
    /// </summary>
    private ScreenPoint Point(ExecutableStep step, string xName, string yName)
    {
        if (Flag(step, "useCursor", false))
        {
            return Input(step).Cursor;
        }

        if (RecordedPlace(step, "place") is { } place)
        {
            return place;
        }

        var x = ReadText(step, xName).Trim();
        var y = ReadText(step, yName).Trim();
        return x.Length == 0 && y.Length == 0
            ? Input(step).Cursor
            : Placed(Anchor(step), Number(step, xName), Number(step, yName));
    }

    /// <summary>
    /// Where a move lands: the place a record says, or the two numbers the step writes down, which
    /// are counted from the corner it names and are read as zero where they were not written. A
    /// move has no "wherever the pointer already is" of its own, so both numbers missing is a move
    /// to the corner the step counts from rather than to nowhere.
    /// </summary>
    private ScreenPoint MovePlace(ExecutableStep step)
        => RecordedPlace(step, "place")
            ?? Placed(Anchor(step), Number(step, "x"), Number(step, "y"));

    /// <summary>
    /// The place a record field names, or null when nobody filled it in. A value written there that
    /// is not a record is refused with what was written, so a macro that aimed at the wrong thing
    /// can be read back from its own log.
    /// </summary>
    private ScreenPoint? RecordedPlace(ExecutableStep step, string name)
    {
        var value = ReadValue(step, name);
        if (value.IsNull)
        {
            return null;
        }

        var written = step.Text(name).Trim();
        return value.IsRecord
            ? RecordPoint(value, written)
            : throw new StepFailure(RunKeys.BadPoint, written);
    }

    /// <summary>
    /// The point a record says it is at, in screen pixels: its <c>x</c> and <c>y</c>, which every
    /// record that names a rectangle carries as the middle of it rather than as a corner. A record
    /// without them is refused here, and what was handed over is said so that a macro that aimed at
    /// the wrong thing can be read back from its own log.
    /// </summary>
    private static ScreenPoint RecordPoint(Value record, string written)
    {
        if (record.Field("x") is not { } x || record.Field("y") is not { } y)
        {
            throw new StepFailure(RunKeys.BadPoint, written);
        }

        return new ScreenPoint(Whole(x), Whole(y));
    }

    private static string Button(ExecutableStep step)
    {
        var button = step.Text("button").Trim();
        return button.Length == 0 ? "left" : button;
    }

    /// <summary>
    /// One end of a drag, in the screen pixels the devices ask for: the record that end takes its
    /// place from, or the two numbers written for it, each of which may be an expression. Every
    /// record that carries a rectangle carries the middle of it under <c>x</c> and <c>y</c>, so a
    /// hit, a window or an element goes in as it stands — "drag what was just found onto that
    /// window" is one field each end instead of four numbers worked out from other numbers. A
    /// record's numbers are screen pixels, the way every record's are; numbers written out are
    /// counted from <paramref name="origin"/>, which is the corner the step says its own numbers are
    /// measured from and is read once so both ends share it. <paramref name="end"/> names which end
    /// this is, because a drag has two of everything and the fields are read by name.
    /// </summary>
    private ScreenPoint DragPlace(ExecutableStep step, string end, ScreenPoint? origin)
    {
        return RecordedPlace(step, end + "Place")
            ?? Placed(origin, Number(step, end + "X"), Number(step, end + "Y"));
    }

    /// <summary>
    /// Moves the pointer to a point. A straight move is handed to the device exactly as it always
    /// was; a bent or hand-like one is worked out into a path first, because the shape of that
    /// path is the whole point of those styles.
    /// </summary>
    private void MovePointer(ExecutableStep step, ScreenPoint to)
    {
        var route = MousePath.Route(ReadText(step, "style"));
        var duration = Pace(Number(step, "durationMs"));
        if (route is MouseRoute.Direct)
        {
            Input(step).MoveMouse(to.X, to.Y, duration);
            return;
        }

        WalkPointer(Input(step), route, Input(step).Cursor, to, duration);
    }

    /// <summary>The same, for a move written as an offset from wherever the pointer is.</summary>
    private void MovePointerBy(ExecutableStep step)
    {
        // "By how much" is a question about the pointer, and the posted-input mode never moves the
        // pointer: it tells the window what happened, so the pointer a relative move counts from is
        // not its own. Sending the offsets on anyway is how "move 200 to the right" becomes "go to
        // the point (200, 0)" — the wrong place, arrived at silently — so this mode says what it
        // cannot do instead, whichever way the move was asked to travel.
        if (PostsMessages(step))
        {
            throw new StepFailure(RunKeys.BackgroundNeedsPointer);
        }

        var dx = Number(step, "dx");
        var dy = Number(step, "dy");
        var route = MousePath.Route(ReadText(step, "style"));
        var duration = Pace(Number(step, "durationMs"));
        if (route is MouseRoute.Direct)
        {
            Input(step).MoveMouseRelative(dx, dy, duration);
            return;
        }

        var from = Input(step).Cursor;
        WalkPointer(Input(step), route, from, new ScreenPoint(from.X + dx, from.Y + dy), duration);
    }

    private void WalkPointer(IInputDevice input, MouseRoute route, ScreenPoint from,
        ScreenPoint to, int duration)
    {
        if (duration <= 0)
        {
            duration = Pace(BorrowedMoveMs);
        }

        input.MoveMouseAlong(
            MousePath.Plan(route, from, to, MousePath.StepsFor(route, duration)), duration);
    }

    /// <summary>Presses, travels, and lets go: the same shape of path as a plain move.</summary>
    private async Task DragPointer(ExecutableStep step, CancellationToken token)
    {
        // Both ends are measured from the same corner, so a drag across a window stays inside it —
        // and a record handed to one of them says where on the screen it is, so it comes back
        // already in screen pixels rather than moved by that corner a second time. The corner is
        // read once, here, rather than once per end.
        var origin = Anchor(step);
        var from = DragPlace(step, "start", origin);
        var to = DragPlace(step, "end", origin);
        var duration = Pace(Number(step, "durationMs"));
        var settle = Pace(Number(step, "settleMs"));
        var steps = Math.Clamp(Number(step, "dragSteps"), 1, 200);
        var route = MousePath.Route(ReadText(step, "style"));
        if (route is not MouseRoute.Direct)
        {
            if (duration <= 0)
            {
                duration = Pace(BorrowedMoveMs);
            }
        }

        if (settle <= 0)
        {
            if (route is MouseRoute.Direct)
            {
                Input(step).Drag(Button(step), from.X, from.Y, to.X, to.Y, duration, steps);
            }
            else
            {
                Input(step).DragAlong(Button(step), MousePath.Plan(route, from, to, steps), duration);
            }

            return;
        }

        // Held at the end for a moment before letting go: a slot a thing is dragged into only takes
        // it once the pointer has rested over it, and letting go the instant the pointer arrives is
        // how a drag onto something turns into a drag past it. That is three calls rather than the
        // one the devices offer, because the one they offer lets go before it comes back.
        Input(step).MouseDown(Button(step), from.X, from.Y);
        Input(step).MoveMouseAlong(MousePath.Plan(route, from, to, steps), duration);
        await Pause(settle, token);
        Input(step).MouseUp(Button(step), to.X, to.Y);
    }

    private IReadOnlyList<string> Keys(ExecutableStep step)
        => Chord(ReadText(step, "keys"));

    /// <summary>The shortcut a clipboard step presses, written as one combination.</summary>
    private IReadOnlyList<string> Shortcut(ExecutableStep step)
        => Chord(ReadText(step, "shortcut"));

    /// <summary>The keys of one written combination, such as the Ctrl+Shift+S a row of a sequence holds.</summary>
    private static IReadOnlyList<string> Chord(string written)
        => [.. written.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Sends a run of combinations in order, one row after another, the way a macro plays out a
    /// combo. Each row is held together for its own moment and then the run waits out the beat
    /// before the next one — the gaps are what make a run read as separate presses rather than as
    /// one long hold, and they are the part of a combo a game notices. A row may carry its own hold
    /// and its own gap, because a combination with one beat of its own is the rule rather than the
    /// exception; the whole run may be played again with a pause between.
    /// </summary>
    private async Task SendKeySequence(ExecutableStep step, int depth, CancellationToken token)
    {
        var rows = step.Rows("keySequence");
        var hold = Number(step, "holdMs");
        var gap = Number(step, "gapMs");
        var after = Pace(Number(step, "afterMs"));
        var runs = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "sequenceIntervalMs"));
        var presses = 0;

        for (var run = 0; run < runs; run++)
        {
            token.ThrowIfCancellationRequested();
            if (run > 0)
            {
                await Pause(interval, token);
            }

            for (var index = 0; index < rows.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var keys = Chord(Column(rows[index], "keys"));
                if (keys.Count == 0)
                {
                    // A row that was added and never filled in is left out rather than sent as a
                    // press of nothing.
                    continue;
                }

                Input(step).Hotkey(keys, Pace(Cell(rows[index], "holdMs", hold)));
                presses++;

                // The beat is between the presses; there is nothing to wait for after the last one.
                if (index < rows.Count - 1)
                {
                    await Pause(Pace(Cell(rows[index], "gapMs", gap)), token);
                }
            }

            if (after > 0)
            {
                await Pause(after, token);
            }
        }

        if (presses == 0)
        {
            throw new StepFailure(RunKeys.NoKeys, string.Empty);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.KeysSent, presses);
    }

    /// <summary>
    /// Presses a key, over again when the step asks for more than one. The pause goes between the
    /// presses and never after the last, so a step that presses once waits for nothing.
    /// </summary>
    private async Task PressKey(ExecutableStep step, CancellationToken token)
    {
        var key = ReadText(step, "key");
        var hold = Pace(Number(step, "holdMs"));
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            Input(step).KeyPress(key, hold);
        }
    }

    /// <summary>
    /// Holds a controller button or lets it up, or presses and lets go of it. A tap is what a
    /// button is normally for, so it is the one that happens when the step does not say; the hold
    /// is the pause between the press and the release, and a step that asked for none still gets a
    /// short one, because a report that comes and goes in the same instant is one a game may never
    /// see. Tapping several times is the same question the key actions answer with repeat and
    /// interval, so it is answered the same way here: one pause between taps, none before the first.
    /// </summary>
    private async Task PressGamepadButton(ExecutableStep step, CancellationToken token)
    {
        var device = _devices.Gamepad;
        var button = ReadText(step, "button");
        _gamepadUsed = true;

        switch (ReadText(step, "buttonMode").Trim().ToLowerInvariant())
        {
            case "down":
                device.Button(button, true);
                return;
            case "up":
                device.Button(button, false);
                return;
        }

        var hold = Math.Max(Pace(Number(step, "holdMs")), ShortestPressMs);
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            device.Button(button, true);
            await Pause(hold, token);
            device.Button(button, false);
        }
    }

    /// <summary>The same for a combination: it can be sent more than once with a pause between.</summary>
    private async Task SendHotkey(ExecutableStep step, CancellationToken token)
    {
        var keys = Keys(step);
        var hold = Pace(Number(step, "holdMs"));
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            Input(step).Hotkey(keys, hold);
        }
    }

    /// <summary>
    /// Clicks the mouse. The device's own click is a quick tap, so a step that asks for a hold
    /// presses, waits and releases by itself; without one the device does the whole thing, which
    /// also covers a click aimed at a background window.
    /// </summary>
    /// <summary>Wheel units in one notch, the amount a wheel turns in.</summary>
    private const int WheelNotch = 120;

    /// <summary>
    /// Turns the wheel. "Notches" is the step a wheel normally moves in; "pixels" measures the
    /// same turn finer, with 120 units to a notch, which is what an exact amount needs. A smooth
    /// duration splits the whole turn over several smaller wheel events with a pause between, so
    /// an application that animates its scrolling has the time to follow.
    /// </summary>
    private async Task ScrollMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var direction = ReadText(step, "direction");
        var pixels = string.Equals(ReadText(step, "unit").Trim(), "pixels", StringComparison.OrdinalIgnoreCase);
        var amount = Math.Max(1, Number(step, "amount"));
        var total = pixels ? amount : amount * WheelNotch;
        var smooth = Pace(Number(step, "smoothMs"));
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            if (smooth <= 0)
            {
                Input(step).Scroll(direction, total, point.X, point.Y);
                continue;
            }

            // One event per 15 ms is about 66 a second: roughly the fastest an application would
            // draw its scrolling at, and never more events than there are units to send.
            var events = Math.Clamp(smooth / 15, 1, total);
            var share = total / events;
            var extra = total % events;

            for (var index = 0; index < events; index++)
            {
                token.ThrowIfCancellationRequested();
                if (index > 0)
                {
                    await Pause(smooth / events, token);
                }

                Input(step).Scroll(direction, share + (index < extra ? 1 : 0), point.X, point.Y);
            }
        }
    }

    private async Task ClickMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var button = Button(step);
        var clicks = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));
        var hold = Pace(Number(step, "holdMs"));
        var spread = Spread(step);
        var from = ArrivedFrom(step);

        // A step that lets the clicks drift, or that wants the pointer put back, has to place every
        // click itself: sending them all at once would put them all on the same pixel, and there
        // would be nowhere to put the pointer back from.
        if (hold <= 0 && spread == 0 && from is null)
        {
            Input(step).Click(button, point.X, point.Y, clicks, interval);
            return;
        }

        for (var count = 0; count < clicks; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            var at = Scattered(point, spread);
            if (hold <= 0)
            {
                Input(step).Click(button, at.X, at.Y, 1, 0);
                continue;
            }

            Input(step).MouseDown(button, at.X, at.Y);
            await Pause(hold, token);
            Input(step).MouseUp(button, at.X, at.Y);
        }

        if (from is { } back)
        {
            Input(step).MoveMouse(back.X, back.Y, 0);
        }
    }

    /// <summary>
    /// Double clicks the mouse: two presses in quick succession, which is what tells the machine a
    /// double click from two single ones. The step may ask for several of them in a row with a
    /// pause between, which is the same question the other input steps answer with repeat and
    /// interval; there is no hold, because holding the button is what makes it stop being a double
    /// click.
    /// </summary>
    private async Task DoubleClickMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));
        var spread = Spread(step);
        var from = ArrivedFrom(step);

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            var at = Scattered(point, spread);
            Input(step).Click(Button(step), at.X, at.Y, 2, 0);
        }

        if (from is { } back)
        {
            Input(step).MoveMouse(back.X, back.Y, 0);
        }
    }

    /// <summary>
    /// How far a click is allowed to drift from the point the step names, in pixels: 0, the default,
    /// is the point itself.
    /// </summary>
    private int Spread(ExecutableStep step)
    {
        var written = ReadText(step, "variance").Trim();
        return written.Length == 0 ? 0 : Math.Clamp(Number(step, "variance"), 0, 100000);
    }

    /// <summary>
    /// A point with the drift the step asked for on it, so a run of clicks is spread over a button
    /// rather than landing on one pixel. A drift of nothing is the point itself, to the pixel.
    /// </summary>
    private static ScreenPoint Scattered(ScreenPoint point, int spread)
        => spread <= 0
            ? point
            : new ScreenPoint(
                point.X + Random.Shared.Next(-spread, spread + 1),
                point.Y + Random.Shared.Next(-spread, spread + 1));

    /// <summary>
    /// Where the pointer was before a step that has to put it back, or null when the step does not
    /// put it back. Posting messages at a window never moves the pointer on screen, so there is
    /// nothing to put back there and nothing is read.
    /// </summary>
    private ScreenPoint? ArrivedFrom(ExecutableStep step)
        => Flag(step, "moveBack", false) && Route(step).Delivery is not InputDelivery.Background
            ? Input(step).Cursor
            : null;

}
