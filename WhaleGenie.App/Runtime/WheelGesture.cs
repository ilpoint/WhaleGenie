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

    /// <summary>
    /// The press was taken and the wheel never opened: it was an ordinary click after all, and the
    /// caller has to send it again so the application sees the one it never received.
    /// </summary>
    Replay,
}

/// <summary>What one hook event asks the wheel to do.</summary>
public readonly record struct WheelGestureEvent(
    WheelGestureAction Action,
    double X = 0,
    double Y = 0,
    WheelSlotRef? Slot = null,
    bool Swallow = false);

/// <summary>
/// The Quicker gesture: hold the trigger button, move the pointer, release over a direction.
/// </summary>
/// <remarks>
/// The button belongs to the wheel from the moment it goes down until it comes back up, and both
/// events are kept from the rest of the system. That is not a detail: a right button that reaches
/// the window under the pointer makes it draw a selection of its own while the person is aiming,
/// and releases into its context menu instead of the sector that was chosen. Nothing is lost by
/// taking it, because a press that never opened the wheel is answered with <see cref="WheelGestureAction.Replay"/>,
/// which sends the same click once the button is up.
/// </remarks>
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

    /// <summary>
    /// The button this gesture took from the system and still owes an answer for. It outlives the
    /// wheel state on purpose: after Escape or a timeout the press is still held down, and its
    /// release must not reach the application either.
    /// </summary>
    private string _owned = string.Empty;

    public WheelGesture(Func<string> triggerKey, Func<string> repeatKey, Func<bool> enabled)
    {
        _triggerKey = triggerKey;
        _repeatKey = repeatKey;
        _enabled = enabled;
    }

    public bool IsOpen => _state == State.Open;

    public bool IsPending => _state == State.Pending;

    /// <summary>
    /// A press that names the trigger button is taken: the state waits for a movement to decide
    /// whether this is a swipe or a click. The press itself is swallowed, so nothing under the
    /// pointer acts on a button whose only job so far is to be held.
    /// </summary>
    public WheelGestureEvent Down(string binding, double x, double y)
    {
        // A second press of the same button is a new gesture (the release was lost); any other
        // button belongs to the application and must not cancel the one being held.
        if (_state == State.Pending && Same(binding, _held))
        {
            Reset();
        }

        if (_state != State.Idle || !Same(binding, _triggerKey()) || !_enabled())
        {
            return new WheelGestureEvent(WheelGestureAction.None);
        }

        _state = State.Pending;
        _held = binding;
        _owned = binding;
        _startX = x;
        _startY = y;
        _hover = null;
        return new WheelGestureEvent(WheelGestureAction.None, Swallow: true);
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
        // A release this gesture does not own belongs to the application: a middle click over an
        // open wheel, for instance, is still a middle click.
        if (_owned.Length == 0 || !Same(binding, _owned))
        {
            return new WheelGestureEvent(WheelGestureAction.None);
        }

        _owned = string.Empty;
        var wasOpen = _state == State.Open;
        var wasPending = _state == State.Pending;
        var metrics = _metrics;
        var centreX = _centerX;
        var centreY = _centerY;
        Reset();

        // The press never reached the application, so a release that opened nothing has to hand
        // it back as the click it turned out to be.
        if (wasPending)
        {
            return new WheelGestureEvent(WheelGestureAction.Replay, x, y, Swallow: true);
        }

        // Escape and the timeout end the wheel before the button comes up; the release still has
        // to be swallowed, and it has nothing left to answer.
        if (!wasOpen || metrics is null)
        {
            return new WheelGestureEvent(WheelGestureAction.None, Swallow: true);
        }

        var aimed = WheelGeometry.At(x - centreX, y - centreY, metrics);
        return aimed is { } picked
            ? new WheelGestureEvent(WheelGestureAction.Select, x, y, picked, Swallow: true)
            : new WheelGestureEvent(WheelGestureAction.Cancel, x, y, Swallow: true);
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
