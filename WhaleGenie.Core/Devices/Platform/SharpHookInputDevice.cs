using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Sends keyboard and mouse input the way a person would, through the same hook layer the
/// recorder listens on. Runs against the active desktop, so it needs no window of its own.
/// </summary>
public sealed class SharpHookInputDevice : IInputDevice
{
    private const int Notch = 120;

    private static readonly MouseWheelScrollType Scrolling = MouseWheelScrollType.BlockScroll;

    /// <summary>The one simulator this device reuses, since setting one up is not cheap.</summary>
    private readonly IEventSimulator _simulator = EventSimulator.Create("WhaleGenie");

    public ScreenPoint Cursor => WindowsScreenDevice.CursorPosition();

    public void KeyPress(string key, int holdMs)
    {
        var code = Code(key);
        WhaleGenieInputGate.Note(KeyNames.Name(code, sided: true));
        _simulator.SimulateKeyPress(code);
        Rest(holdMs, 50);
        _simulator.SimulateKeyRelease(code);
    }

    public void KeyDown(string key)
    {
        var code = Code(key);
        WhaleGenieInputGate.Note(KeyNames.Name(code, sided: true));
        _simulator.SimulateKeyPress(code);
    }

    public void KeyUp(string key)
    {
        var code = Code(key);
        WhaleGenieInputGate.Note(KeyNames.Name(code, sided: true));
        _simulator.SimulateKeyRelease(code);
    }

    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
    {
        var codes = new List<KeyCode>(keys.Count);
        foreach (var key in keys)
        {
            var code = Code(key);
            codes.Add(code);

            // The gate speaks the names the trigger compares, which keep a modifier's side.
            WhaleGenieInputGate.Note(KeyNames.Name(code, sided: true));
        }

        if (codes.Count == 0)
        {
            throw new DeviceActionException("Run.EmptyChord");
        }

        // Press in order, hold so the target sees the whole chord, release in reverse.
        foreach (var code in codes)
        {
            _simulator.SimulateKeyPress(code);
            Thread.Sleep(10);
        }

        Rest(holdMs, 50);

        for (var index = codes.Count - 1; index >= 0; index--)
        {
            _simulator.SimulateKeyRelease(codes[index]);
        }
    }

    public void TypeText(string text, int intervalMs)
    {
        if (text.Length == 0)
        {
            return;
        }

        // A macro that types the very key another macro listens for should not set it off.
        foreach (var character in text)
        {
            WhaleGenieInputGate.Note(character.ToString());
        }

        if (intervalMs <= 0)
        {
            _simulator.SimulateTextEntry(text);
            return;
        }

        foreach (var character in text)
        {
            _simulator.SimulateTextEntry(character.ToString());
            Thread.Sleep(intervalMs);
        }
    }

    public void MoveMouse(int x, int y, int durationMs) => Glide(Cursor, new ScreenPoint(x, y), durationMs);

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        if (path.Count == 0)
        {
            return;
        }

        if (path.Count == 1 || durationMs <= 0)
        {
            var end = path[^1];
            _simulator.SimulateMouseMovement(Clamp(end.X), Clamp(end.Y));
            return;
        }

        // The path already says which way to go, so the stops are simply sent in order at an
        // even pace: the shape of the path is what makes the pointer bend and ease.
        var wait = Math.Max(1, durationMs / (path.Count - 1));
        for (var index = 1; index < path.Count; index++)
        {
            _simulator.SimulateMouseMovement(Clamp(path[index].X), Clamp(path[index].Y));
            Thread.Sleep(wait);
        }
    }

    public void MoveMouseRelative(int dx, int dy, int durationMs)
    {
        var from = Cursor;
        Glide(from, new ScreenPoint(from.X + dx, from.Y + dy), durationMs);
    }

    public void MouseDown(string button, int x, int y)
    {
        MoveMouse(x, y, 0);
        var code = Button(button);
        WhaleGenieInputGate.Note(KeyNames.MouseName(code));
        _simulator.SimulateMousePress(code);
    }

    public void MouseUp(string button, int x, int y)
    {
        MoveMouse(x, y, 0);
        var code = Button(button);
        WhaleGenieInputGate.Note(KeyNames.MouseName(code));
        _simulator.SimulateMouseRelease(code);
    }

    public void Click(string button, int x, int y, int clicks, int intervalMs)
    {
        var code = Button(button);
        var repeat = Math.Max(1, clicks);
        var gap = intervalMs > 0 ? intervalMs : 30;

        MoveMouse(x, y, 0);
        for (var index = 0; index < repeat; index++)
        {
            if (index > 0)
            {
                Thread.Sleep(gap);
            }

            WhaleGenieInputGate.Note(KeyNames.MouseName(code));
            _simulator.SimulateMousePress(code);
            Thread.Sleep(10);
            _simulator.SimulateMouseRelease(code);
        }
    }

    public void Scroll(string direction, int delta, int x, int y)
    {
        MoveMouse(x, y, 0);

        var (sign, axis) = direction.Trim().ToLowerInvariant() switch
        {
            "up" => (1, MouseWheelScrollDirection.Vertical),
            "left" => (1, MouseWheelScrollDirection.Horizontal),
            "right" => (-1, MouseWheelScrollDirection.Horizontal),
            _ => (-1, MouseWheelScrollDirection.Vertical),
        };

        // One notch per event: a single large jump is ignored by some applications, and the
        // leftover under a notch is what lets a step ask for a part of one.
        var left = Math.Abs(delta);
        while (left > 0)
        {
            var step = Math.Min(left, Notch);
            left -= step;
            var rotation = (short)(sign * step);
            WhaleGenieInputGate.Note(KeyNames.WheelName(axis, rotation));
            _simulator.SimulateMouseWheel(rotation, axis, Scrolling);
            Thread.Sleep(10);
        }
    }

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
        => DragAlong(button, MousePath.Plan(MouseRoute.Direct, new ScreenPoint(startX, startY),
            new ScreenPoint(endX, endY), Math.Clamp(steps, 1, 200)), durationMs);

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        if (path.Count == 0)
        {
            return;
        }

        var code = Button(button);
        MoveMouse(path[0].X, path[0].Y, 0);
        WhaleGenieInputGate.Note(KeyNames.MouseName(code));
        _simulator.SimulateMousePress(code);

        try
        {
            // The intermediate moves are what tell the target it is being dragged rather
            // than clicked, so at least one of them always happens.
            var wait = path.Count > 1 && durationMs > 0 ? Math.Max(1, durationMs / (path.Count - 1)) : 0;
            for (var index = 1; index < path.Count; index++)
            {
                _simulator.SimulateMouseMovement(Clamp(path[index].X), Clamp(path[index].Y));

                if (wait > 0)
                {
                    Thread.Sleep(wait);
                }
            }
        }
        finally
        {
            _simulator.SimulateMouseRelease(code);
        }
    }

    /// <summary>Walks the cursor to a point, or jumps when no duration was asked for.</summary>
    private void Glide(ScreenPoint from, ScreenPoint to, int durationMs)
    {
        if (durationMs <= 0)
        {
            _simulator.SimulateMouseMovement(Clamp(to.X), Clamp(to.Y));
            return;
        }

        var path = MousePath.Plan(MouseRoute.Direct, from, to, MousePath.StepsFor(MouseRoute.Direct, durationMs));
        var wait = Math.Max(1, durationMs / (path.Count - 1));
        for (var index = 1; index < path.Count; index++)
        {
            _simulator.SimulateMouseMovement(Clamp(path[index].X), Clamp(path[index].Y));
            Thread.Sleep(wait);
        }
    }

    private static KeyCode Code(string key)
        => KeyNames.Resolve(key) ?? throw new DeviceActionException("Run.UnknownKey", key);

    private static MouseButton Button(string button) => button.Trim().ToLowerInvariant() switch
    {
        "right" => MouseButton.Button2,
        "middle" => MouseButton.Button3,
        "middle2" or "back" => MouseButton.Button4,
        "forward" => MouseButton.Button5,
        _ => MouseButton.Button1,
    };

    private static short Clamp(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private static void Rest(int milliseconds, int fallback)
        => Thread.Sleep(milliseconds > 0 ? milliseconds : fallback);
}
