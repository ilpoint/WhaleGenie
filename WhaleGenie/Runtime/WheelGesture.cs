using System;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

public enum WheelGestureAction
{
    None,
    OpenWheel,
    Select,
    Cancel,
    Repeat,
    Move,
}

/// <summary>What one hook event asks the wheel to do.</summary>
public readonly record struct WheelGestureEvent(
    WheelGestureAction Action,
    double X = 0,
    double Y = 0,
    WheelSlotRef? Slot = null);

/// <summary>
/// The Quicker gesture: hold the trigger button, move the pointer, release over a direction.
/// A press with no movement is deliberately left completely alone — the trigger service never
/// yields the button and no input is swallowed, so a short right click still opens the ordinary
/// system context menu. Only the release that ends an open wheel is swallowed, and only because
/// Windows would otherwise put that context menu on top of the wheel.
/// </summary>
public sealed class WheelGesture
{
    /// <summary>Enough movement to tell an intentional swipe from a hand resting on the mouse.</summary>
    public const double MoveThreshold = 12;

    private readonly Func<string> _triggerKey;
    private readonly Func<string> _repeatKey;
    private readonly Func<bool> _enabled;

    private enum State { Idle, Pending, Open }

    private State _state;
    private string _held = string.Empty;
    private double _startX;
    private double _startY;
    private double _centerX;
    private double _centerY;
    private WheelMetrics? _metrics;
    private WheelSlotRef? _hover;

    public WheelGesture(Func<string> triggerKey, Func<string> repeatKey, Func<bool> enabled)
    {
        _triggerKey = triggerKey;
        _repeatKey = repeatKey;
        _enabled = enabled;
    }

    public bool IsOpen => _state == State.Open;

    public bool IsPending => _state == State.Pending;

    /// <summary>
    /// A press only starts waiting. It returns None and never asks for the event to be suppressed:
    /// from the operating system's point of view this is still an ordinary button press.
    /// </summary>
    public WheelGestureEvent Down(string binding, double x, double y)
    {
        if (_state == State.Pending)
        {
            Reset();
        }

        if (_state != State.Idle || !_enabled() || !Same(binding, _triggerKey()))
        {
            return new WheelGestureEvent(WheelGestureAction.None);
        }

        _state = State.Pending;
        _held = binding;
        _startX = x;
        _startY = y;
        _hover = null;
        return new WheelGestureEvent(WheelGestureAction.None);
    }

    public WheelGestureEvent Move(double x, double y)
    {
        if (_state == State.Pending
            && Math.Sqrt(Square(x - _startX) + Square(y - _startY)) >= MoveThreshold)
        {
            _state = State.Open;
            // Centred on the press, so the direction the hand travelled is the direction selected.
            return new WheelGestureEvent(WheelGestureAction.OpenWheel, _startX, _startY);
        }

        return _state == State.Open
            ? new WheelGestureEvent(WheelGestureAction.Move, x, y)
            : new WheelGestureEvent(WheelGestureAction.None);
    }

    public WheelGestureEvent Up(string binding, double x, double y)
    {
        if (_state == State.Idle || !Same(binding, _held))
        {
            return new WheelGestureEvent(WheelGestureAction.None);
        }

        var wasOpen = _state == State.Open;
        var metrics = _metrics;
        var centreX = _centerX;
        var centreY = _centerY;
        var hover = _hover;
        Reset();

        // A short press is nothing at all: the button was never suppressed, so the system handles it.
        if (!wasOpen)
        {
            return new WheelGestureEvent(WheelGestureAction.None);
        }

        if (metrics is null)
        {
            return new WheelGestureEvent(WheelGestureAction.Cancel, x, y);
        }

        var aimed = WheelGeometry.At(x - centreX, y - centreY, metrics);
        // A parent's children are fanned out past the wheel; releasing while the pointer is still
        // in that direction picks the child that was being aimed at, not the parent underneath it.
        var chosen = hover is { IsSub: true } sub && aimed is not null
                     && sub.Parent == aimed.Value.Parent
            ? sub
            : aimed;
        return chosen is { } picked
            ? new WheelGestureEvent(WheelGestureAction.Select, x, y, picked)
            : new WheelGestureEvent(WheelGestureAction.Cancel, x, y);
    }

    public WheelGestureEvent Key(string name)
    {
        if (_state != State.Open)
        {
            return new WheelGestureEvent(WheelGestureAction.None);
        }

        if (name is "Esc" or "Escape")
        {
            Reset();
            return new WheelGestureEvent(WheelGestureAction.Cancel);
        }

        return Same(name, _repeatKey())
            ? new WheelGestureEvent(WheelGestureAction.Repeat, Slot: _hover)
            : new WheelGestureEvent(WheelGestureAction.None);
    }

    public WheelGestureEvent Timeout()
    {
        if (_state != State.Open)
        {
            Reset();
            return new WheelGestureEvent(WheelGestureAction.None);
        }

        Reset();
        return new WheelGestureEvent(WheelGestureAction.Cancel);
    }

    public void Hover(WheelSlotRef? reference) => _hover = reference;

    public void SetCenter(double x, double y, WheelMetrics metrics)
    {
        _centerX = x;
        _centerY = y;
        _metrics = metrics;
    }

    private static bool Same(string first, string second)
        => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static double Square(double value) => value * value;

    private void Reset()
    {
        _state = State.Idle;
        _held = string.Empty;
        _hover = null;
        _metrics = null;
    }
}
