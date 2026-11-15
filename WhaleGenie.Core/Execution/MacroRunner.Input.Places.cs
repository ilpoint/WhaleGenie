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

// Turning what a step wrote into a point: screen or window numbers, drag corners, and what is handed back.
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

    /// <summary>A point as the device needs it and in both coordinate spaces a macro may read.</summary>
    private readonly record struct ResolvedPoint(ScreenPoint Device, ScreenPoint Screen,
        ScreenPoint? Window);

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
}
