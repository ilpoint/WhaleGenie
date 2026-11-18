using System.Text.Json.Nodes;

using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// When a timer trigger wants its macro to run. The clock itself is not asked here: every
/// question is answered from a time handed in, so the answers hold on any machine.
/// </summary>
public class MacroScheduleTests
{
    [Fact]
    public void An_interval_waits_its_gap_from_the_moment_it_is_asked_about()
    {
        var macro = new MacroItem
        {
            TriggerMode = MacroTrigger.Timer,
            ScheduleMode = ScheduleMode.Interval,
            ScheduleInterval = 5,
            ScheduleUnit = ScheduleUnit.Minutes,
        };

        var from = new DateTime(2026, 10, 6, 9, 0, 0);

        Assert.True(MacroSchedule.TryNextDue(macro, from, out var due));
        Assert.Equal(from.AddMinutes(5), due);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(90, 90)]
    public void A_gap_that_would_spin_is_raised_to_the_shortest_wait(int written, int seconds)
    {
        var macro = new MacroItem { ScheduleInterval = written, ScheduleUnit = ScheduleUnit.Seconds };

        Assert.Equal(TimeSpan.FromSeconds(seconds), MacroSchedule.Interval(macro));
    }

    [Fact]
    public void A_very_long_gap_is_kept_inside_what_a_date_can_hold()
    {
        var macro = new MacroItem { ScheduleInterval = 999_999, ScheduleUnit = ScheduleUnit.Hours };

        Assert.Equal(TimeSpan.FromDays(365), MacroSchedule.Interval(macro));
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
        var macro = new MacroItem { ScheduleMode = ScheduleMode.Daily, ScheduleTime = "08:30" };

        Assert.True(MacroSchedule.TryNextDue(
            macro, new DateTime(2026, 10, 6, 8, 0, 0), out var today));
        Assert.Equal(new DateTime(2026, 10, 6, 8, 30, 0), today);

        Assert.True(MacroSchedule.TryNextDue(
            macro, new DateTime(2026, 10, 6, 21, 0, 0), out var tomorrow));
        Assert.Equal(new DateTime(2026, 10, 7, 8, 30, 0), tomorrow);
    }

    [Fact]
    public void A_daily_schedule_without_a_usable_time_never_comes_round()
    {
        var macro = new MacroItem { ScheduleMode = ScheduleMode.Daily, ScheduleTime = "half past eight" };

        Assert.False(MacroSchedule.TryNextDue(macro, DateTime.Now, out _));
    }

    [Fact]
    public void A_daily_schedule_is_previewed_as_a_time_of_day()
    {
        var macro = new MacroItem
        {
            TriggerMode = MacroTrigger.Timer,
            ScheduleMode = ScheduleMode.Daily,
            ScheduleTime = "21:15",
        };

        Assert.Contains("21:15", MacroSchedule.Describe(macro));
    }

    [Fact]
    public void A_timed_macro_is_previewed_with_its_gap_and_the_clock_icon()
    {
        var macro = new MacroItem
        {
            TriggerMode = MacroTrigger.Timer,
            ScheduleInterval = 5,
            ScheduleUnit = ScheduleUnit.Minutes,
        };

        Assert.True(macro.IsTimerTrigger);
        Assert.False(macro.IsKeyboardTrigger);
        Assert.Contains("5", macro.TriggerPreview);
        Assert.Contains(Strings.Get("Schedule.Unit.Minutes"), macro.TriggerPreview);
    }

    [Fact]
    public void A_schedule_survives_being_saved_and_read_back()
    {
        var macro = new MacroItem
        {
            Name = "Nightly",
            TriggerMode = MacroTrigger.Timer,
            ScheduleMode = ScheduleMode.Daily,
            ScheduleInterval = 90,
            ScheduleUnit = ScheduleUnit.Minutes,
            ScheduleTime = "21:15:30",
        };

        var restored = MacroItem.FromJson(macro.ToJson());

        Assert.Equal(MacroTrigger.Timer, restored.TriggerMode);
        Assert.Equal(ScheduleMode.Daily, restored.ScheduleMode);
        Assert.Equal(90, restored.ScheduleInterval);
        Assert.Equal(ScheduleUnit.Minutes, restored.ScheduleUnit);
        Assert.Equal("21:15:30", restored.ScheduleTime);
    }

    [Fact]
    public void A_macro_written_before_the_clock_existed_reads_back_with_a_usable_schedule()
    {
        var macro = MacroItem.FromJson(new JsonObject { ["name"] = "Old" });

        Assert.Equal(ScheduleMode.Interval, macro.ScheduleMode);
        Assert.Equal(5, macro.ScheduleInterval);
        Assert.Equal(ScheduleUnit.Seconds, macro.ScheduleUnit);
    }
}
