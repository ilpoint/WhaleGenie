using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;
using Viiper.Client.Devices.Keyboard;
using Viiper.Client.Devices.Mouse;

namespace WhaleGenie.Core.Devices.Platform;

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
/// macros sending at once would type on top of each other. The machine also answers only the pair
/// that got on the bus first — a second pair stays attached and does nothing — so the program keeps
/// one pair and shares it out (<see cref="SharedDriverInput"/>).
/// </remarks>
public sealed class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
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

    /// <summary>How many times a move is tried before the machine is given up on.</summary>
    private const int SeekTries = 12;

    /// <summary>How close to the point asked for counts as being there, in pixels.</summary>
    private const int Arrived = 1;

    /// <summary>
    /// How far the pointer is moved to find out whether the machine is listening to the devices at
    /// all. A pixel is not enough: the machine rounds movement this small away, and the check would
    /// answer "no" on a device that is working perfectly well.
    /// </summary>
    private const int Nudge = 10;

    private readonly Func<IViiperLink> _connect;

    private readonly object _gate = new();

    /// <summary>The keys held down right now, in the order they were pressed.</summary>
    private readonly List<byte> _keys = [];

    private IViiperLink? _link;
    private byte _modifiers;
    private byte _buttons;
    private long _movedAt;

    /// <summary>Whether a controller has been put on the machine yet.</summary>
    private bool _gamepadOn;

    /// <summary>What the controller is doing, kept here because a report is the whole picture of it.</summary>
    private GamepadState _gamepad = GamepadState.Neutral;

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
                WhaleGenieInputGate.Note(character.ToString());

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
            // Both ends of the path are made sure of: the first is where the pointer is meant to
            // already be, and the last is where the macro wants it. Everything between is only the
            // way it gets there.
            _ = Seek(path[0]);

            if (path.Count > 1 && durationMs > 0)
            {
                Walk(path, Math.Max(1, durationMs / (path.Count - 1)));
            }

            _ = Seek(path[^1]);
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

                WhaleGenieInputGate.Note(name);
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
                WhaleGenieInputGate.Note(KeyNames.WheelName(
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
            WhaleGenieInputGate.Note(name);
            _buttons |= bit;
            Send(0, 0);

            try
            {
                Walk(path, path.Count > 1 && durationMs > 0
                    ? Math.Max(1, durationMs / (path.Count - 1))
                    : 0);

                // The end of a drag is where the button is let go, so it is worth landing on.
                _ = Seek(path[^1]);
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

    /// <summary>
    /// Puts the controller on the machine, and lets go of everything it was holding: a pad that
    /// arrives with a button already down is one no game ever pressed.
    /// </summary>
    public void Connect()
    {
        lock (_gate)
        {
            if (_gamepadOn)
            {
                return;
            }

            _gamepad = GamepadState.Neutral;
            Link().ConnectGamepad();
            _gamepadOn = true;
            PushGamepad();
        }
    }

    /// <summary>Holds a controller button down, or lets it up.</summary>
    public void Button(string button, bool down)
    {
        lock (_gate)
        {
            var flag = GamepadNames.Button(button)
                ?? throw new DeviceActionException("Run.UnknownGamepadButton", button);

            BeginGamepad();
            _gamepad = _gamepad with
            {
                Buttons = down ? _gamepad.Buttons | flag : _gamepad.Buttons & ~flag,
            };

            // A trigger named as a button is the whole pull, or none of it: the pads that report
            // triggers do it by how far they are pulled rather than by a button of their own.
            if (flag == GamepadButtons.LeftTrigger)
            {
                _gamepad = _gamepad with { LeftTrigger = down ? 100 : 0 };
            }

            if (flag == GamepadButtons.RightTrigger)
            {
                _gamepad = _gamepad with { RightTrigger = down ? 100 : 0 };
            }

            PushGamepad();
        }
    }

    /// <summary>Moves one stick to a whole percent from its centre.</summary>
    public void Stick(string stick, int x, int y)
    {
        lock (_gate)
        {
            BeginGamepad();
            var (sideways, upwards) = (Least(x), Least(y));
            var which = (stick ?? string.Empty).Trim().ToLowerInvariant();

            _gamepad = which switch
            {
                "left" => _gamepad with { LeftX = sideways, LeftY = upwards },
                "right" => _gamepad with { RightX = sideways, RightY = upwards },
                _ => throw new DeviceActionException("Run.UnknownGamepadStick", which),
            };

            PushGamepad();
        }
    }

    /// <summary>Pulls one trigger by a whole percent.</summary>
    public void Trigger(string trigger, int amount)
    {
        lock (_gate)
        {
            BeginGamepad();
            var pulled = Math.Clamp(amount, 0, 100);
            var which = (trigger ?? string.Empty).Trim().ToLowerInvariant();

            _gamepad = which switch
            {
                "left" => _gamepad with { LeftTrigger = pulled },
                "right" => _gamepad with { RightTrigger = pulled },
                _ => throw new DeviceActionException("Run.UnknownGamepadTrigger", which),
            };

            PushGamepad();
        }
    }

    /// <summary>Lets go of everything, which is what the end of a run leaves behind it.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            if (!_gamepadOn)
            {
                return;
            }

            _gamepad = GamepadState.Neutral;
            PushGamepad();
        }
    }

    /// <summary>
    /// Brings the controller up if it is not there yet, so a macro that drives one does not have to
    /// say so first. A macro that does say so puts it on the bus before the game looks.
    /// </summary>
    private void BeginGamepad()
    {
        if (!_gamepadOn)
        {
            Connect();
        }
    }

    private void PushGamepad()
    {
        if (_gamepadOn)
        {
            Link().SendGamepad(_gamepad);
        }
    }

    /// <summary>Keeps a stick axis inside the range a controller understands.</summary>
    private static int Least(int percent) => Math.Clamp(percent, -100, 100);

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

            WhaleGenieInputGate.Note(name);
            _buttons = down ? (byte)(_buttons | bit) : (byte)(_buttons & ~bit);
            Send(0, 0);
        }
    }

    /// <summary>Walks the pointer to one point, or sends the whole distance at once.</summary>
    private void Glide(ScreenPoint to, int durationMs)
    {
        if (durationMs > 0)
        {
            var from = Pointer();
            var stops = MousePath.StepsFor(MouseRoute.Direct, durationMs);
            Walk(MousePath.Plan(MouseRoute.Direct, from, to, stops), durationMs / stops);
        }

        _ = Seek(to);
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

    /// <summary>Tells the parts of the app that watch the keyboard that this press is WhaleGenie's own.</summary>
    private static void Note(KeyCode code) => WhaleGenieInputGate.Note(KeyNames.Name(code, sided: true));

    /// <summary>The virtual mouse's bit for a button, and the name WhaleGenie calls that button by.</summary>
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
    /// The connection to the server, opened and woken the first time a step needs it. Opening it is
    /// what fails when the driver or the server is missing, so it happens here rather than when the
    /// device is made — and the run reports it on the step that asked for driver-level input.
    /// </summary>
    private IViiperLink Link()
    {
        if (_link is not null)
        {
            return _link;
        }

        var link = _connect();
        _link = link;

        try
        {
            Wake();
            return link;
        }
        catch
        {
            _link = null;
            link.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Waits for the virtual keyboard and mouse to be devices the machine is really listening to,
    /// and gives up when they never are.
    ///
    /// A device that has just been put on the machine is not one the machine polls yet: everything
    /// sent in the first quarter of a second goes nowhere, so a macro's first move would silently
    /// not happen. Nothing can be read back from a keyboard, but the pointer can be asked to move
    /// and put back, which is what this does — both ways round, in case one of them is off the edge
    /// of the screen, and wait as long as a device takes to be noticed. A device the machine never
    /// notices never moves at all, which is what a VIIPER server started without its auto-attach
    /// flag leaves behind.
    /// </summary>
    private void Wake()
    {
        var at = Pointer();
        foreach (var (dx, dy) in new[] { (Nudge, 0), (-Nudge, 0), (0, Nudge), (0, -Nudge) })
        {
            if (Seek(new ScreenPoint(at.X + dx, at.Y + dy)) && Seek(at))
            {
                return;
            }
        }

        throw new DeviceActionException("Run.NoDriverAttached");
    }

    /// <summary>
    /// Puts the pointer on a point, saying whether it got there.
    ///
    /// A report can go nowhere while the machine is still waking up to a device that has just been
    /// plugged in, and a pointer whose speed the machine boosts moves further than it was told, so
    /// what is left to cover is measured and sent again rather than assumed. Each try after one
    /// that went too far sends less of what remains, which settles rather than swinging wider.
    ///
    /// A point the machine will not allow — past the edge of the screen, say — is not a failure of
    /// the step: the pointer is left as close as the machine lets it be and the macro carries on,
    /// which is what the same move does when it is sent through the front device.
    /// </summary>
    private bool Seek(ScreenPoint to)
    {
        var share = 1.0;
        var at = Pointer();
        for (var attempt = 0; attempt < SeekTries; attempt++)
        {
            var dx = to.X - at.X;
            var dy = to.Y - at.Y;
            if (Math.Abs(dx) <= Arrived && Math.Abs(dy) <= Arrived)
            {
                return true;
            }

            Push(Step(dx, share), Step(dy, share));
            Sleep(SettleMs);

            var now = Pointer();
            if (Math.Abs(to.X - now.X) > Math.Abs(dx) || Math.Abs(to.Y - now.Y) > Math.Abs(dy))
            {
                share /= 2;
            }

            at = now;
        }

        return false;
    }

    /// <summary>How much of the distance left to cover to send, and never nothing at all.</summary>
    private static int Step(int left, double share)
    {
        var step = (int)Math.Round(left * share, MidpointRounding.AwayFromZero);
        return step != 0 ? step : Math.Sign(left);
    }
}
