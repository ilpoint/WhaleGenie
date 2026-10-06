using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using SharpHook.Data;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Keyboard and mouse input posted at one window as messages. This is what lets a macro work on
/// a window that is not in front: the window is told what happened instead of the desktop being
/// driven. The pointer on screen does not move, so a macro that only posts messages leaves the
/// user's own mouse where it is.
/// </summary>
public sealed class MessageInputDevice(IntPtr window, IInputDevice pointer) : IInputDevice
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmXButtonUp = 0x020C;
    private const uint WmMouseHWheel = 0x020E;

    private const int VkAlt = 0x12;
    private const int VkMenu = 0xA5;

    /// <summary>Wheel units in one notch, the amount a wheel normally turns in.</summary>
    private const int WheelNotch = 120;

    /// <summary>Keys held down right now, so a chord can be released in the order it was pressed.</summary>
    private readonly List<int> _held = [];

    public ScreenPoint Cursor => pointer.Cursor;

    public void KeyPress(string key, int holdMs)
    {
        var virtualKey = VirtualKey(key);
        Down(virtualKey);
        Sleep(holdMs);
        Up(virtualKey);
    }

    public void KeyDown(string key) => Down(VirtualKey(key));

    public void KeyUp(string key) => Up(VirtualKey(key));

    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
    {
        var codes = new List<int>();
        foreach (var key in keys)
        {
            var virtualKey = VirtualKey(key);
            codes.Add(virtualKey);
            Down(virtualKey);
        }

        Sleep(holdMs);

        for (var index = codes.Count - 1; index >= 0; index--)
        {
            Up(codes[index]);
        }
    }

    public void TypeText(string text, int intervalMs)
    {
        foreach (var character in text)
        {
            // A posted character is what a text box acts on, so shifted and localised letters
            // do not have to be worked out from their key codes.
            Post(WmChar, character, 1);
            Sleep(intervalMs);
        }
    }

    public void MoveMouse(int x, int y, int durationMs) => Hover([new ScreenPoint(x, y)], durationMs);

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        // The first point is where the pointer already is, so only the stops after it are sent.
        if (path.Count > 1)
        {
            Hover(Tail(path), durationMs);
        }
    }

    public void MoveMouseRelative(int dx, int dy, int durationMs) => MoveMouse(dx, dy, durationMs);

    public void MouseDown(string button, int x, int y) => Press(button, down: true, x, y);

    public void MouseUp(string button, int x, int y) => Press(button, down: false, x, y);

    public void Click(string button, int x, int y, int clicks, int intervalMs)
    {
        var times = Math.Max(1, clicks);
        for (var count = 0; count < times; count++)
        {
            MouseDown(button, x, y);
            Thread.Sleep(10);
            MouseUp(button, x, y);

            if (count + 1 < times)
            {
                Sleep(intervalMs);
            }
        }
    }

    public void Scroll(string direction, int delta, int x, int y)
    {
        var horizontal = direction is "left" or "right";
        var sign = direction is "up" or "right" ? 1 : -1;

        // The wheel is the odd one out among the mouse messages: it carries screen
        // coordinates, not client-area ones.
        var at = (y << 16) | (x & 0xFFFF);

        // One notch per message, so a part of a notch is sent as a part rather than being
        // rounded up into a whole turn.
        var left = Math.Abs(delta);
        while (left > 0)
        {
            var step = Math.Min(left, WheelNotch);
            left -= step;
            var wheel = ((sign * step) & 0xFFFF) << 16;
            Post(horizontal ? WmMouseHWheel : WmMouseWheel, wheel, at);
        }
    }

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
    {
        MouseDown(button, startX, startY);

        var stops = Math.Max(1, steps);
        for (var step = 1; step <= stops; step++)
        {
            var x = startX + (int)((endX - startX) * ((double)step / stops));
            var y = startY + (int)((endY - startY) * ((double)step / stops));
            Post(WmMouseMove, 0, Client(x, y));
            Sleep(durationMs / stops);
        }

        MouseUp(button, endX, endY);
    }

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        if (path.Count == 0)
        {
            return;
        }

        MouseDown(button, path[0].X, path[0].Y);
        Hover(Tail(path), durationMs);
        MouseUp(button, path[^1].X, path[^1].Y);
    }

    /// <summary>The stops after the first, which is where the pointer already is.</summary>
    private static List<ScreenPoint> Tail(IReadOnlyList<ScreenPoint> path)
    {
        var rest = new List<ScreenPoint>(Math.Max(0, path.Count - 1));
        for (var index = 1; index < path.Count; index++)
        {
            rest.Add(path[index]);
        }

        return rest;
    }

    /// <summary>Sends the mouse to each point without pressing anything.</summary>
    private void Hover(IReadOnlyList<ScreenPoint> stops, int durationMs)
    {
        if (stops.Count == 0)
        {
            return;
        }

        var pause = Math.Max(0, durationMs) / stops.Count;
        foreach (var stop in stops)
        {
            Post(WmMouseMove, 0, Client(stop.X, stop.Y));
            Sleep(pause);
        }
    }

    private void Press(string button, bool down, int x, int y)
    {
        Post(WmMouseMove, 0, Client(x, y));

        var (message, extra) = button.ToLowerInvariant() switch
        {
            "right" => (down ? WmRButtonDown : WmRButtonUp, 0),
            "middle" => (down ? WmMButtonDown : WmMButtonUp, 0),
            "back" => (down ? WmXButtonDown : WmXButtonUp, 1),
            "forward" => (down ? WmXButtonDown : WmXButtonUp, 2),
            _ => (down ? WmLButtonDown : WmLButtonUp, 0),
        };

        // The two extra buttons carry their number in the high word of the message's first
        // argument; the ordinary ones carry the keyboard state there, which nothing reads.
        Post(message, extra << 16, Client(x, y));
    }

    private void Down(int virtualKey)
    {
        if (!_held.Contains(virtualKey))
        {
            _held.Add(virtualKey);
        }

        Post(IsAlt(virtualKey) ? WmSysKeyDown : WmKeyDown, virtualKey,
            KeyParameter(virtualKey, down: true));
    }

    private void Up(int virtualKey)
    {
        _held.Remove(virtualKey);

        Post(IsAlt(virtualKey) ? WmSysKeyUp : WmKeyUp, virtualKey,
            KeyParameter(virtualKey, down: false));
    }

    /// <summary>The virtual key code a macro's key name stands for.</summary>
    private static int VirtualKey(string key)
        => KeyNames.Resolve(key) is { } code
            ? (int)code
            : throw new DeviceActionException("Run.UnknownKey", key);

    private static bool IsAlt(int virtualKey) => virtualKey is VkAlt or VkMenu;

    /// <summary>The state word a key message carries: scan code, repeat count, extended flag.</summary>
    private static int KeyParameter(int virtualKey, bool down)
    {
        var scan = (int)(MapVirtualKey((uint)virtualKey, 0) & 0xFF);
        var bits = 1 | (scan << 16);

        if (IsExtended(virtualKey))
        {
            bits |= 1 << 24;
        }

        if (!down)
        {
            bits |= unchecked((int)0xC0000000);
        }

        return bits;
    }

    /// <summary>The keys that carry the extended flag, which tells them apart from numpad keys.</summary>
    private static bool IsExtended(int virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    /// <summary>A screen point as the client-area point the window expects.</summary>
    private int Client(int x, int y)
    {
        var point = new Point { X = x, Y = y };
        ScreenToClient(window, ref point);
        return (point.Y << 16) | (point.X & 0xFFFF);
    }

    private void Post(uint message, int first, int second)
        => PostMessage(window, message, (IntPtr)first, (IntPtr)second);

    private static void Sleep(int milliseconds)
    {
        if (milliseconds > 0)
        {
            Thread.Sleep(milliseconds);
        }
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr window, ref Point point);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
