using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;


namespace WhaleGenie.Platform.Windows;

// What the device does under the calls: holding, gliding and the key codes it sends.
public sealed partial class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
{
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
    private void Apply(HookCode code, bool down)
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

        throw new DeviceActionException(RunKeys.NoDriverKey, KeyTable.Name(code, sided: true));
    }

    /// <summary>The code a macro's key name stands for.</summary>
    private static HookCode Code(string key)
        => KeyTable.ResolveCode(key) ?? throw new DeviceActionException(RunKeys.UnknownKey, key);

    /// <summary>Tells the parts of the app that watch the keyboard that this press is WhaleGenie's own.</summary>
    private static void Note(HookCode code) => WhaleGenieInputGate.Note(KeyTable.Key(code));

    /// <summary>The virtual mouse's bit for a button, and the name WhaleGenie calls that button by.</summary>
    private static (byte Bit, string Name) Button(string button) => button.Trim().ToLowerInvariant() switch
    {
        "right" => (LibViiper.MouseRight, KeyTable.MouseName(HookMouseButton.Right)),
        "middle" => (LibViiper.MouseMiddle, KeyTable.MouseName(HookMouseButton.Middle)),
        "middle2" or "back" => (LibViiper.MouseBack, KeyTable.MouseName(HookMouseButton.Back)),
        "forward" => (LibViiper.MouseForward, KeyTable.MouseName(HookMouseButton.Forward)),
        _ => (LibViiper.MouseLeft, KeyTable.MouseName(HookMouseButton.Left)),
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
}
