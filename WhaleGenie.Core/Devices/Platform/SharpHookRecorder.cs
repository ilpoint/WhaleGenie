using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SharpHook;
using SharpHook.Data;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Watches the keyboard and the mouse the way the operating system sees them, so a macro can
/// be built by doing the work once instead of describing it step by step.
/// </summary>
/// <remarks>
/// The hook runs on a thread of its own and raises its events there; the timestamps count
/// milliseconds from the moment recording started. Input that WhaleGenie itself sends is skipped,
/// which keeps a recording from picking up a macro that happens to be running.
/// </remarks>
public sealed class SharpHookRecorder : IInputRecorder
{
    /// <summary>One wheel notch, as the platform reports it.</summary>
    private const int Notch = 120;

    /// <summary>Most notches written into a single scroll step, however fast the wheel spun.</summary>
    private const int MaxScrollNotches = 60;

    private readonly Stopwatch _clock = new();
    private readonly HashSet<KeyCode> _down = [];

    /// <summary>The shared hook while this recorder is listening to it.</summary>
    private IGlobalHook? _hook;
    private int _cursorX;
    private int _cursorY;
    private bool _hasCursor;
    private int _lastMoveX = int.MinValue;
    private int _lastMoveY = int.MinValue;
    private int _stopRaised;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// When set, input the system marks as injected — anything another program sent, including
    /// this application's own playback — is left out. Recording through a remote session or a
    /// virtual machine can turn it off, at the price of recording a running macro as well.
    /// </summary>
    public bool IgnoreInjected { get; set; } = true;

    public event Action<RecordedInput>? Captured;

    public event Action? StopRequested;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        // The hook lives for the life of the process; a recording only listens to it for a
        // while. Building one per recording stops working after the first couple of times.
        var hook = GlobalInputHook.Shared.Hook;
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
        hook.MousePressed += OnMousePressed;
        hook.MouseReleased += OnMouseReleased;
        hook.MouseMoved += OnMouseMoved;
        hook.MouseDragged += OnMouseMoved;
        hook.MouseMovedRelative += OnMouseMovedRelative;
        hook.MouseDraggedRelative += OnMouseMovedRelative;
        hook.MouseWheel += OnMouseWheel;

        _down.Clear();
        _hasCursor = false;
        _lastMoveX = int.MinValue;
        _lastMoveY = int.MinValue;
        _stopRaised = 0;
        _clock.Restart();
        _hook = hook;
        IsRunning = true;
    }

    public void Stop()
    {
        var hook = _hook;
        if (hook is null)
        {
            return;
        }

        IsRunning = false;
        _hook = null;
        _clock.Stop();
        Detach(hook);
    }

    public void Dispose() => Stop();

    /// <summary>Milliseconds since the recording started.</summary>
    private long Now => _clock.ElapsedMilliseconds;

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!IsRunning || Skipped(e))
        {
            return;
        }

        var code = e.Data.KeyCode;
        if (code == KeyCode.VcUndefined)
        {
            return;
        }

        // The one shortcut that ends a recording is never part of the recording. Escape is
        // deliberately not one of them: it is a key the work being recorded often uses.
        if (code == KeyCode.VcF10 && (Held(KeyCode.VcLeftControl) || Held(KeyCode.VcRightControl)))
        {
            RequestStop();
            return;
        }

        var name = KeyNames.Name(code);
        if (name.Length == 0)
        {
            return;
        }

        _down.Add(code);
        Raise(RecordedInput.KeyDown(name, Now));
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!IsRunning || Skipped(e))
        {
            return;
        }

        // A key that was already down when recording started has no press of ours to match,
        // and replaying its release on its own would confuse whatever is being driven.
        var code = e.Data.KeyCode;
        if (!_down.Remove(code))
        {
            return;
        }

        var name = KeyNames.Name(code);
        if (name.Length != 0)
        {
            Raise(RecordedInput.KeyUp(name, Now));
        }
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (!IsRunning || Skipped(e) || Button(e.Data.Button) is not { } button)
        {
            return;
        }

        MoveCursorTo(e.Data.X, e.Data.Y);
        Raise(RecordedInput.MouseDown(button, e.Data.X, e.Data.Y, Now));
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        if (!IsRunning || Skipped(e) || Button(e.Data.Button) is not { } button)
        {
            return;
        }

        MoveCursorTo(e.Data.X, e.Data.Y);
        Raise(RecordedInput.MouseUp(button, e.Data.X, e.Data.Y, Now));
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        if (!IsRunning || Skipped(e))
        {
            return;
        }

        Report(e.Data.X, e.Data.Y);
    }

    private void OnMouseMovedRelative(object? sender, MouseHookEventArgs e)
    {
        if (!IsRunning || Skipped(e))
        {
            return;
        }

        Report(_cursorX + e.Data.X, _cursorY + e.Data.Y);
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        if (!IsRunning || Skipped(e))
        {
            return;
        }

        MoveCursorTo(e.Data.X, e.Data.Y);

        var (direction, amount) = Wheel(e.Data.Direction, e.Data.Rotation, e.Data.Delta);
        if (amount > 0)
        {
            Raise(RecordedInput.Scroll(direction, amount, e.Data.X, e.Data.Y, Now));
        }
    }

    /// <summary>Reports a pointer position, ignoring a repeat of the one just reported.</summary>
    private void Report(int x, int y)
    {
        // A movement while a button is held arrives as a "drag" in addition to the plain move,
        // so the same position can be reported twice.
        if (_hasCursor && x == _lastMoveX && y == _lastMoveY)
        {
            _cursorX = x;
            _cursorY = y;
            return;
        }

        _hasCursor = true;
        _lastMoveX = x;
        _lastMoveY = y;
        _cursorX = x;
        _cursorY = y;
        Raise(RecordedInput.MouseMove(x, y, Now));

        // The position is part of the recorded motion, so the report above is the only one.
    }

    /// <summary>Follows the pointer without reporting a move, for events that carry the position.</summary>
    private void MoveCursorTo(int x, int y)
    {
        _hasCursor = true;
        _cursorX = x;
        _cursorY = y;
    }

    private bool Held(KeyCode code) => _down.Contains(code);

    /// <summary>True when an event is input another program sent and the user asked to skip it.</summary>
    private bool Skipped(HookEventArgs e) => IgnoreInjected && e.IsEventSimulated;

    /// <summary>Turns a wheel turn into the direction and the number of notches a macro writes.</summary>
    private static (string Direction, int Amount) Wheel(
        MouseWheelScrollDirection axis, short rotation, ushort delta)
    {
        // The signs follow the input device, so what is recorded plays back the way it happened.
        var direction = axis == MouseWheelScrollDirection.Horizontal
            ? rotation < 0 ? "right" : "left"
            : rotation < 0 ? "down" : "up";

        var notches = Math.Abs(rotation) >= Notch
            ? Math.Abs(rotation) / Notch
            : Math.Max(1, Math.Abs((int)delta));

        return (direction, Math.Clamp(notches, 1, MaxScrollNotches));
    }

    private static string? Button(MouseButton button) => button switch
    {
        MouseButton.Button1 => "left",
        MouseButton.Button2 => "right",
        MouseButton.Button3 => "middle",
        MouseButton.Button4 => "back",
        MouseButton.Button5 => "forward",
        _ => null,
    };

    private void Raise(RecordedInput input)
    {
        try
        {
            Captured?.Invoke(input);
        }
        catch (Exception)
        {
            // A listener's trouble must not take the hook thread down with it.
        }
    }

    private void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRaised, 1) != 0)
        {
            return;
        }

        try
        {
            StopRequested?.Invoke();
        }
        catch (Exception)
        {
            // Nothing useful can be done about a listener that throws while stopping.
        }
    }

    private void Detach(IGlobalHook hook)
    {
        hook.KeyPressed -= OnKeyPressed;
        hook.KeyReleased -= OnKeyReleased;
        hook.MousePressed -= OnMousePressed;
        hook.MouseReleased -= OnMouseReleased;
        hook.MouseMoved -= OnMouseMoved;
        hook.MouseDragged -= OnMouseMoved;
        hook.MouseMovedRelative -= OnMouseMovedRelative;
        hook.MouseDraggedRelative -= OnMouseMovedRelative;
        hook.MouseWheel -= OnMouseWheel;
    }
}
