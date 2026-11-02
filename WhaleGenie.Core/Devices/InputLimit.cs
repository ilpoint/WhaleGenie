using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// How fast this program may send keyboard and mouse input, in events per second. A macro that types
/// a thousand characters as fast as the machine takes them reads, to any program on the other end, as
/// a machine rather than as a person — some games drop the input, some windows miss clicks, and the
/// result looks like a bug in the macro. The limit is a ceiling, not a pace to aim at: a step that
/// sends one key every half second is not slowed down by it at all.
/// </summary>
/// <remarks>
/// What counts as one event, which is the same list the manual gives: a key press, a key going down
/// or coming up, one click, one wheel notch, one drag, and one character of typed text. A chord
/// counts once — the keys go down together, which is what it looked like from the outside — and
/// moving the pointer counts not at all: a path of five hundred points is still one movement, and
/// counting it would make every move action the slow one.
/// </remarks>
public static class InputLimit
{
    /// <summary>The most the setting may be: fast enough to be no limit in practice.</summary>
    public const int Most = 999;

    /// <summary>What the program allows when nothing has been chosen.</summary>
    public const int Usual = 120;

    /// <summary>
    /// Held while the moment of the last event is worked out and moved forward, so two threads
    /// sending input take turns instead of both deciding they are under the limit.
    /// </summary>
    private static readonly object Gate = new();

    private static int _perSecond = Usual;

    /// <summary>When the last event was allowed to go, or null before anything has been sent.</summary>
    private static long? _last;

    /// <summary>
    /// Events per second, from 0 to <see cref="Most"/>. Zero turns the limit off, which is what
    /// somebody who has deliberately tuned a macro against a game wants back.
    /// </summary>
    public static int PerSecond
    {
        get => Volatile.Read(ref _perSecond);
        set => Volatile.Write(ref _perSecond, Math.Clamp(value, 0, Most));
    }

    /// <summary>Reads the moment, for the checks: a limit about time cannot be waited out in a test.</summary>
    internal static Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>Waits, for the checks, which record the wait instead of sleeping through it.</summary>
    internal static Action<TimeSpan> Wait { get; set; } = time =>
    {
        if (time > TimeSpan.Zero)
        {
            Thread.Sleep(time);
        }
    };

    /// <summary>Forgets what has been sent, which is what a check needs before it measures.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _last = null;
        }
    }

    /// <summary>
    /// Waits as long as <paramref name="events"/> events would have to be spaced out by, and returns
    /// the moment they were allowed to go. Called before the device sends them: the wait belongs to
    /// the events about to leave, not to the ones already gone.
    /// </summary>
    public static void Spend(int events)
    {
        var rate = PerSecond;
        if (rate <= 0 || events <= 0)
        {
            return;
        }

        // One second's worth of the clock divided by the number of events in it: how far apart two
        // events have to be. Kept in ticks rather than seconds so every event uses the same reading.
        var spacing = (double)Stopwatch.Frequency / rate;

        lock (Gate)
        {
            var now = Clock();

            // Nothing has been sent yet, so nothing can be too close to it — but the batch still
            // takes its own time, and the next event has to be spaced from the last one in it.
            if (_last is not { } last)
            {
                _last = now + (long)Math.Round(spacing * (events - 1));
                return;
            }

            var due = last + (long)Math.Round(spacing * events);
            if (due > now)
            {
                Wait(TimeSpan.FromSeconds((due - now) / (double)Stopwatch.Frequency));
                now = due;
            }

            _last = now;
        }
    }
}

/// <summary>
/// One input device seen through the limit: every event is counted and the sender waits its turn
/// before the device is asked to send it. It is a wrapper rather than a change inside each of the
/// three ways input leaves the machine, so the front device, the one that posts at a window and the
/// driver all answer to the same limit instead of three limits that could drift apart.
/// </summary>
public sealed class ThrottledInputDevice(IInputDevice inner) : IInputDevice
{
    /// <summary>
    /// The view of one device, which is the same view every time it is asked for. Input that went
    /// through a device is the same input whatever route it took, so two steps sending through the
    /// virtual keyboard and mouse are using one device and must be told so — a fresh wrapper per
    /// question would make them look like two.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IInputDevice,
        ThrottledInputDevice> Views = new();

    /// <summary>The counted view of a device, wrapping it once however often it is asked for.</summary>
    public static IInputDevice Of(IInputDevice device)
        => device is ThrottledInputDevice ? device : Views.GetValue(device, one => new ThrottledInputDevice(one));

    public InputCoordinateSpace CoordinateSpace => inner.CoordinateSpace;

    public ScreenPoint Cursor => inner.Cursor;

    public void KeyPress(string key, int holdMs)
    {
        InputLimit.Spend(1);
        inner.KeyPress(key, holdMs);
    }

    public void KeyDown(string key)
    {
        InputLimit.Spend(1);
        inner.KeyDown(key);
    }

    public void KeyUp(string key)
    {
        InputLimit.Spend(1);
        inner.KeyUp(key);
    }

    /// <summary>A chord is one event: from outside, the keys went down together.</summary>
    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
    {
        InputLimit.Spend(1);
        inner.Hotkey(keys, holdMs);
    }

    /// <summary>
    /// Every character is a keystroke of its own, so a long text is paced rather than sent at once.
    /// The device already spaces them by the interval it is given; this is the ceiling above it.
    /// </summary>
    public void TypeText(string text, int intervalMs)
    {
        InputLimit.Spend(text?.Length ?? 0);
        inner.TypeText(text ?? string.Empty, intervalMs);
    }

    /// <summary>Moving the pointer is not counted: a path of any length is still one movement.</summary>
    public void MoveMouse(int x, int y, int durationMs) => inner.MoveMouse(x, y, durationMs);

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
        => inner.MoveMouseAlong(path, durationMs);

    public void MoveMouseRelative(int dx, int dy, int durationMs)
        => inner.MoveMouseRelative(dx, dy, durationMs);

    public void MouseDown(string button, int x, int y)
    {
        InputLimit.Spend(1);
        inner.MouseDown(button, x, y);
    }

    public void MouseUp(string button, int x, int y)
    {
        InputLimit.Spend(1);
        inner.MouseUp(button, x, y);
    }

    /// <summary>A double click is two clicks, and counts as two.</summary>
    public void Click(string button, int x, int y, int clicks, int intervalMs)
    {
        InputLimit.Spend(Math.Max(1, clicks));
        inner.Click(button, x, y, clicks, intervalMs);
    }

    /// <summary>A wheel event is a notch; a step asking for three notches is three of them.</summary>
    public void Scroll(string direction, int delta, int x, int y)
    {
        InputLimit.Spend(Math.Max(1, (Math.Abs(delta) + 119) / 120));
        inner.Scroll(direction, delta, x, y);
    }

    /// <summary>A drag is one gesture: the press and the release belong to it, and its path is movement.</summary>
    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
    {
        InputLimit.Spend(1);
        inner.Drag(button, startX, startY, endX, endY, durationMs, steps);
    }

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        InputLimit.Spend(1);
        inner.DragAlong(button, path, durationMs);
    }
}
