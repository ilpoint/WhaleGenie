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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// Moving the pointer, by a step or along a route, and dragging it from one place to another.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Moves the pointer to a point. A straight move is handed to the device exactly as it always
    /// was; a bent or hand-like one is worked out into a path first, because the shape of that
    /// path is the whole point of those styles.
    /// </summary>
    private void MovePointer(ExecutableStep step, ResolvedPoint to)
    {
        var route = MousePath.Route(Args(step).Text("style"));
        var duration = Pace(Args(step).Number("durationMs"));
        if (route is MouseRoute.Direct || PostsMessages(step))
        {
            Input(step).MoveMouse(to.Device.X, to.Device.Y, duration);
            GivePoints(step, to.Screen, to.Window);
            return;
        }

        WalkPointer(Input(step), route, Input(step).Cursor, to.Device, duration);
        GivePoints(step, to.Screen, to.Window);
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

        var dx = Args(step).Number("dx");
        var dy = Args(step).Number("dy");
        var route = MousePath.Route(Args(step).Text("style"));
        var duration = Pace(Args(step).Number("durationMs"));
        if (route is MouseRoute.Direct)
        {
            Input(step).MoveMouseRelative(dx, dy, duration);
            var at = Input(step).Cursor;
            GivePoints(step, at, null);
            return;
        }

        var from = Input(step).Cursor;
        WalkPointer(Input(step), route, from, new ScreenPoint(from.X + dx, from.Y + dy), duration);
        var arrived = Input(step).Cursor;
        GivePoints(step, arrived, null);
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
        var anchor = AnchorOf(step);
        var from = DragPlace(step, "start", anchor);
        var to = DragPlace(step, "end", anchor);
        var duration = Pace(Args(step).Number("durationMs"));
        var settle = Pace(Args(step).Number("settleMs"));
        var steps = Math.Clamp(Args(step).Number("dragSteps"), 1, 200);
        var route = MousePath.Route(Args(step).Text("style"));
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
                Input(step).Drag(Button(step), from.Device.X, from.Device.Y, to.Device.X,
                    to.Device.Y, duration, steps);
            }
            else
            {
                Input(step).DragAlong(Button(step),
                    MousePath.Plan(route, from.Device, to.Device, steps), duration);
            }

            GivePoints(step, from.Screen, from.Window, "screenStartPoint", "windowStartPoint");
            GivePoints(step, to.Screen, to.Window, "screenEndPoint", "windowEndPoint");
            return;
        }

        // Held at the end for a moment before letting go: a slot a thing is dragged into only takes
        // it once the pointer has rested over it, and letting go the instant the pointer arrives is
        // how a drag onto something turns into a drag past it. That is three calls rather than the
        // one the devices offer, because the one they offer lets go before it comes back.
        Input(step).MouseDown(Button(step), from.Device.X, from.Device.Y);
        Input(step).MoveMouseAlong(MousePath.Plan(route, from.Device, to.Device, steps), duration);
        await Pause(settle, token);
        Input(step).MouseUp(Button(step), to.Device.X, to.Device.Y);
        GivePoints(step, from.Screen, from.Window, "screenStartPoint", "windowStartPoint");
        GivePoints(step, to.Screen, to.Window, "screenEndPoint", "windowEndPoint");
    }
}
