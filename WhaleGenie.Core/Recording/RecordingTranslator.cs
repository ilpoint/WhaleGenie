using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Recording;

/// <summary>
/// Turns the stream of events a recording captured into the handful of actions a macro is
/// built from. The rules live here rather than inside the hook so they can be reasoned about
/// — and tested — without a keyboard, a mouse or a desktop.
/// </summary>
/// <remarks>
/// A raw recording is far too fine-grained to be useful: the pointer reports hundreds of
/// positions a second, a click arrives as a press and a release, and a chord as four key
/// events. The translator thins the pointer down to a readable path, folds press/release
/// pairs back into clicks and taps, folds held modifiers back into chords, and leaves the
/// pauses between what is left as delay steps.
/// </remarks>
public sealed class RecordingTranslator
{
    /// <summary>Largest number of wheel notches written into one scroll step.</summary>
    private const int MaxScrollNotches = 60;

    private readonly RecordingOptions _options;
    private readonly List<Placed> _placed = [];
    private readonly Dictionary<string, Held> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _downKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Placed> _dragPath = [];

    private PendingClick? _pendingClick;
    private bool _hasOrigin;
    private int _lastEmittedX;
    private int _lastEmittedY;
    private long _lastMoveAt;
    private bool _hasPending;
    private int _pendingX;
    private int _pendingY;
    private long _pendingAt;
    private bool _dragging;
    private long _lastDragAt;
    private long _scrollAt = long.MinValue;

    public RecordingTranslator(RecordingOptions? options = null)
        => _options = options ?? new RecordingOptions();

    /// <summary>Translates a whole recording in one go.</summary>
    public static IReadOnlyList<RecordedAction> Translate(
        IEnumerable<RecordedInput> inputs, RecordingOptions? options = null)
    {
        var translator = new RecordingTranslator(options);
        translator.Feed(inputs);
        return translator.Result();
    }

    /// <summary>Feeds in captured events, in the order the hook reported them.</summary>
    public void Feed(IEnumerable<RecordedInput> inputs)
    {
        foreach (var input in inputs)
        {
            Accept(input);
        }
    }

    /// <summary>Translates everything fed in so far, ready to become macro steps.</summary>
    public IReadOnlyList<RecordedAction> Result()
    {
        if (_hasPending)
        {
            WriteMove(_pendingAt);
        }

        FlushPendingClick();
        return WithDelays(CollapseKeys());
    }

    /// <summary>Takes in a single captured event.</summary>
    public void Accept(RecordedInput input)
    {
        switch (input.Kind)
        {
            case RecordedInputKind.KeyDown:
                if (_options.KeyboardKeys && _downKeys.Add(input.Key))
                {
                    Place(new RecordedAction(RecordedActionKind.KeyDown, Key: input.Key), input.At);
                }

                break;

            case RecordedInputKind.KeyUp:
                // A release for a key that was already down when recording started has no press
                // to match it, and replaying it alone would confuse the target.
                if (_options.KeyboardKeys && _downKeys.Remove(input.Key))
                {
                    Place(new RecordedAction(RecordedActionKind.KeyUp, Key: input.Key), input.At);
                }

                break;

            case RecordedInputKind.MouseMove:
                Move(input.X, input.Y, input.At);
                break;

            case RecordedInputKind.MouseDown:
                if (_options.MouseButtons)
                {
                    Press(input.Button, input.X, input.Y, input.At);
                }

                break;

            case RecordedInputKind.MouseUp:
                if (_options.MouseButtons)
                {
                    Release(input.Button, input.X, input.Y, input.At);
                }

                break;

            case RecordedInputKind.MouseScroll:
                // Scrolling is a mouse action, so the mouse buttons switch turns it off too.
                if (_options.MouseButtons)
                {
                    Scroll(input.Direction, input.Amount, input.X, input.Y, input.At);
                }

                break;
        }
    }

    private void Move(int x, int y, long at)
    {
        if (!_options.MouseMovement)
        {
            return;
        }

        // The first position seen is where the recording started; nothing has to be replayed
        // yet, and in relative mode it is the origin every later offset is measured from.
        if (!_hasOrigin)
        {
            _hasOrigin = true;
            _lastEmittedX = x;
            _lastEmittedY = y;
            _lastMoveAt = at;
            return;
        }

        if (_dragging)
        {
            if (at - _lastDragAt >= _options.DragMoveIntervalMs)
            {
                _dragPath.Add(new Placed(new RecordedAction(RecordedActionKind.MouseMove, X: x, Y: y), at));
                _lastDragAt = at;
            }

            return;
        }

        _pendingX = x;
        _pendingY = y;
        _pendingAt = at;
        _hasPending = true;

        if (at - _lastMoveAt >= _options.MouseMoveIntervalMs)
        {
            WriteMove(at);
        }
    }

    /// <summary>Writes the position that has been waiting, as movement or as an offset.</summary>
    private void WriteMove(long at)
    {
        if (!_hasPending)
        {
            return;
        }

        _hasPending = false;
        WriteMoveTo(_pendingX, _pendingY, _pendingAt);
        _lastMoveAt = at;
    }

    private void WriteMoveTo(int x, int y, long at)
    {
        if (x != _lastEmittedX || y != _lastEmittedY)
        {
            var action = _options.RelativeMovement
                ? new RecordedAction(RecordedActionKind.MouseMoveRelative, X: x - _lastEmittedX, Y: y - _lastEmittedY)
                : new RecordedAction(RecordedActionKind.MouseMove, X: x, Y: y);

            Place(action, at);
        }

        _lastEmittedX = x;
        _lastEmittedY = y;
    }

    private void Press(string button, int x, int y, long at)
    {
        // A position that was still waiting belongs before the press, unless the press lands on
        // it anyway, in which case the press step already carries the spot.
        if (_hasPending)
        {
            if (Near(_pendingX, _pendingY, x, y, _options.DragTolerance))
            {
                _hasPending = false;
            }
            else
            {
                WriteMove(at);
            }
        }

        _pressed[button] = new Held(x, y, at);
        _dragging = true;
        _dragPath.Clear();
        _lastDragAt = at;
        _lastEmittedX = x;
        _lastEmittedY = y;
    }

    private void Release(string button, int x, int y, long at)
    {
        if (!_pressed.Remove(button, out var held))
        {
            // The button was already down when recording started, so its release is not ours.
            return;
        }

        _dragging = _pressed.Count > 0;

        var hold = at - held.At;
        var moved = !Near(held.X, held.Y, x, y, _options.DragTolerance);
        if (!moved && hold <= _options.ClickMaxHoldMs)
        {
            // A quick press and release in one place is a click, not a step of its own.
            _hasPending = false;
            _dragPath.Clear();
            QueueClick(button, x, y, held.At, at);
            return;
        }

        WriteDrag(button, x, y, held, at);
    }

    /// <summary>Writes a press that was held, together with whatever path it took.</summary>
    private void WriteDrag(string button, int x, int y, Held held, long at)
    {
        Place(new RecordedAction(RecordedActionKind.MouseDown, Button: button, X: held.X, Y: held.Y), held.At);

        foreach (var (action, pointAt) in _dragPath)
        {
            WriteMoveTo(action.X, action.Y, pointAt);
        }

        _dragPath.Clear();
        Place(new RecordedAction(RecordedActionKind.MouseUp, Button: button, X: x, Y: y), at);
        _lastEmittedX = x;
        _lastEmittedY = y;
    }

    private void QueueClick(string button, int x, int y, long downAt, long upAt)
    {
        if (_pendingClick is { } waiting
            && string.Equals(waiting.Button, button, StringComparison.OrdinalIgnoreCase)
            && Near(waiting.X, waiting.Y, x, y, _options.DoubleClickMaxDistance)
            && downAt - waiting.UpAt <= _options.DoubleClickMaxGapMs)
        {
            _pendingClick = null;
            _placed.Add(new Placed(
                new RecordedAction(RecordedActionKind.MouseDoubleClick, Button: button, X: x, Y: y),
                waiting.DownAt));
            _lastEmittedX = x;
            _lastEmittedY = y;
            return;
        }

        FlushPendingClick();
        _pendingClick = new PendingClick(button, x, y, downAt, upAt);
    }

    /// <summary>
    /// A click is the only action that waits to see what comes next, because two of them in a
    /// row are a double click. Everything else pushes the waiting click out first.
    /// </summary>
    private void FlushPendingClick()
    {
        if (_pendingClick is not { } click)
        {
            return;
        }

        _pendingClick = null;
        _placed.Add(new Placed(
            new RecordedAction(RecordedActionKind.MouseClick, Button: click.Button, X: click.X, Y: click.Y, Amount: 1),
            click.DownAt));
        _lastEmittedX = click.X;
        _lastEmittedY = click.Y;
    }

    private void Scroll(string direction, int amount, int x, int y, long at)
    {
        if (_hasPending)
        {
            WriteMove(at);
        }

        _lastEmittedX = x;
        _lastEmittedY = y;

        if (amount <= 0)
        {
            return;
        }

        // Turning the wheel five times in a row is one gesture, not five steps, as long as it
        // keeps going the same way without a pause.
        if (_placed.Count > 0
            && _placed[^1].Action.Kind == RecordedActionKind.Scroll
            && string.Equals(_placed[^1].Action.Direction, direction, StringComparison.OrdinalIgnoreCase)
            && at - _scrollAt <= _options.ScrollCoalesceMs
            && _placed[^1].Action.Amount + amount <= MaxScrollNotches)
        {
            var previous = _placed[^1];
            _placed[^1] = previous with { Action = previous.Action with { Amount = previous.Action.Amount + amount } };
            _scrollAt = at;
            return;
        }

        Place(
            new RecordedAction(RecordedActionKind.Scroll, Direction: direction, X: x, Y: y, Amount: Math.Min(amount, MaxScrollNotches)),
            at);
        _scrollAt = at;
    }

    /// <summary>Adds an action to the recording, after any click still waiting to be resolved.</summary>
    private void Place(RecordedAction action, long at)
    {
        FlushPendingClick();
        _placed.Add(new Placed(action, at));
    }

    /// <summary>
    /// Folds the key events of a chord into one hotkey step: the modifiers go down, one key is
    /// pressed and let go, and the modifiers come back up, in whatever order the fingers lifted.
    /// </summary>
    private List<Placed> CollapseKeys()
    {
        var result = new List<Placed>(_placed.Count);
        var index = 0;

        while (index < _placed.Count)
        {
            if (TryChord(index, out var chord, out var consumed))
            {
                result.Add(chord);
                index += consumed;
                continue;
            }

            if (TryTap(index, out var tap))
            {
                result.Add(tap);
                index += 2;
                continue;
            }

            result.Add(_placed[index]);
            index++;
        }

        return result;
    }

    private bool TryChord(int index, out Placed chord, out int consumed)
    {
        chord = default;
        consumed = 0;

        var at = index;
        var modifiers = new List<string>();
        while (at < _placed.Count
               && _placed[at].Action.Kind == RecordedActionKind.KeyDown
               && KeyRoles.IsModifier(_placed[at].Action.Key))
        {
            modifiers.Add(_placed[at].Action.Key);
            at++;
        }

        if (modifiers.Count == 0 || at + 1 >= _placed.Count)
        {
            return false;
        }

        var mainDown = _placed[at];
        if (mainDown.Action.Kind != RecordedActionKind.KeyDown || KeyRoles.IsModifier(mainDown.Action.Key))
        {
            return false;
        }

        var mainUp = _placed[at + 1];
        if (mainUp.Action.Kind != RecordedActionKind.KeyUp
            || !string.Equals(mainUp.Action.Key, mainDown.Action.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var releaseStart = at + 2;
        if (releaseStart + modifiers.Count > _placed.Count)
        {
            return false;
        }

        var released = new List<string>(modifiers.Count);
        for (var offset = 0; offset < modifiers.Count; offset++)
        {
            var candidate = _placed[releaseStart + offset];
            if (candidate.Action.Kind != RecordedActionKind.KeyUp)
            {
                return false;
            }

            released.Add(candidate.Action.Key);
        }

        if (!SameKeys(modifiers, released))
        {
            return false;
        }

        chord = new Placed(
            new RecordedAction(
                RecordedActionKind.Hotkey,
                Key: string.Join("+", modifiers) + "+" + mainDown.Action.Key,
                DurationMs: (int)Math.Max(0, mainUp.At - mainDown.At)),
            _placed[index].At);
        consumed = releaseStart + modifiers.Count - index;
        return true;
    }

    private bool TryTap(int index, out Placed tap)
    {
        tap = default;
        if (index + 1 >= _placed.Count)
        {
            return false;
        }

        var down = _placed[index];
        var up = _placed[index + 1];
        if (down.Action.Kind != RecordedActionKind.KeyDown
            || up.Action.Kind != RecordedActionKind.KeyUp
            || !string.Equals(down.Action.Key, up.Action.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hold = up.At - down.At;
        if (hold > _options.KeyTapMaxHoldMs)
        {
            return false;
        }

        tap = new Placed(
            new RecordedAction(RecordedActionKind.KeyPress, Key: down.Action.Key, DurationMs: (int)Math.Max(0, hold)),
            down.At);
        return true;
    }

    /// <summary>Writes the pauses that are long enough to have been meant, as delay steps.</summary>
    private IReadOnlyList<RecordedAction> WithDelays(List<Placed> placed)
    {
        var result = new List<RecordedAction>(placed.Count);
        long? busyUntil = null;

        foreach (var (action, at) in placed)
        {
            if (_options.Delays && busyUntil is { } previous)
            {
                var gap = at - previous;
                if (gap > 0 && gap >= _options.IgnoreDelaysBelowMs)
                {
                    result.Add(new RecordedAction(RecordedActionKind.Delay, DurationMs: (int)Math.Min(gap, int.MaxValue)));
                }
            }

            result.Add(action);

            // A step that holds something down keeps the timeline busy while it does, so the
            // pause before the next step is measured from the end of the hold.
            busyUntil = action.Kind is RecordedActionKind.KeyPress or RecordedActionKind.Hotkey
                ? at + action.DurationMs
                : at;
        }

        return result;
    }

    private static bool SameKeys(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        var left = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
        foreach (var key in second)
        {
            if (!left.Remove(key))
            {
                return false;
            }
        }

        return left.Count == 0;
    }

    private static bool Near(int x, int y, int otherX, int otherY, int tolerance)
        => Math.Abs(x - otherX) <= tolerance && Math.Abs(y - otherY) <= tolerance;

    /// <summary>An action together with the moment it happened.</summary>
    private readonly record struct Placed(RecordedAction Action, long At);

    /// <summary>A button that is down, and where it went down.</summary>
    private readonly record struct Held(int X, int Y, long At);

    /// <summary>A click waiting to find out whether it is half of a double click.</summary>
    private readonly record struct PendingClick(string Button, int X, int Y, long DownAt, long UpAt);
}
