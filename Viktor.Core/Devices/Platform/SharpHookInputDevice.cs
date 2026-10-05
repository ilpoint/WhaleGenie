using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Sends keyboard and mouse input the way a person would, through the same hook layer the
/// recorder listens on. Runs against the active desktop, so it needs no window of its own.
/// </summary>
public sealed class SharpHookInputDevice : IInputDevice
{
    private const int Notch = 120;

    private static readonly MouseWheelScrollType Scrolling = MouseWheelScrollType.BlockScroll;

    /// <summary>The one simulator this device reuses, since setting one up is not cheap.</summary>
    private readonly IEventSimulator _simulator = EventSimulator.Create("Viktor");

    public ScreenPoint Cursor => WindowsScreenDevice.CursorPosition();

    public void KeyPress(string key, int holdMs)
    {
        var code = Code(key);
        ViktorInputGate.Note(KeyNames.Name(code, sided: true));
        _simulator.SimulateKeyPress(code);
        Rest(holdMs, 50);
        _simulator.SimulateKeyRelease(code);
    }

    public void KeyDown(string key)
    {
        var code = Code(key);
        ViktorInputGate.Note(KeyNames.Name(code, sided: true));
        _simulator.SimulateKeyPress(code);
    }

    public void KeyUp(string key)
    {
        var code = Code(key);
        ViktorInputGate.Note(KeyNames.Name(code, sided: true));
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
            ViktorInputGate.Note(KeyNames.Name(code, sided: true));
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
            ViktorInputGate.Note(character.ToString());
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

    public void MoveMouseRelative(int dx, int dy, int durationMs)
    {
        var from = Cursor;
        Glide(from, new ScreenPoint(from.X + dx, from.Y + dy), durationMs);
    }

    public void MouseDown(string button, int x, int y)
    {
        MoveMouse(x, y, 0);
        var code = Button(button);
        ViktorInputGate.Note(KeyNames.MouseName(code));
        _simulator.SimulateMousePress(code);
    }

    public void MouseUp(string button, int x, int y)
    {
        MoveMouse(x, y, 0);
        var code = Button(button);
        ViktorInputGate.Note(KeyNames.MouseName(code));
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

            ViktorInputGate.Note(KeyNames.MouseName(code));
            _simulator.SimulateMousePress(code);
            Thread.Sleep(10);
            _simulator.SimulateMouseRelease(code);
        }
    }

    public void Scroll(string direction, int amount, int x, int y)
    {
        MoveMouse(x, y, 0);

        var notches = Math.Max(1, amount);
        var (rotation, axis) = direction.Trim().ToLowerInvariant() switch
        {
            "up" => (Notch, MouseWheelScrollDirection.Vertical),
            "left" => (Notch, MouseWheelScrollDirection.Horizontal),
            "right" => (-Notch, MouseWheelScrollDirection.Horizontal),
            _ => (-Notch, MouseWheelScrollDirection.Vertical),
        };

        var notch = KeyNames.WheelName(axis, (short)rotation);
        for (var index = 0; index < notches; index++)
        {
            ViktorInputGate.Note(notch);
            _simulator.SimulateMouseWheel((short)rotation, axis, Scrolling);
            Thread.Sleep(10);
        }
    }

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
    {
        var code = Button(button);
        MoveMouse(startX, startY, 0);
        ViktorInputGate.Note(KeyNames.MouseName(code));
        _simulator.SimulateMousePress(code);

        try
        {
            // The intermediate moves are what tell the target it is being dragged rather
            // than clicked, so at least one of them always happens.
            var count = Math.Clamp(steps, 1, 200);
            var wait = durationMs > 0 ? Math.Max(1, durationMs / count) : 0;
            for (var index = 1; index <= count; index++)
            {
                var ratio = (double)index / count;
                var x = (int)Math.Round(startX + ((endX - startX) * ratio));
                var y = (int)Math.Round(startY + ((endY - startY) * ratio));
                _simulator.SimulateMouseMovement(Clamp(x), Clamp(y));

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

        var steps = Math.Clamp(durationMs / 16, 1, 120);
        var wait = Math.Max(1, durationMs / steps);
        for (var index = 1; index <= steps; index++)
        {
            var ratio = (double)index / steps;
            var x = (int)Math.Round(from.X + ((to.X - from.X) * ratio));
            var y = (int)Math.Round(from.Y + ((to.Y - from.Y) * ratio));
            _simulator.SimulateMouseMovement(Clamp(x), Clamp(y));
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
