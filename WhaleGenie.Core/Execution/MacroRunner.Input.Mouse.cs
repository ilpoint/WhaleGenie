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

// The mouse and the dispatch: scrolling, clicking, the drift over a button, and which step type goes where.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Turns the wheel. "Notches" is the step a wheel normally moves in; "pixels" measures the
    /// same turn finer, with 120 units to a notch, which is what an exact amount needs. A smooth
    /// duration splits the whole turn over several smaller wheel events with a pause between, so
    /// an application that animates its scrolling has the time to follow.
    /// </summary>
    private async Task ScrollMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var direction = Args(step).Text("direction");
        var pixels = string.Equals(Args(step).Text("unit").Trim(), "pixels", StringComparison.OrdinalIgnoreCase);
        var amount = Math.Max(1, Args(step).Number("amount"));
        var total = pixels ? amount : amount * WheelNotch;
        var smooth = Pace(Args(step).Number("smoothMs"));
        var repeats = Math.Max(1, Args(step).Number("repeat"));
        var interval = Pace(Args(step).Number("intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            if (smooth <= 0)
            {
                Input(step).Scroll(direction, total, point.Device.X, point.Device.Y);
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

                Input(step).Scroll(direction, share + (index < extra ? 1 : 0),
                    point.Device.X, point.Device.Y);
            }
        }

        GivePoints(step, point.Screen, point.Window);
    }

    private async Task ClickMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var button = Button(step);
        var clicks = Math.Max(1, Args(step).Number("repeat"));
        var interval = Pace(Args(step).Number("intervalMs"));
        var hold = Pace(Args(step).Number("holdMs"));
        var spread = Spread(step);
        var from = PostsMessages(step) ? null : ArrivedFrom(step);

        // A step that lets the clicks drift, or that wants the pointer put back, has to place every
        // click itself: sending them all at once would put them all on the same pixel, and there
        // would be nowhere to put the pointer back from.
        if (hold <= 0 && spread == 0 && from is null)
        {
            Input(step).Click(button, point.Device.X, point.Device.Y, clicks, interval);
            GivePoints(step, point.Screen, point.Window);
            return;
        }

        var last = point;
        for (var count = 0; count < clicks; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            var at = Scattered(point.Device, spread);
            last = new ResolvedPoint(at, new ScreenPoint(
                point.Screen.X + (at.X - point.Device.X),
                point.Screen.Y + (at.Y - point.Device.Y)), point.Window is { } window
                ? new ScreenPoint(window.X + (at.X - point.Device.X),
                    window.Y + (at.Y - point.Device.Y))
                : null);
            if (hold <= 0)
            {
                Input(step).Click(button, at.X, at.Y, 1, 0);
                continue;
            }

            Input(step).MouseDown(button, at.X, at.Y);
            await Pause(hold, token);
            Input(step).MouseUp(button, at.X, at.Y);
        }

        GivePoints(step, last.Screen, last.Window);

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
        var repeats = Math.Max(1, Args(step).Number("repeat"));
        var interval = Pace(Args(step).Number("intervalMs"));
        var spread = Spread(step);
        var from = PostsMessages(step) ? null : ArrivedFrom(step);

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            var at = Scattered(point.Device, spread);
            var actual = new ResolvedPoint(at, new ScreenPoint(
                point.Screen.X + (at.X - point.Device.X),
                point.Screen.Y + (at.Y - point.Device.Y)), point.Window is { } window
                ? new ScreenPoint(window.X + (at.X - point.Device.X),
                    window.Y + (at.Y - point.Device.Y))
                : null);
            Input(step).Click(Button(step), at.X, at.Y, 2, 0);
            GivePoints(step, actual.Screen, actual.Window);
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

    /// <summary>
    /// Where the pointer was before a step that has to put it back, or null when the step does not
    /// put it back. Posting messages at a window never moves the pointer on screen, so there is
    /// nothing to put back there and nothing is read.
    /// </summary>
    private ScreenPoint? ArrivedFrom(ExecutableStep step)
        => Flag(step, "moveBack", false) && Route(step).Delivery is not InputDelivery.Background
            ? Input(step).Cursor
            : null;


    /// <summary>
    /// The input actions, each answered by the method written for it. A step of
    /// another family is not in this table.
    /// </summary>
    private Dictionary<string, ActionHandler> InputActions
        => _inputActions ??= new(StringComparer.Ordinal)
        {
            ["input.keyPress"] = async (step, depth, token) =>
            {
                await PressKey(step, token);
                return Signal.Normal;
            },

            ["input.waitKey"] = async (step, depth, token) =>
            {
                await WaitForKey(step, token);
                return Signal.Normal;
            },

            ["input.keyDown"] = (step, depth, token) =>
            {
                HoldKey(step);
                return Task.FromResult(Signal.Normal);
            },

            ["input.keyUp"] = (step, depth, token) =>
            {
                LetGoKey(step);
                return Task.FromResult(Signal.Normal);
            },

            ["input.hotkey"] = async (step, depth, token) =>
            {
                await SendHotkey(step, token);
                return Signal.Normal;
            },

            ["input.typeText"] = (step, depth, token) =>
            {
                Input(step).TypeText(Args(step).ValueOf("typedText").AsText(), Pace(Args(step).Number("intervalMs")));
                return Task.FromResult(Signal.Normal);
            },

            ["input.mouseMove"] = (step, depth, token) =>
            {
                MovePointer(step, MovePlace(step));
                return Task.FromResult(Signal.Normal);
            },

            ["input.mouseMoveRelative"] = (step, depth, token) =>
            {
                MovePointerBy(step);
                return Task.FromResult(Signal.Normal);
            },

            ["input.mouseClick"] = async (step, depth, token) =>
            {
                await ClickMouse(step, token);
                return Signal.Normal;
            },

            ["input.mouseDoubleClick"] = async (step, depth, token) =>
            {
                await DoubleClickMouse(step, token);
                return Signal.Normal;
            },

            ["input.keySequence"] = async (step, depth, token) =>
            {
                await SendKeySequence(step, depth, token);
                return Signal.Normal;
            },

            ["input.mouseDown"] = (step, depth, token) =>
            {
                HoldButton(step);
                return Task.FromResult(Signal.Normal);
            },

            ["input.mouseUp"] = (step, depth, token) =>
            {
                LetGoButton(step);
                return Task.FromResult(Signal.Normal);
            },

            ["input.mouseScroll"] = async (step, depth, token) =>
            {
                await ScrollMouse(step, token);
                return Signal.Normal;
            },

            ["input.mouseDrag"] = async (step, depth, token) =>
            {
                await DragPointer(step, token);
                return Signal.Normal;
            },

            ["gamepad.connect"] = (step, depth, token) =>
            {
                _devices.Gamepad.Connect();
                _run.GamepadUsed = true;
                return Task.FromResult(Signal.Normal);
            },

            ["gamepad.button"] = async (step, depth, token) =>
            {
                await PressGamepadButton(step, token);
                return Signal.Normal;
            },

            ["gamepad.stick"] = (step, depth, token) =>
            {
                _devices.Gamepad.Stick(Args(step).Text("stick"), Args(step).Number("x"), Args(step).Number("y"));
                _run.GamepadUsed = true;
                return Task.FromResult(Signal.Normal);
            },

            ["gamepad.trigger"] = (step, depth, token) =>
            {
                _devices.Gamepad.Trigger(Args(step).Text("trigger"), Args(step).Number("amount"));
                _run.GamepadUsed = true;
                return Task.FromResult(Signal.Normal);
            },

            ["gamepad.release"] = (step, depth, token) =>
            {
                _devices.Gamepad.ReleaseAll();
                _run.GamepadUsed = true;
                return Task.FromResult(Signal.Normal);
            },
        };

    private Dictionary<string, ActionHandler>? _inputActions;

}
