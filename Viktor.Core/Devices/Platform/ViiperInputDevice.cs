using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;
using Viiper.Client.Devices.Keyboard;
using Viiper.Client.Devices.Mouse;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Keyboard and mouse input sent through a virtual USB device, so the machine sees input that
/// really came from hardware. This is the third way a step can ask for its input: the window in
/// front, messages posted at one window, or this.
/// </summary>
/// <remarks>
/// Two things about a virtual device shape everything below. Its mouse says how far to move rather
/// than where to be, so a move is worked out as a distance from wherever the pointer is now. Its
/// keyboard report is the whole keyboard rather than a change to it, so what is held down is kept
/// here and sent again with every key.
///
/// One report at a time: the virtual keyboard and mouse are single pieces of hardware, so two
/// macros sending at once would type on top of each other.
/// </remarks>
public sealed class ViiperInputDevice : IInputDevice, IDisposable
{
    /// <summary>Wheel units in one notch, the amount a wheel normally turns in.</summary>
    private const int Notch = 120;

    /// <summary>How long a key stays down when the step asked for no time in particular.</summary>
    private const int LeastHoldMs = 50;

    /// <summary>How long a key stays down while text is typed, one character at a time.</summary>
    private const int TypingHoldMs = 10;

    /// <summary>How long a button stays down for a click.</summary>
    private const int ClickHoldMs = 10;

    /// <summary>Pause between the keys of a chord, so the target sees it being put together.</summary>
    private const int ChordGapMs = 10;

    /// <summary>Pause between clicks that did not ask for one.</summary>
    private const int ClickGapMs = 30;

    /// <summary>Pause between wheel notches.</summary>
    private const int NotchGapMs = 10;

    /// <summary>
    /// How long a report needs before the machine has moved the pointer by it. A report is polled
    /// rather than applied on the spot, so reading the pointer straight after sending one hands
    /// back where it was before it — and a move measured from there lands short.
    /// </summary>
    private const int SettleMs = 30;

    private readonly Func<IViiperLink> _connect;

    private readonly object _gate = new();

    /// <summary>The keys held down right now, in the order they were pressed.</summary>
    private readonly List<byte> _keys = [];

    private IViiperLink? _link;
    private byte _modifiers;
    private byte _buttons;
    private long _movedAt;

    /// <summary>Connects to the server on this machine the first time a step needs it.</summary>
    public ViiperInputDevice()
        : this(ViiperLink.Open)
    {
    }

    /// <summary>
    /// The same, with the way to the server handed in, so a check can watch what the device sends
    /// without a server to send it to.
    /// </summary>
    public ViiperInputDevice(Func<IViiperLink> connect) => _connect = connect;

    public ScreenPoint Cursor
    {
        get
        {
            lock (_gate)
            {
                return Pointer();
            }
        }
    }

    public void KeyPress(string key, int holdMs)
    {
        lock (_gate)
        {
            var code = Code(key);
            Note(code);
            Apply(code, down: true);
            SendKeyboard();
            Sleep(holdMs > 0 ? holdMs : LeastHoldMs);
            Apply(code, down: false);
            SendKeyboard();
        }
    }

    public void KeyDown(string key) => Hold(key, down: true);

    public void KeyUp(string key) => Hold(key, down: false);

    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
    {
        if (keys.Count == 0)
        {
            throw new DeviceActionException("Run.EmptyChord");
        }

        lock (_gate)
        {
            // Every key is looked up before any is pressed, so a chord with a name that means
            // nothing fails without half of it being down already.
            var codes = new List<KeyCode>(keys.Count);
            foreach (var key in keys)
            {
                codes.Add(Code(key));
            }

            foreach (var code in codes)
            {
                Note(code);
                Apply(code, down: true);
                SendKeyboard();
                Sleep(ChordGapMs);
            }

            Sleep(holdMs > 0 ? holdMs : LeastHoldMs);

            for (var index = codes.Count - 1; index >= 0; index--)
            {
                Apply(codes[index], down: false);
                SendKeyboard();
            }
        }
    }

    public void TypeText(string text, int intervalMs)
    {
        if (text.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var character in text)
            {
                if (ViiperKeys.Typing(character) is not { } wanted)
                {
                    throw new DeviceActionException("Run.NoDriverTyping", character.ToString());
                }

                // The gate speaks characters here, because a character is what the recorder and
                // the triggers compare typed text against.
                ViktorInputGate.Note(character.ToString());

                var modifiers = wanted.Shift ? (byte)(_modifiers | (byte)Mod.LeftShift) : _modifiers;
                SendKeyboard(modifiers, [(byte)wanted.Key]);
                Sleep(TypingHoldMs);
                SendKeyboard(_modifiers, []);
                Sleep(intervalMs);
            }
        }
    }

    public void MoveMouse(int x, int y, int durationMs)
    {
        lock (_gate)
        {
            Glide(new ScreenPoint(x, y), durationMs);
        }
    }

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        if (path.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            // The path starts where the pointer was when it was planned. Going there first costs
            // one report and makes the last point land exactly, wherever the pointer really was.
            var from = Pointer();
            Push(path[0].X - from.X, path[0].Y - from.Y);

            if (path.Count > 1 && durationMs > 0)
            {
                Walk(path, Math.Max(1, durationMs / (path.Count - 1)));
            }
        }
    }

    public void MoveMouseRelative(int dx, int dy, int durationMs)
    {
        lock (_gate)
        {
            var from = Pointer();
            Glide(new ScreenPoint(from.X + dx, from.Y + dy), durationMs);
        }
    }

    public void MouseDown(string button, int x, int y) => Press(button, down: true, x, y);

    public void MouseUp(string button, int x, int y) => Press(button, down: false, x, y);

    public void Click(string button, int x, int y, int clicks, int intervalMs)
    {
        lock (_gate)
        {
            var (bit, name) = Button(button);
            Glide(new ScreenPoint(x, y), 0);

            var times = Math.Max(1, clicks);
            var gap = intervalMs > 0 ? intervalMs : ClickGapMs;
            for (var count = 0; count < times; count++)
            {
                if (count > 0)
                {
                    Sleep(gap);
                }

                ViktorInputGate.Note(name);
                _buttons |= bit;
                Send(0, 0);
                Sleep(ClickHoldMs);
                _buttons &= (byte)~bit;
                Send(0, 0);
            }
        }
    }

    public void Scroll(string direction, int delta, int x, int y)
    {
        lock (_gate)
        {
            Glide(new ScreenPoint(x, y), 0);

            var (sign, vertical) = direction.Trim().ToLowerInvariant() switch
            {
                "up" => (1, true),
                "left" => (-1, false),
                "right" => (1, false),
                _ => (-1, true),
            };

            var left = Math.Abs(delta);
            var notches = Math.Max(1, (left + (Notch / 2)) / Notch);
            for (var turned = 0; turned < notches; turned++)
            {
                // One notch per report: the device counts in whole notches and the machine turns
                // each of them into a wheel message of 120 units, which is the unit a step asks in.
                // A step that asked for part of a notch still gets a whole one, because a partial
                // notch cannot be said at all.
                var turn = (short)sign;

                // The name is the one the recorder hears, which counts up as a turn towards the
                // left, while the virtual device counts up as a turn towards the right.
                ViktorInputGate.Note(KeyNames.WheelName(
                    vertical ? MouseWheelScrollDirection.Vertical : MouseWheelScrollDirection.Horizontal,
                    vertical ? turn : (short)-turn));

                Send(0, 0, vertical ? turn : (short)0, vertical ? (short)0 : turn);
                Sleep(NotchGapMs);
            }
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

        lock (_gate)
        {
            var (bit, name) = Button(button);
            Glide(path[0], 0);
            ViktorInputGate.Note(name);
            _buttons |= bit;
            Send(0, 0);

            try
            {
                Walk(path, path.Count > 1 && durationMs > 0
                    ? Math.Max(1, durationMs / (path.Count - 1))
                    : 0);
            }
            finally
            {
                // Let go whatever happened, or the machine is left with a button held that nobody
                // is going to release.
                _buttons &= (byte)~bit;
                Send(0, 0);
            }
        }
    }

    /// <summary>Closes the connection, which takes the virtual keyboard and mouse off the machine.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _link?.Dispose();
            _link = null;
        }
    }

    private void Hold(string key, bool down)
    {
        lock (_gate)
        {
            var code = Code(key);
            Note(code);
            Apply(code, down);
            SendKeyboard();
        }
    }

    private void Press(string button, bool down, int x, int y)
    {
        lock (_gate)
        {
            var (bit, name) = Button(button);
            Glide(new ScreenPoint(x, y), 0);

            ViktorInputGate.Note(name);
            _buttons = down ? (byte)(_buttons | bit) : (byte)(_buttons & ~bit);
            Send(0, 0);
        }
    }

    /// <summary>Walks the pointer to one point, or sends the whole distance at once.</summary>
    private void Glide(ScreenPoint to, int durationMs)
    {
        var from = Pointer();
        if (durationMs <= 0)
        {
            Push(to.X - from.X, to.Y - from.Y);
            return;
        }

        var stops = MousePath.StepsFor(MouseRoute.Direct, durationMs);
        Walk(MousePath.Plan(MouseRoute.Direct, from, to, stops), durationMs / stops);
    }

    /// <summary>
    /// Follows the stops of a path, one report each, at an even pace. The first stop is where the
    /// pointer already is, so only the ones after it are sent.
    /// </summary>
    private void Walk(IReadOnlyList<ScreenPoint> path, int wait)
    {
        for (var index = 1; index < path.Count; index++)
        {
            Push(path[index].X - path[index - 1].X, path[index].Y - path[index - 1].Y);
            Sleep(wait);
        }
    }

    /// <summary>Sends how far to move; the machine does the adding up.</summary>
    private void Push(int dx, int dy) => Send(Clamp(dx), Clamp(dy));

    /// <summary>Sends one mouse report, and notes that the pointer is on its way to somewhere.</summary>
    private void Send(short dx, short dy, short wheel = 0, short pan = 0)
    {
        Link().SendMouse(_buttons, dx, dy, wheel, pan);
        _movedAt = Environment.TickCount64;
    }

    /// <summary>Sends the whole picture of the keyboard, which is what a keyboard report is.</summary>
    private void SendKeyboard() => SendKeyboard(_modifiers, _keys);

    private void SendKeyboard(byte modifiers, IReadOnlyList<byte> keys)
        => Link().SendKeyboard(modifiers, keys);

    /// <summary>
    /// Where the pointer is, once the reports already sent have had time to take effect; reading it
    /// any sooner would hand back where the pointer was before them.
    /// </summary>
    private ScreenPoint Pointer()
    {
        if (_movedAt != 0)
        {
            Sleep((int)Math.Max(0, SettleMs - (Environment.TickCount64 - _movedAt)));
        }

        return Link().Cursor;
    }

    /// <summary>Puts a key down or takes it up in the state the next report will carry.</summary>
    private void Apply(KeyCode code, bool down)
    {
        if (ViiperKeys.Modifier(code) is { } modifier)
        {
            var bit = (byte)modifier;
            _modifiers = down ? (byte)(_modifiers | bit) : (byte)(_modifiers & ~bit);
            return;
        }

        if (ViiperKeys.Usage(code) is { } usage)
        {
            var id = (byte)usage;
            _keys.Remove(id);
            if (down)
            {
                _keys.Add(id);
            }

            return;
        }

        throw new DeviceActionException("Run.NoDriverKey", KeyNames.Name(code, sided: true));
    }

    /// <summary>The code a macro's key name stands for.</summary>
    private static KeyCode Code(string key)
        => KeyNames.Resolve(key) ?? throw new DeviceActionException("Run.UnknownKey", key);

    /// <summary>Tells the parts of the app that watch the keyboard that this press is Viktor's own.</summary>
    private static void Note(KeyCode code) => ViktorInputGate.Note(KeyNames.Name(code, sided: true));

    /// <summary>The virtual mouse's bit for a button, and the name Viktor calls that button by.</summary>
    private static (byte Bit, string Name) Button(string button) => button.Trim().ToLowerInvariant() switch
    {
        "right" => ((byte)Btn.Right, KeyNames.MouseName(MouseButton.Button2)),
        "middle" => ((byte)Btn.Middle, KeyNames.MouseName(MouseButton.Button3)),
        "middle2" or "back" => ((byte)Btn.Back, KeyNames.MouseName(MouseButton.Button4)),
        "forward" => ((byte)Btn.Forward, KeyNames.MouseName(MouseButton.Button5)),
        _ => ((byte)Btn.Left, KeyNames.MouseName(MouseButton.Button1)),
    };

    /// <summary>The most one report has room for.</summary>
    private static short Clamp(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private static void Sleep(int milliseconds)
    {
        if (milliseconds > 0)
        {
            Thread.Sleep(milliseconds);
        }
    }

    /// <summary>
    /// The connection to the server, opened the first time a step needs it. Opening it is what
    /// fails when the driver or the server is missing, so it happens here rather than when the
    /// device is made — and the run reports it on the step that asked for driver-level input.
    /// </summary>
    private IViiperLink Link()
    {
        if (_link is not null)
        {
            return _link;
        }

        var link = _connect();
        try
        {
            Wake(link);
        }
        catch
        {
            link.Dispose();
            throw;
        }

        _link = link;
        return link;
    }

    /// <summary>
    /// Waits for the virtual keyboard and mouse to be devices the machine is really listening to.
    /// A device that has just been put on the machine is not one yet: report after report goes
    /// nowhere for a quarter of a second, and a macro's first move would silently not happen.
    ///
    /// The check is a nudge of one pixel that is taken straight back, so the pointer ends where it
    /// started. It is also the only way to tell a device that never attaches at all — which is what
    /// a VIIPER server started without its auto-attach flag leaves behind — from one that works.
    /// </summary>
    private static void Wake(IViiperLink link)
    {
        var at = link.Cursor;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            // Both ways round, because a pointer sitting against an edge of the screen has nowhere
            // to go in that direction.
            if (Nudge(link, at, 1) || Nudge(link, at, -1))
            {
                return;
            }
        }

        throw new DeviceActionException("Run.NoDriverAttached");
    }

    /// <summary>Moves the pointer a hair and puts it back, saying whether the machine moved it.</summary>
    private static bool Nudge(IViiperLink link, ScreenPoint at, short by)
    {
        link.SendMouse(0, by, 0, 0, 0);
        Thread.Sleep(SettleMs);

        var moved = link.Cursor.X - at.X;
        if (moved == 0)
        {
            return false;
        }

        link.SendMouse(0, (short)-moved, 0, 0, 0);
        Thread.Sleep(SettleMs);
        return true;
    }
}
