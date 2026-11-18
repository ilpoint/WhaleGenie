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

// The table of built-in functions, grouped by what they work on, and the group keys they carry.
internal static partial class FunctionLibrary
{
    private const int Any = int.MaxValue;

    /// <summary>
    /// The groups the table is laid out in, in the order the editor offers them. A key rather than
    /// a name: what the reader sees is <c>Expr.Group.&lt;key&gt;</c> out of the text tables, so the
    /// editor has nothing in it that only one language can read.
    /// </summary>
    internal const string TextGroup = "text";

    internal const string MathsGroup = "maths";

    internal const string ListsGroup = "lists";

    internal const string DatesGroup = "dates";

    internal const string RecordsGroup = "records";

    /// <summary>
    /// The ones that are about a value rather than about a kind of thing: reading one as another,
    /// and choosing between two.
    /// </summary>
    internal const string OtherGroup = "other";

    private static readonly FunctionEntry[] Entries =
    [
        // -------------------------------------------------------------------- text
        Fn(TextGroup, "upper", "upper(text)", "Write a value in capitals.", 1, 1,
            args => Value.FromText(S(args, 0).ToUpperInvariant())),
        Fn(TextGroup, "lower", "lower(text)", "Write a value in small letters.", 1, 1,
            args => Value.FromText(S(args, 0).ToLowerInvariant())),
        Fn(TextGroup, "trim", "trim(text)", "Drop the spaces from both ends.", 1, 1,
            args => Value.FromText(S(args, 0).Trim())),
        Fn(TextGroup, "length", "length(value)", "How many characters, or how many items a list holds.", 1, 1,
            args => Value.FromNumber(Length(V(args, 0)))),
        Fn(TextGroup, "substring", "substring(text, start [, length])", "Part of some text; start counts from 0.", 2, 3,
            args => Value.FromText(Substring(args))),
        Fn(TextGroup, "replace", "replace(text, old, new)", "Swap every occurrence of one piece of text for another.", 3, 3,
            args => Value.FromText(S(args, 0).Replace(S(args, 1), S(args, 2), StringComparison.OrdinalIgnoreCase))),
        Fn(TextGroup, "replaceRegex", "replaceRegex(text, pattern, replacement)",
            "Swap everything the regular expression matches for the replacement, which can name the "
            + "pieces it captured as $1, $2 and so on. Write (?i) at the front of the pattern to "
            + "ignore case.", 3, 3,
            args => Value.FromText(RegexGuard.Replace(S(args, 0), S(args, 1), S(args, 2)))),
        Fn(TextGroup, "split", "split(text, separator)", "Break text into a list.", 2, 2,
            args => Value.FromList(Split(args))),
        Fn(TextGroup, "join", "join(list, separator)", "Glue the items of a list into one text.", 2, 2,
            args => Value.FromText(string.Join(S(args, 1), L(args, 0).Select(item => item.AsText())))),
        Fn(TextGroup, "contains", "contains(value, needle)", "True when the text, or a list item, holds the needle.", 2, 2,
            args => Value.FromBool(Contains(V(args, 0), S(args, 1)))),
        Fn(TextGroup, "compare", "compare(value, comparison, needle [, caseSensitive, trim, multiline])",
            "Compare one value with the shared text and number ways.", 3, 6, args => Compare(args)),
        Fn(TextGroup, "startsWith", "startsWith(text, prefix)", "True when the text begins with the prefix.", 2, 2,
            args => Value.FromBool(S(args, 0).StartsWith(S(args, 1), StringComparison.OrdinalIgnoreCase))),
        Fn(TextGroup, "endsWith", "endsWith(text, suffix)", "True when the text ends with the suffix.", 2, 2,
            args => Value.FromBool(S(args, 0).EndsWith(S(args, 1), StringComparison.OrdinalIgnoreCase))),
        Fn(TextGroup, "indexOf", "indexOf(value, needle)", "Where the needle sits, or -1 when it is missing.", 2, 2,
            args => Value.FromNumber(IndexOf(V(args, 0), S(args, 1)))),
        Fn(TextGroup, "repeat", "repeat(text, times)", "Write the same text several times over.", 2, 2,
            args => Value.FromText(Repeat(S(args, 0), (int)N(args, 1)))),
        Fn(TextGroup, "concat", "concat(value, ...)", "Join every argument into one text.", 1, Any,
            args => Value.FromText(string.Concat(args.Select(argument => argument().AsText())))),
        Fn(TextGroup, "format", "format(template, ...)", "Replace {0}, {1} and so on in a template.", 1, Any,
            args => Value.FromText(Format(args))),
        Fn(TextGroup, "match", "match(text, pattern)", "True when the regular expression matches the text.", 2, 2,
            args => Value.FromBool(RegexGuard.IsMatch(S(args, 0), S(args, 1)))),
        Fn(TextGroup, "extract", "extract(text, pattern)", "The first piece of text the regular expression matches.", 2, 2,
            args => Value.FromText(Extract(args))),

        // -------------------------------------------------------------------- maths
        Fn(MathsGroup, "abs", "abs(number)", "How far the number is from zero.", 1, 1,
            args => Value.FromNumber(Math.Abs(N(args, 0)))),
        Fn(MathsGroup, "round", "round(number [, digits])", "Round to a whole number, or to some decimal places.", 1, 2,
            args => Value.FromNumber(Round(args))),
        Fn(MathsGroup, "floor", "floor(number)", "Round down.", 1, 1,
            args => Value.FromNumber(Math.Floor(N(args, 0)))),
        Fn(MathsGroup, "ceil", "ceil(number)", "Round up.", 1, 1,
            args => Value.FromNumber(Math.Ceiling(N(args, 0)))),
        Fn(MathsGroup, "sqrt", "sqrt(number)", "Square root.", 1, 1,
            args => Value.FromNumber(Math.Sqrt(N(args, 0)))),
        Fn(MathsGroup, "pow", "pow(number, power)", "Raise a number to a power.", 2, 2,
            args => Value.FromNumber(Math.Pow(N(args, 0), N(args, 1)))),
        Fn(MathsGroup, "min", "min(number, ...)",
            "The smallest of the arguments; the earliest one when they are dates.", 1, Any,
            args => Pick(args, smallest: true)),
        Fn(MathsGroup, "max", "max(number, ...)",
            "The largest of the arguments; the latest one when they are dates.", 1, Any,
            args => Pick(args, smallest: false)),
        Fn(MathsGroup, "sum", "sum(list)", "Add up every number in a list.", 1, 1,
            args => Value.FromNumber(L(args, 0).Sum(item => item.AsNumber()))),
        Fn(MathsGroup, "random", "random([min, max])", "A random number; with bounds, a whole number in that range.", 0, 2,
            args => Value.FromNumber(Random(args))),

        // -------------------------------------------------------------------- lists
        Fn(ListsGroup, "list", "list(...values)", "Build a list out of the arguments.", 0, Any,
            args => Value.FromList(args.Select(argument => argument()))),
        Fn(ListsGroup, "count", "count(list)", "How many items a list holds.", 1, 1,
            args => Value.FromNumber(V(args, 0).AsList().Count)),
        Fn(ListsGroup, "append", "append(list, ...values)", "A copy of the list with values added at the end.", 1, Any,
            args => Append(args)),
        Fn(ListsGroup, "insertAt", "insertAt(list, index, value)", "A copy with one value inserted at the index.", 3, 3,
            args => InsertAt(args)),
        Fn(ListsGroup, "removeAt", "removeAt(list, index)", "A copy without the item at the index.", 2, 2,
            args => RemoveAt(args)),
        Fn(ListsGroup, "get", "get(list, index)", "The item at the index.", 2, 2,
            args => At(L(args, 0), (int)N(args, 1), "get")),
        Fn(ListsGroup, "first", "first(list)", "The first item.", 1, 1,
            args => At(L(args, 0), 0, "first")),
        Fn(ListsGroup, "last", "last(list)", "The last item.", 1, 1,
            args => At(L(args, 0), -1, "last")),
        Fn(ListsGroup, "sort", "sort(list)", "A copy sorted from small to large.", 1, 1,
            args => Value.FromList(Sort(L(args, 0), descending: false))),
        Fn(ListsGroup, "sortDesc", "sortDesc(list)", "A copy sorted from large to small.", 1, 1,
            args => Value.FromList(Sort(L(args, 0), descending: true))),
        Fn(ListsGroup, "reverse", "reverse(list)", "A copy with the items in the other order.", 1, 1,
            args => Value.FromList(L(args, 0).Reverse())),
        Fn(ListsGroup, "unique", "unique(list)", "A copy without the repeated items.", 1, 1,
            args => Value.FromList(Unique(L(args, 0)))),
        Fn(ListsGroup, "slice", "slice(list, start [, length])", "Part of a list; start counts from 0.", 2, 3,
            args => Slice(args)),
        Fn(ListsGroup, "where", "where(list, field, compare, value [, caseSensitive, trim])",
            "Keep the records whose field answers the comparison. Text uses the same five ways as "
            + "every text comparison; numbers also take >, >=, <, <=, = and !=. A row with no such "
            + "field, or one that is not a number when a number is asked for, is left out and "
            + "reported in the run log.", 4, 6, args => Where(args)),
        Fn(ListsGroup, "column", "column(list, field)",
            "One value per record, taken from the same field, as a list. A row without the field is "
            + "left out and reported in the run log.", 2, 2, args => Column(args)),

        // ------------------------------------------------------------- conversion
        Fn(OtherGroup, "number", "number(value)", "Read a value as a number.", 1, 1,
            args => Value.FromNumber(N(args, 0))),
        Fn(OtherGroup, "text", "text(value)", "Read a value as text.", 1, 1,
            args => Value.FromText(S(args, 0))),
        Fn(OtherGroup, "bool", "bool(value)", "Read a value as true or false.", 1, 1,
            args => Value.FromBool(V(args, 0).AsBool())),
        Fn(OtherGroup, "if", "if(condition, whenTrue, whenFalse)", "Pick one of two values.", 3, 3,
            args => V(args, 0).AsBool() ? V(args, 1) : V(args, 2)),

        // -------------------------------------------------------------------- dates
        Fn(DatesGroup, "now", "now()", "The current date and time, as yyyy-MM-dd HH:mm:ss.", 0, 0,
            args => Value.FromDate(DateTimeOffset.Now)),
        Fn(DatesGroup, "today", "today()", "Today's date, as yyyy-MM-dd.", 0, 0,
            args => Value.FromDate(new DateTimeOffset(DateTime.Today))),
        Fn(DatesGroup, "year", "year(date)", "The year of a date.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Year)),
        Fn(DatesGroup, "month", "month(date)", "The month of a date, 1 to 12.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Month)),
        Fn(DatesGroup, "day", "day(date)", "The day of the month, 1 to 31.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Day)),
        Fn(DatesGroup, "hour", "hour(date)", "The hour of a date, 0 to 23.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Hour)),
        Fn(DatesGroup, "minute", "minute(date)", "The minute of a date, 0 to 59.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Minute)),
        Fn(DatesGroup, "second", "second(date)", "The second of a date, 0 to 59.", 1, 1,
            args => Value.FromNumber(Moment(S(args, 0)).Second)),
        Fn(DatesGroup, "weekday", "weekday(date)",
            "The day of the week, counted the ISO way: 1 is Monday and 7 is Sunday.", 1, 1,
            args => Value.FromNumber(Weekday(Moment(S(args, 0))))),
        Fn(DatesGroup, "dateAdd", "dateAdd(date, amount, unit)",
            "A date moved forward or back; the unit is seconds, minutes, hours, days, weeks, "
            + "months or years.", 3, 3,
            args => Value.FromDate(Shifted(args))),
        Fn(DatesGroup, "dateDiff", "dateDiff(from, to, unit)",
            "How far apart two dates are, counted in the unit.", 3, 3,
            args => Value.FromNumber(Distance(args))),
        Fn(DatesGroup, "formatDate", "formatDate(date, pattern)",
            "A date written out in a pattern such as yyyy/MM/dd.", 2, 2,
            args => Value.FromText(Formatted(args))),
        Fn(DatesGroup, "parseDate", "parseDate(text, pattern)",
            "Read a date out of text written in a pattern such as yyyy/MM/dd. The answer is a date "
            + "like every other date function reads and writes, so it can go straight into "
            + "dateAdd, dateDiff or a comparison.", 2, 2,
            args => Value.FromDate(Parsed(args))),

        // -------------------------------------------------------------------- json
        Fn(RecordsGroup, "jsonGet", "jsonGet(json, path)",
            "A value inside JSON text, found by a path such as a.b[0].c. A field holding an "
            + "object or a list comes back as its own JSON text.", 2, 2,
            args => JsonGet(args)),
        Fn(RecordsGroup, "jsonKeys", "jsonKeys(json)",
            "The names of the fields of a JSON object, in the order they are written.", 1, 1,
            args => Value.FromList(JsonKeys(S(args, 0)))),
        Fn(RecordsGroup, "jsonHas", "jsonHas(json, path)", "True when the path is present in JSON text.", 2, 2,
            args => Value.FromBool(Located(args).Found)),
        Fn(RecordsGroup, "jsonSet", "jsonSet(json, path, value)",
            "A copy of JSON text with the path set to a value, making the fields on the way.",
            3, 3, args => Value.FromText(JsonSet(args))),
        Fn(RecordsGroup, "jsonOf", "jsonOf(name, value, ...)",
            "Names and values in turn, put together into one JSON object: jsonOf(\"a\", 1, "
            + "\"b\", 2) is {\"a\":1,\"b\":2}. This is how a macro keeps several things in one "
            + "variable and reads the fields back with jsonGet. A field holding another object "
            + "or a list is made with jsonSet and a path, because a value that is text stays "
            + "text.", 2, Any, args => Value.FromText(JsonOf(args))),
        Fn(RecordsGroup, "toJson", "toJson(value)",
            "A value written as JSON text, so a list becomes an array.", 1, 1,
            args => Value.FromText(JsonValues.Written(V(args, 0)))),
        Fn(RecordsGroup, "field", "field(record, name)", "One field of a record, such as the x of a hit. The "
            + "fields the record does have are listed when the name is not one of them.", 2, 2,
            args => Field(args)),
    ];
}
