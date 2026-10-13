using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SharpHook.Data;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Watches the keyboard and the mouse the way the operating system sees them, so a macro can
/// be built by doing the work once instead of describing it step by step. What it listens to is
/// the hook's own events, so which library reads the keyboard is none of its business.
/// </summary>
/// <remarks>
/// The hook runs on a thread of its own and raises its events there; the timestamps count
/// milliseconds from the moment recording started. Input that WhaleGenie itself sends is skipped,
/// which keeps a recording from picking up a macro that happens to be running.
/// </remarks>
public sealed class InputHookRecorder : IInputRecorder
{
    /// <summary>One wheel notch, as the platform reports it.</summary>
    private const int Notch = 120;

    /// <summary>Most notches written into a single scroll step, however fast the wheel spun.</summary>
    private const int MaxScrollNotches = 60;

    private readonly Stopwatch _clock = new();
    private readonly HashSet<HookKey> _down = [];

    /// <summary>The shared hook while this recorder is listening to it.</summary>
    private IInputHook? _hook;
    private int _cursorX;
    private int _cursorY;
    private bool _hasCursor;
    private int _lastMoveX = int.MinValue;
    private int _lastMoveY = int.MinValue;
    private int _stopRaised;

    /// <summary>The one shortcut that ends a recording, and the two keys it is held with.</summary>
    private static readonly HookKey StopKey = SharpKeys.Key(KeyCode.VcF10);
    private static readonly HookKey LeftControl = SharpKeys.Key(KeyCode.VcLeftControl);
    private static readonly HookKey RightControl = SharpKeys.Key(KeyCode.VcRightControl);

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
        var hook = GlobalInputHook.Shared;
        hook.KeyDown += OnKeyDown;
        hook.KeyUp += OnKeyUp;
        hook.MouseDown += OnMouseDown;
        hook.MouseUp += OnMouseUp;
        hook.Moved += OnMoved;
        hook.MovedRelative += OnMovedRelative;
        hook.Wheel += OnWheel;

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

    private void OnKeyDown(KeyHookEvent e)
    {
        if (!IsRunning || Skipped(e.Injected))
        {
            return;
        }

        // The one shortcut that ends a recording is never part of the recording. Escape is
        // deliberately not one of them: it is a key the work being recorded often uses.
        if (e.Key == StopKey && (Held(LeftControl) || Held(RightControl)))
        {
            RequestStop();
            return;
        }

        // A key with no name is one no macro could ask for, and there is nothing to write down.
        var name = KeyNames.Name(e.Key);
        if (name.Length == 0)
        {
            return;
        }

        _down.Add(e.Key);
        Raise(RecordedInput.KeyDown(name, Now));
    }

    private void OnKeyUp(KeyHookEvent e)
    {
        if (!IsRunning || Skipped(e.Injected))
        {
            return;
        }

        // A key that was already down when recording started has no press of ours to match,
        // and replaying its release on its own would confuse whatever is being driven.
        if (!_down.Remove(e.Key))
        {
            return;
        }

        var name = KeyNames.Name(e.Key);
        if (name.Length != 0)
        {
            Raise(RecordedInput.KeyUp(name, Now));
        }
    }

    private void OnMouseDown(MouseHookEvent e)
    {
        if (!IsRunning || Skipped(e.Injected))
        {
            return;
        }

        MoveCursorTo(e.X, e.Y);
        Raise(RecordedInput.MouseDown(Button(e.Button), e.X, e.Y, Now));
    }

    private void OnMouseUp(MouseHookEvent e)
    {
        if (!IsRunning || Skipped(e.Injected))
        {
            return;
        }

        MoveCursorTo(e.X, e.Y);
        Raise(RecordedInput.MouseUp(Button(e.Button), e.X, e.Y, Now));
    }

    private void OnMoved(MouseMovedHookEvent e)
    {
        if (!IsRunning || Skipped(e.Injected))
        {
            return;
        }

        Report(e.X, e.Y);
    }

    private void OnMovedRelative(MouseMovedRelativeHookEvent e)
    {
        if (!IsRunning || Skipped(e.Injected))
        {
            return;
        }

        Report(_cursorX + e.DeltaX, _cursorY + e.DeltaY);
    }

    private void OnWheel(WheelHookEvent e)
    {
        if (!IsRunning || Skipped(e.Injected))
        {
            return;
        }

        MoveCursorTo(e.X, e.Y);

        var (direction, amount) = Wheel(e.Axis, e.Rotation, e.Delta);
        if (amount > 0)
        {
            Raise(RecordedInput.Scroll(direction, amount, e.X, e.Y, Now));
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

    private bool Held(HookKey key) => _down.Contains(key);

    /// <summary>True when an event is input another program sent and the user asked to skip it.</summary>
    private bool Skipped(bool injected) => IgnoreInjected && injected;

    /// <summary>Turns a wheel turn into the direction and the number of notches a macro writes.</summary>
    private static (string Direction, int Amount) Wheel(HookWheelAxis axis, int rotation, int delta)
    {
        // The signs follow the input device, so what is recorded plays back the way it happened.
        var direction = axis == HookWheelAxis.Horizontal
            ? rotation < 0 ? "right" : "left"
            : rotation < 0 ? "down" : "up";

        var notches = Math.Abs(rotation) >= Notch
            ? Math.Abs(rotation) / Notch
            : Math.Max(1, Math.Abs(delta));

        return (direction, Math.Clamp(notches, 1, MaxScrollNotches));
    }

    /// <summary>The word a macro writes for a button.</summary>
    private static string Button(HookMouseButton button) => button switch
    {
        HookMouseButton.Right => "right",
        HookMouseButton.Middle => "middle",
        HookMouseButton.Back => "back",
        HookMouseButton.Forward => "forward",
        _ => "left",
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

    private void Detach(IInputHook hook)
    {
        hook.KeyDown -= OnKeyDown;
        hook.KeyUp -= OnKeyUp;
        hook.MouseDown -= OnMouseDown;
        hook.MouseUp -= OnMouseUp;
        hook.Moved -= OnMoved;
        hook.MovedRelative -= OnMovedRelative;
        hook.Wheel -= OnWheel;
    }
}
