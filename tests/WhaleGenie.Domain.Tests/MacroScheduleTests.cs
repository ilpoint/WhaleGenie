using System;

using WhaleGenie.Models;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// When a timer trigger wants its macro to run. The clock itself is not asked here: every
/// question is answered from a time handed in, so the answers hold on any machine.
/// </summary>
public class MacroScheduleTests
{
    [Fact]
    public void An_interval_waits_its_gap_from_the_moment_it_is_asked_about()
    {
        var timer = new TimerSpec(Interval: 5, Unit: ScheduleUnit.Minutes);

        var from = new DateTime(2026, 10, 6, 9, 0, 0);

        Assert.True(MacroSchedule.TryNextDue(timer, from, out var due));
        Assert.Equal(from.AddMinutes(5), due);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(90, 90)]
    public void A_gap_that_would_spin_is_raised_to_the_shortest_wait(int written, int seconds)
    {
        var timer = new TimerSpec(Interval: written, Unit: ScheduleUnit.Seconds);

        Assert.Equal(TimeSpan.FromSeconds(seconds), MacroSchedule.Interval(timer));
    }

    [Fact]
    public void A_very_long_gap_is_kept_inside_what_a_date_can_hold()
    {
        var timer = new TimerSpec(Interval: 999_999, Unit: ScheduleUnit.Hours);

        Assert.Equal(TimeSpan.FromDays(365), MacroSchedule.Interval(timer));
    }

    [Theory]
    [InlineData("08:30", 8, 30, 0, true)]
    [InlineData("8:5", 8, 5, 0, true)]
    [InlineData(" 23:59:59 ", 23, 59, 59, true)]
    [InlineData("24:00", 0, 0, 0, false)]
    [InlineData("08:60", 0, 0, 0, false)]
    [InlineData("0830", 0, 0, 0, false)]
    [InlineData("08:30:99", 0, 0, 0, false)]
    [InlineData("", 0, 0, 0, false)]
    public void A_time_of_day_is_read_the_way_it_is_written(
        string text, int hours, int minutes, int seconds, bool expected)
    {
        Assert.Equal(expected, MacroSchedule.TryDaily(text, out var time));
        if (expected)
        {
            Assert.Equal(new TimeSpan(hours, minutes, seconds), time);
        }
    }

    [Fact]
    public void A_daily_time_that_has_gone_today_comes_round_tomorrow()
    {
        var timer = new TimerSpec(ScheduleMode.Daily, "08:30");

        Assert.True(MacroSchedule.TryNextDue(
            timer, new DateTime(2026, 10, 6, 8, 0, 0), out var today));
        Assert.Equal(new DateTime(2026, 10, 6, 8, 30, 0), today);

        Assert.True(MacroSchedule.TryNextDue(
            timer, new DateTime(2026, 10, 6, 21, 0, 0), out var tomorrow));
        Assert.Equal(new DateTime(2026, 10, 7, 8, 30, 0), tomorrow);
    }

    [Fact]
    public void A_daily_schedule_without_a_usable_time_never_comes_round()
    {
        var timer = new TimerSpec(ScheduleMode.Daily, "half past eight");

        Assert.False(MacroSchedule.TryNextDue(timer, DateTime.Now, out _));
    }

    [Fact]
    public void A_daily_schedule_is_described_as_a_time_of_day()
    {
        var timer = new TimerSpec(ScheduleMode.Daily, "21:15");

        Assert.Contains("21:15", MacroSchedule.Describe(timer));
    }

    [Fact]
    public void An_interval_schedule_is_described_with_its_gap_and_unit()
    {
        var timer = new TimerSpec(Interval: 5, Unit: ScheduleUnit.Minutes);

        var described = MacroSchedule.Describe(timer);

        Assert.Contains("5", described);
        Assert.Contains(WhaleGenie.Localization.Strings.Get("Schedule.Unit.Minutes"), described);
    }
}
