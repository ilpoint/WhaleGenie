using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SharpHook.Data;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Keyboard and mouse input posted at the control inside one window as messages. This is what
/// lets a macro work on a window that is not in front: the window is told what happened instead
/// of the desktop being driven. The pointer on screen does not move, so a macro that only posts
/// messages leaves the user's own mouse where it is.
/// </summary>
public sealed class MessageInputDevice(IntPtr window, IInputDevice pointer) : IInputDevice
{
    public InputCoordinateSpace CoordinateSpace => InputCoordinateSpace.WindowClient;

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
    private const uint WmGetDlgCode = 0x0087;
    private const int GwlStyle = -16;
    private const long WsTabStop = 0x00010000;
    private const long DlgcWantAllKeys = 0x0004;
    private const long DlgcWantChars = 0x0080;
    private const uint SmtoAbortIfHung = 0x0002;

    /// <summary>Wheel units in one notch, the amount a wheel normally turns in.</summary>
    private const int WheelNotch = 120;

    /// <summary>
    /// The control a key was pressed into, so its release goes to the same place even if the
    /// target window moves focus while the key is held.
    /// </summary>
    private IntPtr? _keyTarget;
    private IntPtr? _keyboardTarget;
    private IntPtr? _lastMouseTarget;

    public ScreenPoint Cursor => pointer.Cursor;

    public void KeyPress(string key, int holdMs)
    {
        var virtualKey = VirtualKey(key);
        var target = KeyboardTarget();
        Down(target, virtualKey);
        Sleep(holdMs);
        Up(target, virtualKey);
    }

    public void KeyDown(string key)
    {
        var target = KeyboardTarget();
        _keyTarget = target;
        Down(target, VirtualKey(key));
    }

    public void KeyUp(string key)
    {
        var target = _keyTarget ?? KeyboardTarget();
        _keyTarget = null;
        Up(target, VirtualKey(key));
    }

    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
    {
        var target = KeyboardTarget();
        var codes = new List<int>();
        foreach (var key in keys)
        {
            var virtualKey = VirtualKey(key);
            codes.Add(virtualKey);
            Down(target, virtualKey);
        }

        Sleep(holdMs);

        for (var index = codes.Count - 1; index >= 0; index--)
        {
            Up(target, codes[index]);
        }
    }

    public void TypeText(string text, int intervalMs)
    {
        var target = KeyboardTarget();
        foreach (var character in text)
        {
            // A posted character is what a text box acts on, so shifted and localised letters
            // do not have to be worked out from their key codes.
            Post(target, WmChar, character, 1);
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

    /// <summary>
    /// Refused rather than sent: the messages this device posts tell a window where the pointer is,
    /// and an offset is a question about the pointer itself — which this mode never moves. Handing
    /// the offsets to <see cref="MoveMouse"/> is how "200 to the right" arrived as "go to (200, 0)",
    /// so a macro that wants to move by an offset is told to use a mode that has a pointer.
    /// </summary>
    public void MoveMouseRelative(int dx, int dy, int durationMs)
        => throw new DeviceActionException(RunKeys.BackgroundNeedsPointer);

    public void MouseDown(string button, int x, int y) => Press(button, down: true, x, y);

    public void MouseUp(string button, int x, int y) => Press(button, down: false, x, y);

    public void Click(string button, int x, int y, int clicks, int intervalMs)
    {
        var target = MouseTarget(x, y);
        Post(target.Window, WmMouseMove, 0, Packed(target.X, target.Y));
        var times = Math.Max(1, clicks);
        for (var count = 0; count < times; count++)
        {
            Press(target.Window, button, down: true, target.X, target.Y);
            Thread.Sleep(10);
            Press(target.Window, button, down: false, target.X, target.Y);

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
        var target = MouseTarget(x, y);

        // The wheel is the odd one out among the mouse messages: it carries screen
        // coordinates, not client-area ones.
        var at = Packed(target.ScreenX, target.ScreenY);

        // One notch per message, so a part of a notch is sent as a part rather than being
        // rounded up into a whole turn.
        var left = Math.Abs(delta);
        while (left > 0)
        {
            var step = Math.Min(left, WheelNotch);
            left -= step;
            var wheel = ((sign * step) & 0xFFFF) << 16;
            Post(target.Window, horizontal ? WmMouseHWheel : WmMouseWheel, wheel, at);
        }
    }

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
    {
        var target = MouseTarget(startX, startY);
        Press(target.Window, button, down: true, target.X, target.Y);

        var stops = Math.Max(1, steps);
        for (var step = 1; step <= stops; step++)
        {
            var x = startX + (int)((endX - startX) * ((double)step / stops));
            var y = startY + (int)((endY - startY) * ((double)step / stops));
            Post(target.Window, WmMouseMove, 0,
                Packed(target.X + (x - startX), target.Y + (y - startY)));
            Sleep(durationMs / stops);
        }

        Press(target.Window, button, down: false,
            target.X + (endX - startX), target.Y + (endY - startY));
    }

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        if (path.Count == 0)
        {
            return;
        }

        var target = MouseTarget(path[0].X, path[0].Y);
        Press(target.Window, button, down: true, target.X, target.Y);
        var pause = Math.Max(0, durationMs) / Math.Max(1, path.Count - 1);
        for (var index = 1; index < path.Count; index++)
        {
            var stop = path[index];
            Post(target.Window, WmMouseMove, 0,
                Packed(target.X + (stop.X - path[0].X), target.Y + (stop.Y - path[0].Y)));
            Sleep(pause);
        }

        var end = path[^1];
        Press(target.Window, button, down: false,
            target.X + (end.X - path[0].X), target.Y + (end.Y - path[0].Y));
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
            var target = MouseTarget(stop.X, stop.Y);
            Post(target.Window, WmMouseMove, 0, Packed(target.X, target.Y));
            Sleep(pause);
        }
    }

    private void Press(string button, bool down, int x, int y)
    {
        var target = MouseTarget(x, y);
        Post(target.Window, WmMouseMove, 0, Packed(target.X, target.Y));
        Press(target.Window, button, down, target.X, target.Y);
    }

    private static void Press(IntPtr window, string button, bool down, int x, int y)
    {
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
        Post(window, message, extra << 16, Packed(x, y));
    }

    private static void Down(IntPtr target, int virtualKey)
    {
        Post(target, IsAlt(virtualKey) ? WmSysKeyDown : WmKeyDown, virtualKey,
            KeyParameter(virtualKey, down: true));
    }

    private static void Up(IntPtr target, int virtualKey)
    {
        Post(target, IsAlt(virtualKey) ? WmSysKeyUp : WmKeyUp, virtualKey,
            KeyParameter(virtualKey, down: false));
    }

    /// <summary>The virtual key code a macro's key name stands for.</summary>
    private static int VirtualKey(string key)
    {
        // A posted key message carries the code Windows numbers the key with, which is not the code
        // the hook library numbers it with: sending the library's own code would press another key.
        var code = SharpKeys.ResolveCode(key)
            ?? throw new DeviceActionException(RunKeys.UnknownKey, key);

        return WindowsKeys.VirtualKey(code)
            ?? throw new DeviceActionException(RunKeys.UnknownKey, key);
    }

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

    /// <summary>
    /// The control on the target window's GUI thread that would receive a key press: the focused
    /// child when focus is inside the target, and the window itself when the thread holds none.
    /// </summary>
    private IntPtr KeyboardTarget()
    {
        if (FocusedTarget() is { } focused)
        {
            return CacheKeyboardTarget(focused);
        }

        if (_keyboardTarget is { } cached && cached != window && Usable(cached))
        {
            return cached;
        }

        if (_lastMouseTarget is { } clicked && Usable(clicked))
        {
            return CacheKeyboardTarget(clicked);
        }

        if (FindKeyboardChild(wantsChars: true) is { } typed)
        {
            return CacheKeyboardTarget(typed);
        }

        if (FindKeyboardChild(wantsChars: false) is { } tabbed)
        {
            return CacheKeyboardTarget(tabbed);
        }

        return CacheKeyboardTarget(window);
    }

    private IntPtr? FocusedTarget()
    {
        var thread = GetWindowThreadProcessId(window, out _);
        if (thread == 0)
        {
            return null;
        }

        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        // A top-level focus is not a place a character can be typed into. It falls through to
        // the clicked child or the child that answers WM_GETDLGCODE instead.
        return GetGUIThreadInfo(thread, ref info) && Usable(info.Focus) && info.Focus != window
            ? info.Focus
            : null;
    }

    private IntPtr CacheKeyboardTarget(IntPtr target)
    {
        _keyboardTarget = target;
        return target;
    }

    private bool Usable(IntPtr candidate)
        => candidate != IntPtr.Zero && IsWindow(candidate) && IsWindowVisible(candidate)
           && (candidate == window || GetAncestor(candidate, GaRoot) == window);

    /// <summary>
    /// A child that says it wants typed characters is the one a person would be typing into.
    /// The second pass accepts the ordinary focusable control when no child answers that question.
    /// </summary>
    private IntPtr FindKeyboardChild(bool wantsChars)
    {
        var found = IntPtr.Zero;
        bool Visit(IntPtr child, IntPtr _)
        {
            if (!Usable(child))
            {
                return true;
            }

            if (wantsChars ? WantsKeyboard(child) : IsTabStopOrTextClass(child))
            {
                found = child;
                return false;
            }

            return true;
        }

        EnumChildWindows(window, Visit, IntPtr.Zero);
        return found;
    }

    private static bool WantsKeyboard(IntPtr child)
    {
        var sent = SendMessageTimeout(child, WmGetDlgCode, IntPtr.Zero, IntPtr.Zero,
            SmtoAbortIfHung, 20, out var answer);
        return sent != IntPtr.Zero
               && ((long)answer & (DlgcWantChars | DlgcWantAllKeys)) != 0;
    }

    private static bool IsTabStopOrTextClass(IntPtr child)
    {
        var style = GetWindowLongPtr(child, GwlStyle).ToInt64();
        if ((style & WsTabStop) != 0)
        {
            return true;
        }

        var name = new StringBuilder(128);
        GetClassName(child, name, name.Capacity);
        var className = name.ToString();
        return className.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
               || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
               || className.Contains("Scintilla", StringComparison.OrdinalIgnoreCase)
               || className.Contains("Chrome_RenderWidgetHostHWND",
                   StringComparison.OrdinalIgnoreCase)
               || className.Contains("Internet Explorer_Server",
                   StringComparison.OrdinalIgnoreCase)
               || className.Contains("TextBox", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The deepest visible child under a client-area point. Posting at the top-level window would
    /// bypass hit testing, so a standard edit, list or button would never see the click.
    /// </summary>
    private MouseTargetPoint MouseTarget(int x, int y)
    {
        var screen = new NativePoint { X = x, Y = y };
        if (!ClientToScreen(window, ref screen))
        {
            throw Failure(window, 0, Marshal.GetLastWin32Error());
        }

        var target = window;
        var local = new NativePoint { X = x, Y = y };
        while (true)
        {
            var child = ChildWindowFromPointEx(target, local, ChildWindowFlags);
            if (child == IntPtr.Zero || child == target)
            {
                break;
            }

            var childPoint = screen;
            if (!ScreenToClient(child, ref childPoint))
            {
                break;
            }

            target = child;
            local = childPoint;
        }

        if (target == window)
        {
            _lastMouseTarget = null;
            _keyboardTarget = null;
            return new MouseTargetPoint(target, x, y, screen.X, screen.Y);
        }

        if (_lastMouseTarget != target)
        {
            _lastMouseTarget = target;
            _keyboardTarget = null;
        }

        return new MouseTargetPoint(target, local.X, local.Y, screen.X, screen.Y);
    }

    /// <summary>A point packed into a mouse message's lParam.</summary>
    private static int Packed(int x, int y) => (y << 16) | (x & 0xFFFF);

    private static void Post(IntPtr target, uint message, int first, int second)
    {
        if (target == IntPtr.Zero)
        {
            throw Failure(target, message, 1400);
        }

        if (!PostMessage(target, message, (IntPtr)first, (IntPtr)second))
        {
            throw Failure(target, message, Marshal.GetLastWin32Error());
        }
    }

    private static DeviceActionException Failure(IntPtr target, uint message, int error)
        => new(RunKeys.BackgroundPostFailed,
            string.Create(CultureInfo.InvariantCulture,
                $"{target.ToInt64()} · message {message} · error {error}"));

    private static void Sleep(int milliseconds)
    {
        if (milliseconds > 0)
        {
            Thread.Sleep(milliseconds);
        }
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, NativePoint point,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr first,
        IntPtr second, uint flags, uint timeout, out IntPtr answer);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    private const uint GaRoot = 2;
    private const uint CwpSkipInvisible = 0x0001;
    private const uint CwpSkipTransparent = 0x0004;
    private const uint ChildWindowFlags = CwpSkipInvisible | CwpSkipTransparent;

    private readonly record struct MouseTargetPoint(IntPtr Window, int X, int Y, int ScreenX,
        int ScreenY);

    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public NativeRect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
