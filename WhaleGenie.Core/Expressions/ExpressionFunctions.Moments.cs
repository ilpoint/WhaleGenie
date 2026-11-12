using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;


namespace WhaleGenie.Core.Expressions;

// Dates and lengths of time: reading them, shifting them, and writing them back.
internal static partial class FunctionLibrary
{

    // ------------------------------------------------------------------- dates

    /// <summary>
    /// The shapes a date is read in. Everything a date function writes back is in the first
    /// one, so the answer of one function can be the argument of the next.
    /// </summary>
    private static readonly string[] MomentFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm",
        "yyyy/MM/dd",
        "HH:mm:ss",
        "HH:mm",
    ];

    /// <summary>
    /// Reads a date. A time of day on its own counts as today, which is what "wait until
    /// 09:30" means, and an empty argument is now, so a date function that is handed
    /// nothing still does something sensible instead of refusing to run.
    /// </summary>
    public static DateTimeOffset Moment(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return DateTimeOffset.Now;
        }

        if (DateTimeOffset.TryParseExact(trimmed, MomentFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
        {
            return Anchored(exact);
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var loose))
        {
            return loose;
        }

        throw ExpressionException.TypeMismatch($"\"{text}\" is not a date");
    }

    /// <summary>
    /// A clock time with no date on it belongs to today: "wait until 09:30" means the next one,
    /// and a text written in a pattern that only names a time reads the same way.
    /// </summary>
    private static DateTimeOffset Anchored(DateTimeOffset moment)
        => moment.Year == 1
            ? new DateTimeOffset(DateTime.Today.Add(moment.TimeOfDay), DateTimeOffset.Now.Offset)
            : moment;

    /// <summary>
    /// Reads a date out of text that is written in a pattern rather than in one of the shapes
    /// <see cref="Moment"/> already knows — a report that writes 01/03/2024, say. What comes back
    /// is stamped the way every date function writes, not in the pattern it was read in, so the
    /// answer can be handed straight to the next one.
    /// </summary>
    private static DateTimeOffset Parsed(IReadOnlyList<Func<Value>> args)
    {
        var text = S(args, 0).Trim();
        var pattern = S(args, 1);
        if (DateTimeOffset.TryParseExact(text, pattern, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
        {
            return Anchored(exact);
        }

        throw ExpressionException.TypeMismatch($"\"{text}\" is not a date written as \"{pattern}\"");
    }

    /// <summary>The day of the week counted the ISO way, so Monday is 1 and Sunday is 7.</summary>
    private static int Weekday(DateTimeOffset moment)
        => moment.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)moment.DayOfWeek;

    private static DateTimeOffset Shifted(IReadOnlyList<Func<Value>> args)
    {
        var moment = Moment(S(args, 0));
        var amount = N(args, 1);

        return S(args, 2).Trim().ToLowerInvariant() switch
        {
            "second" or "seconds" or "s" => moment.AddSeconds(amount),
            "minute" or "minutes" => moment.AddMinutes(amount),
            "hour" or "hours" or "h" => moment.AddHours(amount),
            "day" or "days" or "d" => moment.AddDays(amount),
            "week" or "weeks" or "w" => moment.AddDays(amount * 7),
            "month" or "months" => moment.AddMonths((int)Math.Round(amount)),
            "year" or "years" or "y" => moment.AddYears((int)Math.Round(amount)),
            _ => throw ExpressionException.TypeMismatch($"\"{S(args, 2)}\" is not a unit of time"),
        };
    }

    private static double Distance(IReadOnlyList<Func<Value>> args)
    {
        var from = Moment(S(args, 0));
        var to = Moment(S(args, 1));
        var span = to - from;

        return S(args, 2).Trim().ToLowerInvariant() switch
        {
            "second" or "seconds" or "s" => span.TotalSeconds,
            "minute" or "minutes" => span.TotalMinutes,
            "hour" or "hours" or "h" => span.TotalHours,
            "day" or "days" or "d" => span.TotalDays,
            "week" or "weeks" or "w" => span.TotalDays / 7,
            "month" or "months" => WholeMonths(from, to),
            "year" or "years" or "y" => WholeMonths(from, to) / 12.0,
            _ => throw ExpressionException.TypeMismatch($"\"{S(args, 2)}\" is not a unit of time"),
        };
    }

    /// <summary>Whole months between two dates, counting a part month as nothing.</summary>
    private static int WholeMonths(DateTimeOffset from, DateTimeOffset to)
    {
        var months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
        if (months > 0 && to.Day < from.Day)
        {
            months--;
        }
        else if (months < 0 && to.Day > from.Day)
        {
            months++;
        }

        return months;
    }

    private static string Formatted(IReadOnlyList<Func<Value>> args)
    {
        var pattern = S(args, 1);
        try
        {
            return Moment(S(args, 0)).ToString(pattern, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            throw ExpressionException.TypeMismatch($"\"{pattern}\" is not a date pattern");
        }
    }
}
