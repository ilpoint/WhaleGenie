using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WhaleGenie.Core.Expressions;

/// <summary>Everything a user can see about a built-in expression function.</summary>
public sealed record ExpressionFunction(string Name, string Signature, string Description);

/// <summary>One entry of the built-in function table.</summary>
internal sealed record FunctionEntry(
    string Name,
    string Signature,
    string Description,
    int MinArguments,
    int MaxArguments,
    Func<IReadOnlyList<Func<Value>>, Value> Body);

/// <summary>
/// The functions an expression can call, kept in one table so the evaluator and the
/// list the editor suggests can never drift apart.
/// </summary>
internal static class FunctionLibrary
{
    private const int Any = int.MaxValue;

    private static readonly FunctionEntry[] Entries =
    [
        // -------------------------------------------------------------------- text
        Fn("upper", "upper(text)", "Write a value in capitals.", 1, 1,
            args => Value.FromText(S(args, 0).ToUpperInvariant())),
        Fn("lower", "lower(text)", "Write a value in small letters.", 1, 1,
            args => Value.FromText(S(args, 0).ToLowerInvariant())),
        Fn("trim", "trim(text)", "Drop the spaces from both ends.", 1, 1,
            args => Value.FromText(S(args, 0).Trim())),
        Fn("length", "length(value)", "How many characters, or how many items a list holds.", 1, 1,
            args => Value.FromNumber(Length(V(args, 0)))),
        Fn("substring", "substring(text, start [, length])", "Part of some text; start counts from 0.", 2, 3,
            args => Value.FromText(Substring(args))),
        Fn("replace", "replace(text, old, new)", "Swap every occurrence of one piece of text for another.", 3, 3,
            args => Value.FromText(S(args, 0).Replace(S(args, 1), S(args, 2), StringComparison.OrdinalIgnoreCase))),
        Fn("replaceRegex", "replaceRegex(text, pattern, replacement)",
            "Swap everything the regular expression matches for the replacement, which can name the "
            + "pieces it captured as $1, $2 and so on. Write (?i) at the front of the pattern to "
            + "ignore case.", 3, 3,
            args => Value.FromText(Regex.Replace(S(args, 0), S(args, 1), S(args, 2)))),
        Fn("split", "split(text, separator)", "Break text into a list.", 2, 2,
            args => Value.FromList(Split(args))),
        Fn("join", "join(list, separator)", "Glue the items of a list into one text.", 2, 2,
            args => Value.FromText(string.Join(S(args, 1), L(args, 0).Select(item => item.AsText())))),
        Fn("contains", "contains(value, needle)", "True when the text, or a list item, holds the needle.", 2, 2,
            args => Value.FromBool(Contains(V(args, 0), S(args, 1)))),
        Fn("startsWith", "startsWith(text, prefix)", "True when the text begins with the prefix.", 2, 2,
            args => Value.FromBool(S(args, 0).StartsWith(S(args, 1), StringComparison.OrdinalIgnoreCase))),
        Fn("endsWith", "endsWith(text, suffix)", "True when the text ends with the suffix.", 2, 2,
            args => Value.FromBool(S(args, 0).EndsWith(S(args, 1), StringComparison.OrdinalIgnoreCase))),
        Fn("indexOf", "indexOf(value, needle)", "Where the needle sits, or -1 when it is missing.", 2, 2,
            args => Value.FromNumber(IndexOf(V(args, 0), S(args, 1)))),
        Fn("repeat", "repeat(text, times)", "Write the same text several times over.", 2, 2,
            args => Value.FromText(Repeat(S(args, 0), (int)N(args, 1)))),
        Fn("concat", "concat(value, ...)", "Join every argument into one text.", 1, Any,
            args => Value.FromText(string.Concat(args.Select(argument => argument().AsText())))),
        Fn("format", "format(template, ...)", "Replace {0}, {1} and so on in a template.", 1, Any,
            args => Value.FromText(Format(args))),
        Fn("match", "match(text, pattern)", "True when the regular expression matches the text.", 2, 2,
            args => Value.FromBool(Regex.IsMatch(S(args, 0), S(args, 1)))),
        Fn("extract", "extract(text, pattern)", "The first piece of text the regular expression matches.", 2, 2,
            args => Value.FromText(Extract(args))),

        // -------------------------------------------------------------------- maths
        Fn("abs", "abs(number)", "How far the number is from zero.", 1, 1,
            args => Value.FromNumber(Math.Abs(N(args, 0)))),
        Fn("round", "round(number [, digits])", "Round to a whole number, or to some decimal places.", 1, 2,
            args => Value.FromNumber(Round(args))),
        Fn("floor", "floor(number)", "Round down.", 1, 1,
            args => Value.FromNumber(Math.Floor(N(args, 0)))),
        Fn("ceil", "ceil(number)", "Round up.", 1, 1,
            args => Value.FromNumber(Math.Ceiling(N(args, 0)))),
        Fn("sqrt", "sqrt(number)", "Square root.", 1, 1,
            args => Value.FromNumber(Math.Sqrt(N(args, 0)))),
        Fn("pow", "pow(number, power)", "Raise a number to a power.", 2, 2,
            args => Value.FromNumber(Math.Pow(N(args, 0), N(args, 1)))),
        Fn("min", "min(number, ...)", "The smallest of the arguments.", 1, Any,
            args => Value.FromNumber(args.Select(argument => argument().AsNumber()).Min())),
        Fn("max", "max(number, ...)", "The largest of the arguments.", 1, Any,
            args => Value.FromNumber(args.Select(argument => argument().AsNumber()).Max())),
        Fn("sum", "sum(list)", "Add up every number in a list.", 1, 1,
            args => Value.FromNumber(L(args, 0).Sum(item => item.AsNumber()))),
        Fn("random", "random([min, max])", "A random number; with bounds, a whole number in that range.", 0, 2,
            args => Value.FromNumber(Random(args))),

        // -------------------------------------------------------------------- lists
        Fn("list", "list(...values)", "Build a list out of the arguments.", 0, Any,
            args => Value.FromList(args.Select(argument => argument()))),
        Fn("count", "count(list)", "How many items a list holds.", 1, 1,
            args => Value.FromNumber(V(args, 0).AsList().Count)),
        Fn("append", "append(list, ...values)", "A copy of the list with values added at the end.", 1, Any,
            args => Append(args)),
        Fn("insertAt", "insertAt(list, index, value)", "A copy with one value inserted at the index.", 3, 3,
            args => InsertAt(args)),
        Fn("removeAt", "removeAt(list, index)", "A copy without the item at the index.", 2, 2,
            args => RemoveAt(args)),
        Fn("get", "get(list, index)", "The item at the index.", 2, 2,
            args => At(L(args, 0), (int)N(args, 1), "get")),
        Fn("first", "first(list)", "The first item.", 1, 1,
            args => At(L(args, 0), 0, "first")),
        Fn("last", "last(list)", "The last item.", 1, 1,
            args => At(L(args, 0), -1, "last")),
        Fn("sort", "sort(list)", "A copy sorted from small to large.", 1, 1,
            args => Value.FromList(Sort(L(args, 0), descending: false))),
        Fn("sortDesc", "sortDesc(list)", "A copy sorted from large to small.", 1, 1,
            args => Value.FromList(Sort(L(args, 0), descending: true))),
        Fn("reverse", "reverse(list)", "A copy with the items in the other order.", 1, 1,
            args => Value.FromList(L(args, 0).Reverse())),
        Fn("unique", "unique(list)", "A copy without the repeated items.", 1, 1,
            args => Value.FromList(Unique(L(args, 0)))),
        Fn("slice", "slice(list, start [, length])", "Part of a list; start counts from 0.", 2, 3,
            args => Slice(args)),

        // ------------------------------------------------------------- conversion
        Fn("number", "number(value)", "Read a value as a number.", 1, 1,
            args => Value.FromNumber(N(args, 0))),
        Fn("text", "text(value)", "Read a value as text.", 1, 1,
            args => Value.FromText(S(args, 0))),
        Fn("bool", "bool(value)", "Read a value as true or false.", 1, 1,
            args => Value.FromBool(V(args, 0).AsBool())),
        Fn("if", "if(condition, whenTrue, whenFalse)", "Pick one of two values.", 3, 3,
            args => V(args, 0).AsBool() ? V(args, 1) : V(args, 2)),

        // -------------------------------------------------------------------- dates
        Fn("now", "now()", "The current date and time, as yyyy-MM-dd HH:mm:ss.", 0, 0,
            args => Value.FromText(Stamp(DateTimeOffset.Now))),
        Fn("today", "today()", "Today's date, as yyyy-MM-dd.", 0, 0,
            args => Value.FromText(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
        Fn("year", "year(date)", "The year of a date.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Year)),
        Fn("month", "month(date)", "The month of a date, 1 to 12.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Month)),
        Fn("day", "day(date)", "The day of the month, 1 to 31.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Day)),
        Fn("hour", "hour(date)", "The hour of a date, 0 to 23.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Hour)),
        Fn("minute", "minute(date)", "The minute of a date, 0 to 59.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Minute)),
        Fn("second", "second(date)", "The second of a date, 0 to 59.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Second)),
        Fn("weekday", "weekday(date)",
            "The day of the week, counted the ISO way: 1 is Monday and 7 is Sunday.", 1, 1,
            args => Value.FromNumber(Weekday(Moment(S(args, 0))))),
        Fn("dateAdd", "dateAdd(date, amount, unit)",
            "A date moved forward or back; the unit is seconds, minutes, hours, days, weeks, "
            + "months or years.", 3, 3,
            args => Value.FromText(Stamp(Shifted(args)))),
        Fn("dateDiff", "dateDiff(from, to, unit)",
            "How far apart two dates are, counted in the unit.", 3, 3,
            args => Value.FromNumber(Distance(args))),
        Fn("formatDate", "formatDate(date, pattern)",
            "A date written out in a pattern such as yyyy/MM/dd.", 2, 2,
            args => Value.FromText(Formatted(args))),
        Fn("parseDate", "parseDate(text, pattern)",
            "Read a date out of text written in a pattern such as yyyy/MM/dd. The answer is a date "
            + "like every other date function reads and writes, so it can go straight into "
            + "dateAdd, dateDiff or a comparison.", 2, 2,
            args => Value.FromText(Parsed(args))),

        // -------------------------------------------------------------------- json
        Fn("jsonGet", "jsonGet(json, path)",
            "A value inside JSON text, found by a path such as a.b[0].c. A field holding an "
            + "object or a list comes back as its own JSON text.", 2, 2,
            args => JsonGet(args)),
        Fn("jsonKeys", "jsonKeys(json)",
            "The names of the fields of a JSON object, in the order they are written.", 1, 1,
            args => Value.FromList(JsonKeys(S(args, 0)))),
        Fn("jsonHas", "jsonHas(json, path)", "True when the path is present in JSON text.", 2, 2,
            args => Value.FromBool(Located(args).Found)),
        Fn("jsonSet", "jsonSet(json, path, value)",
            "A copy of JSON text with the path set to a value, making the fields on the way.",
            3, 3, args => Value.FromText(JsonSet(args))),
        Fn("jsonOf", "jsonOf(name, value, ...)",
            "Names and values in turn, put together into one JSON object: jsonOf(\"a\", 1, "
            + "\"b\", 2) is {\"a\":1,\"b\":2}. This is how a macro keeps several things in one "
            + "variable and reads the fields back with jsonGet. A field holding another object "
            + "or a list is made with jsonSet and a path, because a value that is text stays "
            + "text.", 2, Any, args => Value.FromText(JsonOf(args))),
        Fn("toJson", "toJson(value)",
            "A value written as JSON text, so a list becomes an array.", 1, 1,
            args => Value.FromText(Node(V(args, 0))?.ToJsonString() ?? "null")),
    ];

    private static readonly Dictionary<string, FunctionEntry> ByName =
        Entries.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Names and signatures the expression editor offers as suggestions.</summary>
    public static IReadOnlyList<ExpressionFunction> Metadata { get; } =
        [.. Entries.Select(entry => new ExpressionFunction(entry.Name, entry.Signature, entry.Description))];

    public static bool TryGet(string name, out FunctionEntry entry)
    {
        if (ByName.TryGetValue(name, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    private static FunctionEntry Fn(string name, string signature, string description, int min, int max,
        Func<IReadOnlyList<Func<Value>>, Value> body)
        => new(name, signature, description, min, max, body);

    private static Value V(IReadOnlyList<Func<Value>> args, int index) => args[index]();

    private static string S(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsText();

    private static double N(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsNumber();

    private static IReadOnlyList<Value> L(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsList();

    private static List<Value> Copy(IReadOnlyList<Func<Value>> args, int index) => [.. args[index]().AsList()];

    private static double Length(Value value) => value.Kind switch
    {
        ValueKind.List => value.Items.Count,
        ValueKind.Null => 0,
        _ => value.AsText().Length,
    };

    private static string Substring(IReadOnlyList<Func<Value>> args)
    {
        var text = S(args, 0);
        var start = (int)N(args, 1);
        if (start < 0)
        {
            start += text.Length;
        }

        start = Math.Clamp(start, 0, text.Length);
        if (args.Count < 3)
        {
            return text[start..];
        }

        var length = Math.Clamp((int)N(args, 2), 0, text.Length - start);
        return text.Substring(start, length);
    }

    private static IEnumerable<Value> Split(IReadOnlyList<Func<Value>> args)
    {
        var text = S(args, 0);
        var separator = S(args, 1);
        var parts = separator.Length == 0
            ? text.Select(character => character.ToString())
            : text.Split(separator, StringSplitOptions.None);

        return parts.Select(part => Value.FromText(part));
    }

    private static bool Contains(Value value, string needle) => value.IsList
        ? value.Items.Any(item => item.AsText().Contains(needle, StringComparison.OrdinalIgnoreCase))
        : value.AsText().Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static double IndexOf(Value value, string needle)
    {
        if (!value.IsList)
        {
            return value.AsText().IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        for (var index = 0; index < value.Items.Count; index++)
        {
            if (value.Items[index].AsText().Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Repeat(string text, int times)
    {
        if (times <= 0 || text.Length == 0)
        {
            return string.Empty;
        }

        if ((long)times * text.Length > 1_000_000)
        {
            throw ExpressionException.TypeMismatch("the repeated text would be too long");
        }

        return string.Concat(Enumerable.Repeat(text, times));
    }

    private static string Format(IReadOnlyList<Func<Value>> args)
    {
        var template = S(args, 0);
        for (var index = 1; index < args.Count; index++)
        {
            template = template.Replace("{" + (index - 1) + "}", S(args, index), StringComparison.Ordinal);
        }

        return template;
    }

    private static string Extract(IReadOnlyList<Func<Value>> args)
    {
        var match = Regex.Match(S(args, 0), S(args, 1));
        return match.Success ? match.Value : string.Empty;
    }

    private static double Round(IReadOnlyList<Func<Value>> args)
    {
        var value = N(args, 0);
        return args.Count < 2
            ? Math.Round(value, MidpointRounding.AwayFromZero)
            : Math.Round(value, Math.Clamp((int)N(args, 1), 0, 15), MidpointRounding.AwayFromZero);
    }

    private static double Random(IReadOnlyList<Func<Value>> args) => args.Count switch
    {
        0 => System.Random.Shared.NextDouble(),
        1 => System.Random.Shared.Next((int)N(args, 0) + 1),
        _ => MinMax(args),
    };

    private static double MinMax(IReadOnlyList<Func<Value>> args)
    {
        var min = (int)N(args, 0);
        var max = (int)N(args, 1);
        return min > max ? min : System.Random.Shared.Next(min, max + 1);
    }

    private static Value Append(IReadOnlyList<Func<Value>> args)
    {
        var items = Copy(args, 0);
        for (var index = 1; index < args.Count; index++)
        {
            items.Add(args[index]());
        }

        return Value.FromList(items);
    }

    private static Value InsertAt(IReadOnlyList<Func<Value>> args)
    {
        var items = Copy(args, 0);
        items.Insert(Normalize((int)N(args, 1), items.Count, allowEnd: true), args[2]());
        return Value.FromList(items);
    }

    private static Value RemoveAt(IReadOnlyList<Func<Value>> args)
    {
        var items = Copy(args, 0);
        items.RemoveAt(Normalize((int)N(args, 1), items.Count, allowEnd: false));
        return Value.FromList(items);
    }

    private static Value At(IReadOnlyList<Value> items, int index, string name)
    {
        try
        {
            return items[Normalize(index, items.Count, allowEnd: false)];
        }
        catch (ExpressionException)
        {
            throw ExpressionException.IndexOutOfRange($"{name}({index})");
        }
    }

    /// <summary>Turns a possibly negative index into a real one, or refuses it.</summary>
    private static int Normalize(int index, int count, bool allowEnd)
    {
        if (index < 0)
        {
            index += count;
        }

        var limit = allowEnd ? count : count - 1;
        if (index < 0 || index > limit)
        {
            throw ExpressionException.IndexOutOfRange(index.ToString(CultureInfo.InvariantCulture));
        }

        return index;
    }

    private static IEnumerable<Value> Sort(IReadOnlyList<Value> items, bool descending)
    {
        IEnumerable<Value> ordered = items.All(item => item.IsNumber)
            ? items.OrderBy(item => item.Number)
            : items.OrderBy(item => item.AsText(), StringComparer.OrdinalIgnoreCase);

        return descending ? ordered.Reverse() : ordered;
    }

    private static IEnumerable<Value> Unique(IReadOnlyList<Value> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (seen.Add(item.AsText()))
            {
                yield return item;
            }
        }
    }

    private static Value Slice(IReadOnlyList<Func<Value>> args)
    {
        var items = L(args, 0);
        var start = (int)N(args, 1);
        if (start < 0)
        {
            start += items.Count;
        }

        start = Math.Clamp(start, 0, items.Count);
        var length = args.Count < 3
            ? items.Count - start
            : Math.Clamp((int)N(args, 2), 0, items.Count - start);

        return Value.FromList(items.Skip(start).Take(length));
    }

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
    private static DateTimeOffset Moment(string text)
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
    private static string Parsed(IReadOnlyList<Func<Value>> args)
    {
        var text = S(args, 0).Trim();
        var pattern = S(args, 1);
        if (DateTimeOffset.TryParseExact(text, pattern, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
        {
            return Stamp(Anchored(exact));
        }

        throw ExpressionException.TypeMismatch($"\"{text}\" is not a date written as \"{pattern}\"");
    }

    /// <summary>A date written back in the one shape every date function reads.</summary>
    private static string Stamp(DateTimeOffset moment)
        => moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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

    // -------------------------------------------------------------------- json

    /// <summary>Reads JSON text, refusing text that is not JSON so a typo shows up as a failure.</summary>
    private static JsonNode? ParseJson(string text)
    {
        if (text.Trim().Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw ExpressionException.TypeMismatch($"\"{text}\" is not JSON");
        }
    }

    /// <summary>The same read for the writer, which treats text that is not JSON as a fresh document.</summary>
    private static JsonNode? LooseJson(string text)
    {
        try
        {
            return ParseJson(text);
        }
        catch (ExpressionException)
        {
            return null;
        }
    }

    private static Value JsonGet(IReadOnlyList<Func<Value>> args)
    {
        var (found, node) = Located(args);
        return found ? AsValue(node) : Value.Null;
    }

    private static (bool Found, JsonNode? Node) Located(IReadOnlyList<Func<Value>> args)
    {
        var current = ParseJson(S(args, 0));
        if (current is null)
        {
            return (false, null);
        }

        foreach (var step in Path(S(args, 1)))
        {
            switch (step)
            {
                case int position when current is JsonArray array && position >= 0 && position < array.Count:
                    current = array[position];
                    break;
                case string name when current is JsonObject obj && obj.TryGetPropertyValue(name, out var child):
                    current = child;
                    break;
                default:
                    return (false, null);
            }
        }

        return (true, current);
    }

    private static IEnumerable<Value> JsonKeys(string text)
    {
        if (ParseJson(text) is not JsonObject obj)
        {
            return [];
        }

        return obj.Select(pair => Value.FromText(pair.Key));
    }

    /// <summary>
    /// A JSON node as one of the engine's values. An object cannot be one of them, so it
    /// comes back as the text the reader can hand straight to another JSON function.
    /// </summary>
    private static Value AsValue(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return Value.Null;
            case JsonArray array:
                return Value.FromList(array.Select(item => AsValue(item)));
            case JsonObject obj:
                return Value.FromText(obj.ToJsonString());
            case JsonValue scalar:
                return Scalar(scalar);
            default:
                return Value.Null;
        }
    }

    private static Value Scalar(JsonValue value)
    {
        if (value.TryGetValue<bool>(out var flag))
        {
            return Value.FromBool(flag);
        }

        if (value.TryGetValue<double>(out var number))
        {
            return Value.FromNumber(number);
        }

        return value.TryGetValue<string>(out var text)
            ? Value.FromText(text)
            : Value.FromText(value.ToJsonString());
    }

    /// <summary>
    /// Names and values in turn, made into one object and kept in the order they were written.
    /// A name with no value after it is a mistake worth saying out loud, because joining the two
    /// halves up wrongly would otherwise quietly produce a document with the wrong fields in it.
    /// </summary>
    private static string JsonOf(IReadOnlyList<Func<Value>> args)
    {
        if (args.Count % 2 != 0)
        {
            throw ExpressionException.ArgumentCount(
                $"jsonOf({args.Count} arguments) - names and values have to come in pairs");
        }

        var made = new JsonObject();
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = S(args, index).Trim();
            if (name.Length == 0)
            {
                throw ExpressionException.TypeMismatch("a field of an object has to be named");
            }

            made[name] = Node(args[index + 1]());
        }

        return made.ToJsonString();
    }

    private static string JsonSet(IReadOnlyList<Func<Value>> args)
    {
        var steps = Path(S(args, 1));
        var value = Node(V(args, 2));
        if (steps.Count == 0)
        {
            // Setting the document itself replaces it, so a macro can start from nothing.
            return value?.ToJsonString() ?? "null";
        }

        JsonNode root = LooseJson(S(args, 0)) ?? Empty(steps[0]);
        if (root is not JsonObject && root is not JsonArray)
        {
            root = Empty(steps[0]);
        }

        var current = root;
        for (var index = 0; index < steps.Count - 1; index++)
        {
            current = Step(current, steps[index], steps[index + 1]);
        }

        Put(current, steps[^1], value);
        return root.ToJsonString();
    }

    /// <summary>A fresh container: a list when the path steps into a position, an object otherwise.</summary>
    private static JsonNode Empty(object step) => step is int ? new JsonArray() : new JsonObject();

    /// <summary>
    /// The part of the document at a path step, made on the way when the path names a field
    /// that is not there yet.
    /// </summary>
    private static JsonNode Step(JsonNode current, object step, object next)
    {
        if (step is int position)
        {
            var array = AsArray(current, position);
            while (array.Count <= position)
            {
                array.Add(null);
            }

            if (array[position] is { } made)
            {
                return made;
            }

            var created = Empty(next);
            array[position] = created;
            return created;
        }

        if (current is not JsonObject obj)
        {
            throw ExpressionException.TypeMismatch(
                $"the path names \"{step}\" but that part is not an object");
        }

        var name = (string)step;
        if (obj.TryGetPropertyValue(name, out var child) && child is not null)
        {
            return child;
        }

        var container = Empty(next);
        obj[name] = container;
        return container;
    }

    private static void Put(JsonNode current, object step, JsonNode? value)
    {
        if (step is int position)
        {
            var array = AsArray(current, position);
            while (array.Count <= position)
            {
                array.Add(null);
            }

            array[position] = value;
            return;
        }

        if (current is not JsonObject obj)
        {
            throw ExpressionException.TypeMismatch(
                $"the path names \"{step}\" but that part is not an object");
        }

        obj[(string)step] = value;
    }

    private static JsonArray AsArray(JsonNode current, int position)
        => current as JsonArray
           ?? throw ExpressionException.TypeMismatch(
               $"the path steps into [{position}] but that part is not a list");

    /// <summary>A value written as JSON, so it can be put into a document or sent on.</summary>
    private static JsonNode? Node(Value value)
    {
        switch (value.Kind)
        {
            case ValueKind.Null:
                return null;
            case ValueKind.Number:
                return JsonValue.Create(value.Number);
            case ValueKind.Bool:
                return JsonValue.Create(value.Flag);
            case ValueKind.Text:
                return JsonValue.Create(value.Text);
            default:
                var array = new JsonArray();
                foreach (var item in value.Items)
                {
                    array.Add(Node(item));
                }

                return array;
        }
    }

    /// <summary>
    /// Reads a path such as <c>a.b[0].c</c> or <c>$.a.b</c> into the field names and the
    /// positions it walks through, in the order they are walked.
    /// </summary>
    private static List<object> Path(string text)
    {
        var parts = new List<object>();
        var trimmed = text.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith('$'))
        {
            trimmed = trimmed[1..];
        }

        var name = new StringBuilder();
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (character == '.')
            {
                TakeName();
                continue;
            }

            if (character != '[')
            {
                name.Append(character);
                continue;
            }

            TakeName();
            var close = trimmed.IndexOf(']', index);
            if (close < 0)
            {
                throw ExpressionException.TypeMismatch($"the path \"{text}\" has an unclosed [");
            }

            var inside = trimmed[(index + 1)..close].Trim().Trim('\'', '"');
            if (inside.Length > 0)
            {
                parts.Add(int.TryParse(inside, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var position) ? position : inside);
            }

            index = close;
        }

        TakeName();
        return parts;

        void TakeName()
        {
            if (name.Length > 0)
            {
                parts.Add(name.ToString());
                name.Clear();
            }
        }
    }
}
