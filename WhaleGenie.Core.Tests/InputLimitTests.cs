using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The ceiling on how fast input may leave. What is checked here is the accounting — how many events
/// each kind of call spends — and the spacing, read from a clock the check advances itself: a limit
/// about time cannot be sat through in a test.
/// </summary>
public class InputLimitTests
{
    [Fact]
    public void Events_are_spaced_by_one_second_divided_by_the_limit()
    {
        using var limit = new Limit(10);

        limit.Spend(1);
        Assert.Empty(limit.Waits);

        limit.Spend(1);

        // 每秒十个事件：两个之间隔 100 毫秒。
        Assert.Single(limit.Waits);
        Assert.InRange(limit.Waits[0].TotalMilliseconds, 99, 101);
    }

    [Fact]
    public void A_batch_is_spaced_as_the_events_it_stands_for()
    {
        using var limit = new Limit(10);
        limit.Spend(1);

        limit.Spend(3);

        Assert.InRange(limit.Waits.Single().TotalMilliseconds, 299, 301);
    }

    /// <summary>
    /// 上限是天花板而不是节拍器：慢慢发的两个事件不必等。
    /// </summary>
    [Fact]
    public void Events_that_are_already_slow_enough_are_not_held_back()
    {
        using var limit = new Limit(10);
        limit.Spend(1);

        limit.Advance(TimeSpan.FromSeconds(5));
        limit.Spend(1);

        Assert.Empty(limit.Waits);
    }

    [Fact]
    public void Zero_turns_the_limit_off()
    {
        using var limit = new Limit(0);

        for (var index = 0; index < 100; index++)
        {
            limit.Spend(1);
        }

        Assert.Empty(limit.Waits);
    }

    [Fact]
    public void A_chord_counts_once_and_moving_the_pointer_does_not_count()
    {
        using var limit = new Limit(10);
        var devices = new ThrottledInputDevice(new Recorder());

        devices.KeyPress("a", 0);
        devices.Hotkey(["ctrl", "shift", "s"], 0);
        devices.MoveMouseAlong([new ScreenPoint(0, 0), new ScreenPoint(500, 400)], 10);
        devices.MoveMouse(10, 10, 0);

        // 只花了两个事件：组合键一次，移动一次也没有。
        Assert.Single(limit.Waits);
        Assert.InRange(limit.Waits[0].TotalMilliseconds, 99, 101);
    }

    /// <summary>
    /// 打字按字拉开间隔，而不是先等一整批再一次性打出来：后者看起来和没限制一样。
    /// </summary>
    [Fact]
    public void Typing_is_paced_character_by_character()
    {
        using var limit = new Limit(10);
        var inner = new Recorder();
        var devices = new ThrottledInputDevice(inner);

        devices.TypeText("hello", 0);

        // 每秒十个事件就是每个字间隔 100 毫秒；这一步自己要的 0 毫秒被抬到这条线上。
        Assert.Equal(100, inner.TypedInterval);
        Assert.Equal("hello", inner.Typed);
    }

    /// <summary>步骤自己要求更慢的间隔时，以它为准：上限只压过快的，不加快本来就慢的。</summary>
    [Fact]
    public void Typing_keeps_a_slower_interval_the_step_asked_for()
    {
        using var limit = new Limit(10);
        var inner = new Recorder();

        new ThrottledInputDevice(inner).TypeText("hi", 250);

        Assert.Equal(250, inner.TypedInterval);
    }

    [Fact]
    public void A_double_click_is_counted_as_two()
    {
        using var limit = new Limit(10);
        var devices = new ThrottledInputDevice(new Recorder());

        devices.KeyPress("a", 0);
        devices.Click("left", 0, 0, 2, 0);

        // 一个按键加两次点击是三个事件，中间两个间隔。
        Assert.InRange(limit.Waits.Single().TotalMilliseconds, 199, 201);
    }

    [Fact]
    public void Each_way_of_sending_shares_the_same_limit()
    {
        using var limit = new Limit(10);
        var front = new ThrottledInputDevice(new Recorder());
        var background = new ThrottledInputDevice(new Recorder());

        front.KeyPress("a", 0);
        background.KeyPress("b", 0);

        Assert.Single(limit.Waits);
    }

    /// <summary>One limit in force, and the clock this check moves by hand.</summary>
    private sealed class Limit : IDisposable
    {
        private readonly Func<long> _clock;
        private readonly Action<TimeSpan> _wait;
        private long _now;

        public Limit(int perSecond)
        {
            _clock = InputLimit.Clock;
            _wait = InputLimit.Wait;

            InputLimit.Reset();
            InputLimit.PerSecond = perSecond;
            InputLimit.Clock = () => _now;
            InputLimit.Wait = time =>
            {
                Waits.Add(time);
                _now += (long)(time.TotalSeconds * Stopwatch.Frequency);
            };
        }

        public List<TimeSpan> Waits { get; } = [];

        public void Spend(int events) => InputLimit.Spend(events);

        public void Advance(TimeSpan by) => _now += (long)(by.TotalSeconds * Stopwatch.Frequency);

        public void Dispose()
        {
            InputLimit.Clock = _clock;
            InputLimit.Wait = _wait;
            InputLimit.PerSecond = InputLimit.Usual;
            InputLimit.Reset();
        }
    }

    /// <summary>A device that only remembers being asked, so the accounting can be read off the waits.</summary>
    private sealed class Recorder : IInputDevice
    {
        public ScreenPoint Cursor => default;

        public List<string> Calls { get; } = [];

        /// <summary>What the last text was, and the interval it was asked to be typed at.</summary>
        public string Typed { get; private set; } = string.Empty;

        public int TypedInterval { get; private set; }

        public void KeyPress(string key, int holdMs) => Calls.Add(key);

        public void KeyDown(string key) => Calls.Add(key);

        public void KeyUp(string key) => Calls.Add(key);

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => Calls.Add(string.Join('+', keys));

        public void TypeText(string text, int intervalMs)
        {
            Typed = text;
            TypedInterval = intervalMs;
            Calls.Add(text);
        }

        public void MoveMouse(int x, int y, int durationMs) => Calls.Add("move");

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs) => Calls.Add("along");

        public void MoveMouseRelative(int dx, int dy, int durationMs) => Calls.Add("relative");

        public void MouseDown(string button, int x, int y) => Calls.Add(button);

        public void MouseUp(string button, int x, int y) => Calls.Add(button);

        public void Click(string button, int x, int y, int clicks, int intervalMs)
            => Calls.Add($"{button}x{clicks}");

        public void Scroll(string direction, int delta, int x, int y) => Calls.Add(direction);

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
            => Calls.Add("drag");

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => Calls.Add("dragAlong");
    }
}
