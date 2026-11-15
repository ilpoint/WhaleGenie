using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

// Where a step's point is and which way its input travels. Numbers may be counted from the screen
// or from a window's client area, a record may carry its own basis, and a step may post at one
// window instead of driving the desktop — all of it read here, so keys and the mouse agree.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Where the pixel a step watches sits on the screen, window anchoring and all, with the window
    /// the point was counted from when there was one, which is also the window its colour has to be
    /// read out of.
    /// </summary>
    private (ScreenPoint Corner, WindowInfo? Window) Spot(ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        var point = RecordedPlaceAndBasis(step, "place") is { } place
            ? Resolve(step, place.Point, place.Basis, anchor).Screen
            : Placed(anchor.Corner, Args(step).Number("x"), Args(step).Number("y"));
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
    private ResolvedPoint Point(ExecutableStep step, string xName, string yName)
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
    private ResolvedPoint MovePlace(ExecutableStep step)
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
            var window = knownAnchor?.Window ?? BackgroundWindow(step);
            var origin = _devices.Windows.ClientOrigin(window.Handle);
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
            ?? throw new StepFailure(RunKeys.WindowNotFound, WindowText(step));
        var screen = new ScreenPoint(originOfWindow.X + point.X, originOfWindow.Y + point.Y);
        return new ResolvedPoint(screen, screen, point);
    }

    /// <summary>
    /// Hands over the point a step actually used in the spaces it knows. A point from a window
    /// action carries both; a foreground action with no window carries only the screen point.
    /// </summary>
    private void GivePoints(ExecutableStep step, ScreenPoint screen, ScreenPoint? window,
        string screenKey = "screenPoint", string windowKey = "windowPoint")
    {
        if (Wants(step, screenKey))
        {
            Give(step, screenKey, PointRecord(screen, "screen"));
        }

        if (!Wants(step, windowKey))
        {
            return;
        }

        if ((window ?? WindowPointAt(step, screen)) is { } client)
        {
            Give(step, windowKey, PointRecord(client, "window"));
        }
    }

    /// <summary>The same screen point counted from the step's window-client origin, when it has one.</summary>
    private ScreenPoint? WindowPointAt(ExecutableStep step, ScreenPoint screen)
    {
        try
        {
            var origin = PostsMessages(step)
                ? _devices.Windows.ClientOrigin(BackgroundWindow(step).Handle)
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

    /// <summary>Hands over a screen point and, when the reading came from a window, its client point.</summary>
    private void GivePointAt(ExecutableStep step, ScreenPoint screen, WindowInfo? window)
    {
        if (!Wants(step, "screenPoint") && !Wants(step, "windowPoint"))
        {
            return;
        }

        if (!Wants(step, "windowPoint"))
        {
            GivePoints(step, screen, null);
            return;
        }

        var client = window is null
            ? WindowPointAt(step, screen)
            : new ScreenPoint(screen.X - _devices.Windows.ClientOrigin(window.Handle).X,
                screen.Y - _devices.Windows.ClientOrigin(window.Handle).Y);
        GivePoints(step, screen, client);
    }

    /// <summary>One point as a record a later position field can read directly.</summary>
    private static Value PointRecord(ScreenPoint point, string basis)
        => Value.FromRecord(
        [
            (FieldNames.X, Value.FromNumber(point.X)),
            (FieldNames.Y, Value.FromNumber(point.Y)),
            (FieldNames.Basis, Value.FromText(basis)),
        ]);

    /// <summary>The window a background step posts at, or a failure naming the missing rule.</summary>
    private WindowInfo BackgroundWindow(ExecutableStep step)
    {
        var rule = WindowRuleOf(step);
        if (rule.MatchValue.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingTargetWindow);
        }

        return _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                   rule.Options)
               ?? throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);
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
    private ResolvedPoint DragPlace(ExecutableStep step, string end,
        (ScreenPoint? Corner, WindowInfo? Window) anchor)
    {
        return RecordedPlaceAndBasis(step, end + "Place") is { } place
            ? Resolve(step, place.Point, place.Basis, anchor)
            : Resolve(step, new ScreenPoint(Args(step).Number(end + "X"), Args(step).Number(end + "Y")),
                PostsMessages(step) ? "window" : "step", anchor);
    }
    /// <summary>
    /// The same, together with the window those numbers were counted from when there was one. The
    /// window is also where the step's pixels are to be read from: a step that says "a hundred
    /// pixels into the game window" and a step that says "read the game window" are saying the same
    /// thing about which window, and one of them should not have to say it twice.
    /// </summary>
    private (ScreenPoint? Corner, WindowInfo? Window) AnchorOf(ExecutableStep step)
    {
        var mode = Args(step).Text("anchorMode").Trim().ToLowerInvariant();
        if (mode.Length > 0 && mode is not ("screen" or "window"))
        {
            throw new StepFailure(RunKeys.BadOption, "anchorMode");
        }

        if (mode != "window")
        {
            return (null, null);
        }

        // The window is recognised by the same captured rule every window action reads.
        var rule = WindowRuleOf(step);
        if (!rule.HasTarget)
        {
            var front = _devices.Windows.Foreground();
            return (front is { } there ? _devices.Windows.ClientOrigin(there.Handle) : null, front);
        }

        var window = _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                         rule.Options)
                     ?? throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);

        var corner = _devices.Windows.ClientOrigin(window.Handle);
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

        var shot = Shot(point.X, point.Y, 1, 1, window);
        if (shot.Origin != point)
        {
            // The picture came back from somewhere else, which means the window does not reach
            // that far: there is no such pixel, and reading a neighbour's colour would be a lie.
            throw new StepFailure(RunKeys.PixelOutsideScreen, $"{point.X},{point.Y}");
        }

        return shot.Frame[0, 0];
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
        var rule = WindowRuleOf(step);
        if (rule.MatchValue.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingTargetWindow);
        }

        var window = _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
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
