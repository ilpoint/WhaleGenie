using System;
using System.Globalization;

using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// When a timer trigger wants the macro to run, and how to say that in words.
/// </summary>
/// <remarks>
/// The arithmetic sits here rather than inside the trigger service so the macro card and the
/// tests can ask the same question the service asks, without watching a clock.
/// </remarks>
public static class MacroSchedule
{
    /// <summary>The shortest wait an interval schedule may use, so a run cannot spin.</summary>
    private static readonly TimeSpan ShortestGap = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest wait an interval schedule may use. A year is far past anything a macro wants
    /// and keeps the date arithmetic below well inside the range a date can hold.
    /// </summary>
    private static readonly TimeSpan LongestGap = TimeSpan.FromDays(365);

    /// <summary>The wait between the runs of an interval schedule, clamped to something sane.</summary>
    public static TimeSpan Interval(MacroItem macro)
        => Interval(macro.ScheduleInterval, macro.ScheduleUnit);

    public static TimeSpan Interval(TriggerEntry entry)
        => Interval(entry.ScheduleInterval, entry.ScheduleUnit);

    private static TimeSpan Interval(int interval, ScheduleUnit unit)
    {
        var value = Math.Max(1, interval);
        var gap = unit switch
        {
            ScheduleUnit.Hours => TimeSpan.FromHours(value),
            ScheduleUnit.Minutes => TimeSpan.FromMinutes(value),
            _ => TimeSpan.FromSeconds(value),
        };

        return gap < ShortestGap ? ShortestGap : gap > LongestGap ? LongestGap : gap;
    }

    /// <summary>
    /// Reads a time of day written the way a person writes it (<c>08:30</c> or <c>08:30:15</c>).
    /// Anything else is refused rather than guessed at, so a typo cannot silently become midnight.
    /// </summary>
    public static bool TryDaily(string? text, out TimeSpan time)
    {
        time = default;

        var parts = (text ?? string.Empty).Trim().Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || hours is < 0 or > 23)
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes is < 0 or > 59)
        {
            return false;
        }

        var seconds = 0;
        if (parts.Length == 3
            && (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
                || seconds is < 0 or > 59))
        {
            return false;
        }

        time = new TimeSpan(hours, minutes, seconds);
        return true;
    }

    /// <summary>
    /// The next moment this schedule wants the macro to run, counted from
    /// <paramref name="from"/> and never landing on it: an interval waits its gap, and a daily
    /// time that has already gone today comes round tomorrow.
    /// </summary>
    public static bool TryNextDue(MacroItem macro, DateTime from, out DateTime due)
        => TryNextDue(macro.ScheduleMode, macro.ScheduleTime, macro.ScheduleInterval,
            macro.ScheduleUnit, from, out due);

    public static bool TryNextDue(TriggerEntry entry, DateTime from, out DateTime due)
        => TryNextDue(entry.ScheduleMode, entry.ScheduleTime, entry.ScheduleInterval,
            entry.ScheduleUnit, from, out due);

    private static bool TryNextDue(ScheduleMode mode, string timeText, int interval,
        ScheduleUnit unit, DateTime from, out DateTime due)
    {
        if (mode == ScheduleMode.Daily)
        {
            if (!TryDaily(timeText, out var time))
            {
                due = default;
                return false;
            }

            var today = from.Date + time;
            due = today > from ? today : today.AddDays(1);
            return true;
        }

        due = from + Interval(interval, unit);
        return true;
    }

    /// <summary>How a person would say this schedule, for the macro card.</summary>
    public static string Describe(MacroItem macro)
        => Describe(macro.ScheduleMode, macro.ScheduleTime, macro.ScheduleInterval, macro.ScheduleUnit);

    public static string Describe(TriggerEntry entry)
        => Describe(entry.ScheduleMode, entry.ScheduleTime, entry.ScheduleInterval, entry.ScheduleUnit);

    private static string Describe(ScheduleMode mode, string timeText, int interval, ScheduleUnit unit)
    {
        if (mode == ScheduleMode.Daily)
        {
            return TryDaily(timeText, out var time)
                ? Strings.Format("Schedule.DailySummary", Spell(time))
                : Strings.Get("Schedule.DailyUnset");
        }

        var unitName = unit switch
        {
            ScheduleUnit.Hours => Strings.Get("Schedule.Unit.Hours"),
            ScheduleUnit.Minutes => Strings.Get("Schedule.Unit.Minutes"),
            _ => Strings.Get("Schedule.Unit.Seconds"),
        };

        return Strings.Format("Schedule.IntervalSummary", Math.Max(1, interval), unitName);
    }

    /// <summary>A time of day as <c>08:30</c>, with the seconds only when there are any.</summary>
    private static string Spell(TimeSpan time)
        => time.Seconds == 0
            ? $"{time.Hours:D2}:{time.Minutes:D2}"
            : $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
}
