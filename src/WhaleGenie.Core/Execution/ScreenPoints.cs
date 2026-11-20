using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Where a step's point is: numbers counted from the screen or from a window's client area, a
/// record that carries its own basis, both ends of a drag measured from one corner, the drift a
/// click may take, and which way this step's input travels. Nothing here writes a result; the point
/// is worked out and handed back.
/// </summary>
internal sealed class ScreenPoints(IDeviceLayer devices, VariableStore variables,
    WindowFacts windows)
{
    /// <summary>The fields of one step, read the way the catalogue says they are written.</summary>
    private StepArguments Args(ExecutableStep step)
        => new(step, variables, devices.Screen.RefreshMilliseconds);

    /// <summary>A yes/no field, with the answer a blank cell means.</summary>
    private static bool Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where the pixel a step watches sits on the screen, window anchoring and all, with the window
    /// the point was counted from when there was one, which is also the window its colour has to be
    /// read out of.
    /// </summary>
    public (ScreenPoint Corner, WindowInfo? Window) Spot(ExecutableStep step)
    {
        var anchor = windows.AnchorOf(step);
        var point = RecordedPlaceAndBasis(step, "place") is { } place
            ? Resolve(step, place.Point, place.Basis, anchor).Screen
            : ScreenMath.Placed(anchor.Corner, Args(step).Number("x"), Args(step).Number("y"));
        return (point,
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
    public ResolvedPoint Point(ExecutableStep step, string xName, string yName)
    {
        if (Flag(step, "useCursor", false))
        {
            if (PostsMessages(step))
            {
                throw new StepFailure(RunKeys.BackgroundNeedsPointer);
            }

            var pointer = Input(step).Cursor;
            return new ResolvedPoint(pointer, pointer, null);
        }

        if (RecordedPlaceAndBasis(step, "place") is { } place)
        {
            return Resolve(step, place.Point, place.Basis);
        }

        var x = Args(step).Text(xName).Trim();
        var y = Args(step).Text(yName).Trim();
        if (x.Length == 0 && y.Length == 0)
        {
            if (PostsMessages(step))
            {
                throw new StepFailure(RunKeys.BadPoint, step.Type);
            }

            var pointer = Input(step).Cursor;
            return new ResolvedPoint(pointer, pointer, null);
        }

        return Resolve(step, new ScreenPoint(Args(step).Number(xName), Args(step).Number(yName)),
            PostsMessages(step) ? "window" : "step");
    }

    /// <summary>
    /// Where a move lands: the place a record says, or the two numbers the step writes down, which
    /// are counted from the corner it names and are read as zero where they were not written. A
    /// move has no "wherever the pointer already is" of its own, so both numbers missing is a move
    /// to the corner the step counts from rather than to nowhere.
    /// </summary>
    public ResolvedPoint MovePlace(ExecutableStep step)
        => RecordedPlaceAndBasis(step, "place") is { } place
            ? Resolve(step, place.Point, place.Basis)
            : Resolve(step, new ScreenPoint(Args(step).Number("x"), Args(step).Number("y")),
                PostsMessages(step) ? "window" : "step");

    /// <summary>
    /// The place a record field names, or null when nobody filled it in. A value written there that
    /// is not a record is refused with what was written, so a macro that aimed at the wrong thing
    /// can be read back from its own log.
    /// </summary>
    private (ScreenPoint Point, string Basis)? RecordedPlaceAndBasis(ExecutableStep step, string name)
    {
        var value = Args(step).ValueOf(name);
        if (value.IsNull)
        {
            return null;
        }

        var written = step.Text(name).Trim();
        return value.IsRecord
            ? (RecordPoint(value, written),
                value.Field(FieldNames.Basis)?.AsText().Trim().ToLowerInvariant() is { Length: > 0 } basis
                    ? basis
                    : "screen")
            : throw new StepFailure(RunKeys.BadPoint, written);
    }

    /// <summary>
    /// Turns a written point into the device's own space. Window-client coordinates are the step's
    /// numbers in the background; a record says its own basis and is converted only when the step
    /// knows which window that basis belongs to.
    /// </summary>
    private ResolvedPoint Resolve(ExecutableStep step, ScreenPoint point, string basis,
        (ScreenPoint? Corner, WindowInfo? Window)? knownAnchor = null)
    {
        if (PostsMessages(step))
        {
            var window = knownAnchor?.Window ?? windows.Background(step);
            var origin = devices.Windows.ClientOrigin(window.Handle);
            return string.Equals(basis, "window", StringComparison.OrdinalIgnoreCase)
                ? new ResolvedPoint(point, new ScreenPoint(origin.X + point.X, origin.Y + point.Y), point)
                : new ResolvedPoint(new ScreenPoint(point.X - origin.X, point.Y - origin.Y), point,
                    new ScreenPoint(point.X - origin.X, point.Y - origin.Y));
        }

        var mode = Args(step).Text("anchorMode").Trim().ToLowerInvariant();
        if (mode.Length > 0 && mode is not ("screen" or "window"))
        {
            throw new StepFailure(RunKeys.BadOption, "anchorMode");
        }

        var corner = knownAnchor is { } known
            ? known.Corner
            : mode == "window" ? Anchor(step) : null;
        if (!string.Equals(basis, "step", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(basis, "window", StringComparison.OrdinalIgnoreCase))
            {
                if (mode != "window" || corner is not { } fromWindow)
                {
                    throw new StepFailure(RunKeys.BadPoint, "window point without a window basis");
                }

                return new ResolvedPoint(
                    new ScreenPoint(fromWindow.X + point.X, fromWindow.Y + point.Y),
                    new ScreenPoint(fromWindow.X + point.X, fromWindow.Y + point.Y), point);
            }

            return new ResolvedPoint(point, point,
                mode == "window" && corner is { } from
                    ? new ScreenPoint(point.X - from.X, point.Y - from.Y)
                    : null);
        }

        if (mode != "window")
        {
            return new ResolvedPoint(point, point, null);
        }

        var originOfWindow = corner
            ?? throw new StepFailure(RunKeys.WindowNotFound, windows.Text(step));
        var screen = new ScreenPoint(originOfWindow.X + point.X, originOfWindow.Y + point.Y);
        return new ResolvedPoint(screen, screen, point);
    }

    /// <summary>One screen point counted from a window's client origin.</summary>
    public ScreenPoint InWindow(WindowInfo window, ScreenPoint screen)
    {
        var origin = devices.Windows.ClientOrigin(window.Handle);
        return new ScreenPoint(screen.X - origin.X, screen.Y - origin.Y);
    }

    /// <summary>The same screen point counted from the step's window-client origin, when it has one.</summary>
    public ScreenPoint? WindowPointAt(ExecutableStep step, ScreenPoint screen)
    {
        try
        {
            var origin = PostsMessages(step)
                ? devices.Windows.ClientOrigin(windows.Background(step).Handle)
                : Args(step).Text("anchorMode").Trim().Equals("window", StringComparison.OrdinalIgnoreCase)
                    ? Anchor(step)
                    : null;
            return origin is { } corner
                ? new ScreenPoint(screen.X - corner.X, screen.Y - corner.Y)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The point a record says it is at, in screen pixels: its <c>x</c> and <c>y</c>, which every
    /// record that names a rectangle carries as the middle of it rather than as a corner. A record
    /// without them is refused here, and what was handed over is said so that a macro that aimed at
    /// the wrong thing can be read back from its own log.
    /// </summary>
    private static int Whole(Value side) => (int)Math.Round(side.AsNumber());

    private static ScreenPoint RecordPoint(Value record, string written)
    {
        if (record.Field("x") is not { } x || record.Field("y") is not { } y)
        {
            throw new StepFailure(RunKeys.BadPoint, written);
        }

        return new ScreenPoint(Whole(x), Whole(y));
    }

    public static string Button(ExecutableStep step)
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
    /// counted from the corner <paramref name="anchor"/> names, which the step reads once so both
    /// ends share it. <paramref name="end"/> names which end this is, because a drag has two of
    /// everything and the fields are read by name.
    /// </summary>
    public ResolvedPoint DragPlace(ExecutableStep step, string end,
        (ScreenPoint? Corner, WindowInfo? Window) anchor)
    {
        return RecordedPlaceAndBasis(step, end + "Place") is { } place
            ? Resolve(step, place.Point, place.Basis, anchor)
            : Resolve(step, new ScreenPoint(Args(step).Number(end + "X"), Args(step).Number(end + "Y")),
                PostsMessages(step) ? "window" : "step", anchor);
    }
    /// <summary>One written position, in the screen pixels the devices ask for.</summary>
    public ScreenPoint Place(ExecutableStep step, int x, int y)
        => ScreenMath.Placed(Anchor(step), x, y);

    /// <summary>The input device this step's mode routes to.</summary>
    public IInputDevice Input(ExecutableStep step) => devices.Inputs.For(Route(step));

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
        var rule = windows.RuleOf(step);
        if (rule.MatchValue.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingTargetWindow);
        }

        var window = devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                         rule.Options)
                     ?? throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);

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
    public static bool PostsMessages(ExecutableStep step) => DeliveryMode(step) == BackgroundMode;

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
    private ScreenPoint? Anchor(ExecutableStep step) => windows.AnchorOf(step).Corner;

    /// <summary>
    /// A point with the drift the step asked for on it, so a run of clicks is spread over a button
    /// rather than landing on one pixel.
    /// </summary>
    public ScreenPoint Scatter(ExecutableStep step, ScreenPoint point)
        => Scattered(point, Spread(step));

    /// <summary>
    /// How far a click is allowed to drift from the point the step names, in pixels: 0, the default,
    /// is the point itself.
    /// </summary>
    private int Spread(ExecutableStep step)
    {
        var written = Args(step).Text("variance").Trim();
        return written.Length == 0 ? 0 : Math.Clamp(Args(step).Number("variance"), 0, 100000);
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
}
