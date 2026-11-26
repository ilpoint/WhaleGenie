using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The pointer actions: moving, clicking, scrolling and dragging. Where a point is read through
/// <see cref="IRunPoints"/>, so a click and a drag land in the same place the move before them did;
/// what the pointer does once it is there is this family's own business.
/// </summary>
internal static class PointerActions
{
    /// <summary>Wheel units in one notch, the amount a wheel turns in.</summary>
    private const int WheelNotch = 120;

    /// <summary>
    /// The pointer actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["input.mouseMove"] = (run, step, depth, _) =>
            {
                MovePointer(run, step, run.Points.MovePlace(step));
                return Task.FromResult(Signal.Normal);
            },

            ["input.mouseMoveRelative"] = (run, step, depth, _) =>
            {
                MovePointerBy(run, step);
                return Task.FromResult(Signal.Normal);
            },

            ["input.mouseClick"] = async (run, step, depth, token) =>
            {
                await ClickMouse(run, step, token);
                return Signal.Normal;
            },

            ["input.mouseDoubleClick"] = async (run, step, depth, token) =>
            {
                await DoubleClickMouse(run, step, token);
                return Signal.Normal;
            },

            ["input.mouseScroll"] = async (run, step, depth, token) =>
            {
                await ScrollMouse(run, step, token);
                return Signal.Normal;
            },

            ["input.mouseDrag"] = async (run, step, depth, token) =>
            {
                await DragPointer(run, step, token);
                return Signal.Normal;
            },
        };

    /// <summary>
    /// Turns the wheel. "Notches" is the step a wheel normally moves in; "pixels" measures the
    /// same turn finer, with 120 units to a notch, which is what an exact amount needs. A smooth
    /// duration splits the whole turn over several smaller wheel events with a pause between, so
    /// an application that animates its scrolling has the time to follow.
    /// </summary>
    private static async Task ScrollMouse(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var point = run.Points.Point(step, "x", "y");
        var direction = run.Args(step).Text("direction");
        var pixels = string.Equals(run.Args(step).Text("unit").Trim(), "pixels",
            StringComparison.OrdinalIgnoreCase);
        var amount = Math.Max(1, run.Args(step).Number("amount"));
        var total = pixels ? amount : amount * WheelNotch;
        var smooth = run.Pace(run.Args(step).Number("smoothMs"));
        var repeats = Math.Max(1, run.Args(step).Number("repeat"));
        var interval = run.Pace(run.Args(step).Number("intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pacing.Pause(interval, token);
            }

            foreach (var turn in WheelPlan.Of(total, smooth))
            {
                token.ThrowIfCancellationRequested();
                if (turn.PauseBeforeMs > 0)
                {
                    await Pacing.Pause(turn.PauseBeforeMs, token);
                }

                run.Input(step).Scroll(direction, turn.Units, point.Device.X, point.Device.Y);
            }
        }

        run.Points.GivePoints(step, point.Screen, point.Window);
    }

    private static async Task ClickMouse(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var point = run.Points.Point(step, "x", "y");
        var button = run.Button(step);
        var clicks = Math.Max(1, run.Args(step).Number("repeat"));
        var interval = run.Pace(run.Args(step).Number("intervalMs"));
        var hold = run.Pace(run.Args(step).Number("holdMs"));
        var from = run.Points.PostsMessages(step) ? null : ArrivedFrom(run, step);

        // A step that lets the clicks drift, or that wants the pointer put back, has to place every
        // click itself: sending them all at once would put them all on the same pixel, and there
        // would be nowhere to put the pointer back from.
        if (hold <= 0 && from is null)
        {
            run.Input(step).Click(button, point.Device.X, point.Device.Y, clicks, interval);
            run.Points.GivePoints(step, point.Screen, point.Window);
            return;
        }

        var last = point;
        for (var count = 0; count < clicks; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pacing.Pause(interval, token);
            }

            var at = run.Points.Scatter(step, point.Device);
            last = new ResolvedPoint(at, new ScreenPoint(
                point.Screen.X + (at.X - point.Device.X),
                point.Screen.Y + (at.Y - point.Device.Y)), point.Window is { } window
                ? new ScreenPoint(window.X + (at.X - point.Device.X),
                    window.Y + (at.Y - point.Device.Y))
                : null);
            if (hold <= 0)
            {
                run.Input(step).Click(button, at.X, at.Y, 1, 0);
                continue;
            }

            run.Input(step).MouseDown(button, at.X, at.Y);
            await Pacing.Pause(hold, token);
            run.Input(step).MouseUp(button, at.X, at.Y);
        }

        run.Points.GivePoints(step, last.Screen, last.Window);

        if (from is { } back)
        {
            run.Input(step).MoveMouse(back.X, back.Y, 0);
        }
    }

    /// <summary>
    /// Double clicks the mouse: two presses in quick succession, which is what tells the machine a
    /// double click from two single ones. The step may ask for several of them in a row with a
    /// pause between, which is the same question the other input steps answer with repeat and
    /// interval; there is no hold, because holding the button is what makes it stop being a double
    /// click.
    /// </summary>
    private static async Task DoubleClickMouse(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var point = run.Points.Point(step, "x", "y");
        var repeats = Math.Max(1, run.Args(step).Number("repeat"));
        var interval = run.Pace(run.Args(step).Number("intervalMs"));
        var from = run.Points.PostsMessages(step) ? null : ArrivedFrom(run, step);

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pacing.Pause(interval, token);
            }

            var at = run.Points.Scatter(step, point.Device);
            var actual = new ResolvedPoint(at, new ScreenPoint(
                point.Screen.X + (at.X - point.Device.X),
                point.Screen.Y + (at.Y - point.Device.Y)), point.Window is { } window
                ? new ScreenPoint(window.X + (at.X - point.Device.X),
                    window.Y + (at.Y - point.Device.Y))
                : null);
            run.Input(step).Click(run.Button(step), at.X, at.Y, 2, 0);
            run.Points.GivePoints(step, actual.Screen, actual.Window);
        }

        if (from is { } back)
        {
            run.Input(step).MoveMouse(back.X, back.Y, 0);
        }
    }

    /// <summary>
    /// Where the pointer was before a step that has to put it back, or null when the step does not
    /// put it back. Posting messages at a window never moves the pointer on screen, so there is
    /// nothing to put back there and nothing is read.
    /// </summary>
    private static ScreenPoint? ArrivedFrom(IRunContext run, ExecutableStep step)
        => run.Flag(step, "moveBack", false) && !run.Points.PostsMessages(step)
            ? run.Input(step).Cursor
            : null;

    /// <summary>
    /// Moves the pointer to a point. A straight move is handed to the device exactly as it always
    /// was; a bent or hand-like one is worked out into a path first, because the shape of that
    /// path is the whole point of those styles.
    /// </summary>
    private static void MovePointer(IRunContext run, ExecutableStep step, ResolvedPoint to)
    {
        var route = MousePath.Route(run.Args(step).Text("style"));
        var duration = run.Pace(run.Args(step).Number("durationMs"));
        if (route is MouseRoute.Direct || run.Points.PostsMessages(step))
        {
            run.Input(step).MoveMouse(to.Device.X, to.Device.Y, duration);
            run.Points.GivePoints(step, to.Screen, to.Window);
            return;
        }

        WalkPointer(run, step, route, run.Input(step).Cursor, to.Device, duration);
        run.Points.GivePoints(step, to.Screen, to.Window);
    }

    /// <summary>The same, for a move written as an offset from wherever the pointer is.</summary>
    private static void MovePointerBy(IRunContext run, ExecutableStep step)
    {
        // "By how much" is a question about the pointer, and the posted-input mode never moves the
        // pointer: it tells the window what happened, so the pointer a relative move counts from is
        // not its own. Sending the offsets on anyway is how "move 200 to the right" becomes "go to
        // the point (200, 0)" — the wrong place, arrived at silently — so this mode says what it
        // cannot do instead, whichever way the move was asked to travel.
        if (run.Points.PostsMessages(step))
        {
            throw new StepFailure(RunKeys.BackgroundNeedsPointer);
        }

        var dx = run.Args(step).Number("dx");
        var dy = run.Args(step).Number("dy");
        var route = MousePath.Route(run.Args(step).Text("style"));
        var duration = run.Pace(run.Args(step).Number("durationMs"));
        if (route is MouseRoute.Direct)
        {
            run.Input(step).MoveMouseRelative(dx, dy, duration);
            var at = run.Input(step).Cursor;
            run.Points.GivePoints(step, at, null);
            return;
        }

        var from = run.Input(step).Cursor;
        WalkPointer(run, step, route, from, new ScreenPoint(from.X + dx, from.Y + dy), duration);
        var arrived = run.Input(step).Cursor;
        run.Points.GivePoints(step, arrived, null);
    }

    private static void WalkPointer(IRunContext run, ExecutableStep step, MouseRoute route,
        ScreenPoint from, ScreenPoint to, int duration)
    {
        if (duration <= 0)
        {
            duration = run.Pace(MousePath.DefaultMoveMs);
        }

        run.Input(step).MoveMouseAlong(
            MousePath.Plan(route, from, to, MousePath.StepsFor(route, duration),
                run.Evaluation.Random), duration);
    }

    /// <summary>Presses, travels, and lets go: the same shape of path as a plain move.</summary>
    private static async Task DragPointer(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        // Both ends are measured from the same corner, so a drag across a window stays inside it —
        // and a record handed to one of them says where on the screen it is, so it comes back
        // already in screen pixels rather than moved by that corner a second time. The corner is
        // read once, here, rather than once per end.
        var anchor = run.Points.AnchorOf(step);
        var from = run.Points.DragPlace(step, "start", anchor);
        var to = run.Points.DragPlace(step, "end", anchor);
        var duration = run.Pace(run.Args(step).Number("durationMs"));
        var settle = run.Pace(run.Args(step).Number("settleMs"));
        var steps = Math.Clamp(run.Args(step).Number("dragSteps"), 1, 200);
        var route = MousePath.Route(run.Args(step).Text("style"));
        if (route is not MouseRoute.Direct && duration <= 0)
        {
            duration = run.Pace(MousePath.DefaultMoveMs);
        }

        if (settle <= 0)
        {
            if (route is MouseRoute.Direct)
            {
                run.Input(step).Drag(run.Button(step), from.Device.X, from.Device.Y, to.Device.X,
                    to.Device.Y, duration, steps);
            }
            else
            {
                run.Input(step).DragAlong(run.Button(step),
                    MousePath.Plan(route, from.Device, to.Device, steps, run.Evaluation.Random),
                    duration);
            }

            run.Points.GivePoints(step, from.Screen, from.Window, "screenStartPoint",
                "windowStartPoint");
            run.Points.GivePoints(step, to.Screen, to.Window, "screenEndPoint", "windowEndPoint");
            return;
        }

        // Held at the end for a moment before letting go: a slot a thing is dragged into only takes
        // it once the pointer has rested over it, and letting go the instant the pointer arrives is
        // how a drag onto something turns into a drag past it. That is three calls rather than the
        // one the devices offer, because the one they offer lets go before it comes back.
        run.Input(step).MouseDown(run.Button(step), from.Device.X, from.Device.Y);
        run.Input(step).MoveMouseAlong(
            MousePath.Plan(route, from.Device, to.Device, steps, run.Evaluation.Random), duration);
        await Pacing.Pause(settle, token);
        run.Input(step).MouseUp(run.Button(step), to.Device.X, to.Device.Y);
        run.Points.GivePoints(step, from.Screen, from.Window, "screenStartPoint",
            "windowStartPoint");
        run.Points.GivePoints(step, to.Screen, to.Window, "screenEndPoint", "windowEndPoint");
    }
}
