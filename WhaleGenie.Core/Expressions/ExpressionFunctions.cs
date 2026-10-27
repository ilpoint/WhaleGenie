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

/// <summary>
/// Everything a user can see about a built-in expression function: what it is called, how it is
/// written, one line saying what it does, and which group it belongs in. The group is a plain key
/// rather than a name, because the name is written in the reader's language and the key is what
/// the editor groups by.
/// </summary>
public sealed record ExpressionFunction(
    string Name,
    string Signature,
    string Description,
    string Group,
    IReadOnlyList<ExpressionParameter> Parameters,
    int MinArguments,
    int MaxArguments);

/// <summary>
/// One argument a function takes, as the editor shows it: the name the signature spells out, what
/// to call it in the reader's language, and one line saying what goes there. The name is the piece
/// of code being written, so it stays as it is; the other two are text and are translated.
/// </summary>
public sealed record ExpressionParameter(string Name, string Label, string Hint,
    ExpressionParameterKind Kind = ExpressionParameterKind.Any,
    IReadOnlyList<string>? Options = null)
{
    /// <summary>The words a choice argument may hold, or nothing when it is not a choice.</summary>
    public IReadOnlyList<string> Choices => Options ?? [];
}

/// <summary>
/// What goes in one argument: any value, a line of text, a number, true or false, or one of a few
/// words. The editor draws each of them with the box it deserves instead of one text line for all,
/// which is what makes a formula readable as the thing it is.
/// </summary>
public enum ExpressionParameterKind
{
    Any,
    Text,
    Number,
    Flag,
    Choice,
}

/// <summary>One entry of the built-in function table.</summary>
internal sealed record FunctionEntry(
    string Group,
    string Name,
    string Signature,
    string Description,
    int MinArguments,
    int MaxArguments,
    Func<IReadOnlyList<Func<Value>>, Value> Body);

/// <summary>
/// What each common argument name means, written once. Most arguments that go by the same name
/// mean the same thing wherever they turn up — the text of <c>upper</c> is the text of
/// <c>trim</c> — so the sentences are kept here rather than repeated sixty-five times over. The
/// few places where a name means something narrower say so for themselves, in
/// <see cref="FunctionLibrary"/>.
/// </summary>
internal static class ArgumentLibrary
{
    private static readonly Dictionary<string, (string Label, string Hint)> Words =
        new(StringComparer.Ordinal)
        {
            ["text"] = ("Text", "The text to work on."),
            ["value"] = ("Value", "The value to work on."),
            ["values"] = ("Values", "The values to work on."),
            ["length"] = ("Length", "How many to take; leave it out to take the rest."),
            ["start"] = ("Start", "Which one to start at; 0 is the first."),
            ["old"] = ("Old", "The piece of text to replace."),
            ["new"] = ("New", "What to put in its place."),
            ["pattern"] = ("Pattern", "The regular expression to look for."),
            ["replacement"] = ("Replacement",
                "What to put in place of what matched; $1, $2 and so on name the pieces it captured."),
            ["separator"] = ("Separator", "The piece of text that sits between the items."),
            ["list"] = ("List", "The list to work on."),
            ["needle"] = ("Needle", "The piece of text, or the list item, to look for."),
            ["prefix"] = ("Prefix", "The piece of text the text has to begin with."),
            ["suffix"] = ("Suffix", "The piece of text the text has to end with."),
            ["times"] = ("Times", "How many times to write it; 0 or less writes nothing."),
            ["template"] = ("Template", "The text with {0}, {1} and so on where the arguments go."),
            ["number"] = ("Number", "The number to work on."),
            ["digits"] = ("Digits",
                "How many decimal places to keep; leave it out to round to a whole number."),
            ["power"] = ("Power", "The power to raise it to."),
            ["min"] = ("Lowest", "The lowest number to pick from."),
            ["max"] = ("Highest", "The highest number to pick from."),
            ["index"] = ("Index", "Where the item sits; 0 is the first."),
            ["condition"] = ("Condition", "What to ask; anything false or empty counts as no."),
            ["whenTrue"] = ("When true", "The value to answer with when the condition holds."),
            ["whenFalse"] = ("When false", "The value to answer with when it does not."),
            ["date"] = ("Date", "The date to work on; leave it out and now is read."),
            ["amount"] = ("Amount", "How far to move it; a negative number moves it back."),
            ["unit"] = ("Unit",
                "The unit of time: seconds, minutes, hours, days, weeks, months or years."),
            ["from"] = ("From", "The date to count from."),
            ["to"] = ("To", "The date to count to."),
            ["json"] = ("JSON", "The JSON text to work on."),
            ["path"] = ("Path", "Where to look, written as a.b[0].c."),
            ["name"] = ("Name", "The name to look for."),
            ["field"] = ("Field", "The name of the field to read from each record, such as x or name."),
            ["compare"] = ("Compare",
                "Contains, startsWith, endsWith, equals or regex, or a number comparison: >, >=, <, <=, = or !=."),
            ["caseSensitive"] = ("Case sensitive",
                "True to compare capitals and small letters as different; false, the default, ignores case."),
            ["trim"] = ("Trim ends",
                "True to drop the spaces at both ends before comparing; leave it out to trim only for equals."),
            ["comparison"] = ("Comparison",
                "Contains, startsWith, endsWith, equals or regex, or a number comparison: >, >=, <, <=, = or !=."),
            ["needle"] = ("Wanted", "The text or number to compare against."),
            ["multiline"] = ("Regex by line",
                "True to let ^ and $ stand for the start and end of every line."),
            ["record"] = ("Record", "The record to read a field from."),
        };

    /// <summary>What this argument is called, in the language the engine writes its text in.</summary>
    public static string Label(string name)
        => Words.TryGetValue(name, out var word) ? word.Label : name;

    /// <summary>One line saying what goes in it.</summary>
    public static string Hint(string name)
        => Words.TryGetValue(name, out var word) ? word.Hint : string.Empty;
}

/// <summary>
/// The functions an expression can call, kept in one table so the evaluator and the
/// list the editor suggests can never drift apart.
/// </summary>
internal static class FunctionLibrary
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
        Fn(MathsGroup, "min", "min(number, ...)", "The smallest of the arguments.", 1, Any,
            args => Value.FromNumber(args.Select(argument => argument().AsNumber()).Min())),
        Fn(MathsGroup, "max", "max(number, ...)", "The largest of the arguments.", 1, Any,
            args => Value.FromNumber(args.Select(argument => argument().AsNumber()).Max())),
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
            args => Value.FromText(Stamp(DateTimeOffset.Now))),
        Fn(DatesGroup, "today", "today()", "Today's date, as yyyy-MM-dd.", 0, 0,
            args => Value.FromText(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
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
            args => Value.FromText(Stamp(Shifted(args)))),
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
            args => Value.FromText(Parsed(args))),

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

    private static readonly Dictionary<string, FunctionEntry> ByName =
        Entries.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The few arguments whose own sentence says more than the shared one for their name does,
    /// keyed by "function.argument". Everything else about an argument — what it is called in the
    /// reader's language, and the line saying what goes in it — comes from the shared table.
    /// </summary>
    private static readonly Dictionary<string, string> OwnHints = new(StringComparer.Ordinal)
    {
        ["replaceRegex.pattern"] = "The regular expression to swap out.",
        ["insertAt.value"] = "The value to insert.",
        ["jsonSet.value"] = "The value to set it to.",
        ["jsonOf.name"] = "The name of the field to write.",
        ["jsonOf.value"] = "The value to write in that field.",
        ["field.name"] = "The name of the field to read, such as x.",
    };

    /// <summary>The words a comparison argument may hold: the text ways and the number ways.</summary>
    private static readonly string[] ComparisonWays =
        ["contains", "startsWith", "endsWith", "equals", "regex", ">", ">=", "<", "<=", "=", "!="];

    /// <summary>
    /// Which arguments are not free text. The name says what the argument is for and the engine reads
    /// the same names everywhere, so the shape is read off it rather than written out per function.
    /// </summary>
    private static (ExpressionParameterKind Kind, IReadOnlyList<string> Choices) ShapeOf(string name)
        => name switch
        {
            "caseSensitive" or "trim" or "multiline" or "descending" =>
                (ExpressionParameterKind.Flag, []),
            "comparison" => (ExpressionParameterKind.Choice, ComparisonWays),
            _ => (ExpressionParameterKind.Any, []),
        };

    /// <summary>Names and signatures the expression editor offers as suggestions.</summary>
    public static IReadOnlyList<ExpressionFunction> Metadata { get; } =
        [.. Entries.Select(entry =>
            new ExpressionFunction(entry.Name, entry.Signature, entry.Description, entry.Group,
                [.. ArgumentNames(entry.Signature).Select(name =>
                {
                    var (kind, choices) = ShapeOf(name);
                    return new ExpressionParameter(name, ArgumentLibrary.Label(name),
                        HintOf(entry.Name, name), kind, choices);
                })],
                entry.MinArguments,
                entry.MaxArguments))];

    /// <summary>
    /// The arguments a signature spells out, in the order they are written. The signature is what
    /// the editor shows and the engine checks, so the names come from there rather than from a
    /// second list that could drift away from it.
    /// </summary>
    private static IReadOnlyList<string> ArgumentNames(string signature)
    {
        var open = signature.IndexOf('(');
        var close = signature.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var part in signature[(open + 1)..close].Split(','))
        {
            // The brackets say an argument may be left out and the dots say more of the same may
            // follow; neither is part of the name.
            var name = part.Replace("[", string.Empty, StringComparison.Ordinal)
                .Replace("]", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>What goes in an argument: its own line where it has one, the shared one otherwise.</summary>
    private static string HintOf(string function, string name)
        => OwnHints.TryGetValue($"{function}.{name}", out var own)
            ? own
            : ArgumentLibrary.Hint(name);

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

    private static FunctionEntry Fn(string group, string name, string signature, string description,
        int min, int max, Func<IReadOnlyList<Func<Value>>, Value> body)
        => new(group, name, signature, description, min, max, body);

    private static Value V(IReadOnlyList<Func<Value>> args, int index) => args[index]();

    private static string S(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsText();

    private static double N(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsNumber();

    private static IReadOnlyList<Value> L(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsList();

    private static List<Value> Copy(IReadOnlyList<Func<Value>> args, int index) => [.. args[index]().AsList()];

    private static double Length(Value value) => value.Kind switch
    {
        ValueKind.List => value.Items.Count,
        ValueKind.Record => value.Fields.Count,
        ValueKind.Null => 0,
        _ => value.AsText().Length,
    };

    /// <summary>
    /// One field of a record. A name the record does not have, or a value that is not a record at
    /// all, is a mistake worth saying out loud — and the message lists the fields the record does
    /// have, because those names come from the action that made it and nobody remembers them all.
    /// </summary>
    private static Value Field(IReadOnlyList<Func<Value>> args)
    {
        var record = V(args, 0);
        if (record.Kind is not ValueKind.Record)
        {
            throw ExpressionException.TypeMismatch($"\"{record.AsText()}\" is not a record");
        }

        var name = S(args, 1).Trim();
        if (record.Field(name) is { } found)
        {
            return found;
        }

        var names = string.Join(", ", record.Fields.Select(field => field.Name));
        throw ExpressionException.TypeMismatch(names.Length == 0
            ? $"a record has no field \"{name}\"; it has no fields at all"
            : $"a record has no field \"{name}\"; it has {names}");
    }

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

    private static Value Compare(IReadOnlyList<Func<Value>> args)
    {
        var comparison = S(args, 1).Trim();
        var wanted = V(args, 2);
        var caseSensitive = args.Count > 3 && V(args, 3).AsBool();
        var trim = args.Count > 4 ? V(args, 4).AsBool() : (bool?)null;
        var multiline = args.Count > 5 && V(args, 5).AsBool();

        if (IsNumberWay(comparison))
        {
            if (!TryNumber(V(args, 0), out var value) || !TryNumber(wanted, out var number))
            {
                throw ExpressionException.TypeMismatch(
                    $"\"{comparison}\" compares numbers");
            }

            return Value.FromBool(NumberMatches(value, number, comparison));
        }

        var way = TextWay(comparison);
        var options = new TextCompareOptions(caseSensitive, trim, multiline);
        try
        {
            return Value.FromBool(TextCompare.Matches(V(args, 0).AsText(), wanted.AsText(), way,
                options));
        }
        catch (ArgumentException failure)
        {
            throw ExpressionException.TypeMismatch(failure.Message);
        }
    }

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
        var match = RegexGuard.Match(S(args, 0), S(args, 1));
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

    /// <summary>
    /// Keeps the records whose named field answers one comparison. Text is held against the same
    /// five words every other comparison uses; numbers are held against the six order words. A row
    /// that cannot answer — it is not a record, has no such field, or holds text where a number
    /// was asked for — is left out and reported rather than read as zero.
    /// </summary>
    private static Value Where(IReadOnlyList<Func<Value>> args)
    {
        var rows = L(args, 0);
        var field = S(args, 1).Trim();
        if (field.Length == 0)
        {
            throw ExpressionException.TypeMismatch("where needs the name of a field to compare");
        }

        var compare = S(args, 2).Trim();
        var wanted = V(args, 3);
        var numeric = IsNumberWay(compare);
        var options = new TextCompareOptions(
            args.Count > 4 && V(args, 4).AsBool(),
            args.Count > 5 ? V(args, 5).AsBool() : null);

        double wantedNumber = 0;
        string way = compare;
        if (numeric)
        {
            wantedNumber = AskedForNumber(wanted, compare);
        }
        else
        {
            way = TextWay(compare);
            try
            {
                TextCompare.Check(wanted.AsText(), way, options);
            }
            catch (ArgumentException failure)
            {
                throw ExpressionException.TypeMismatch(failure.Message);
            }
        }

        var kept = new List<Value>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!row.IsRecord)
            {
                Expression.Report(LogKeys.ListRowNotRecord, index + 1);
                continue;
            }

            if (row.Field(field) is not { } value)
            {
                Expression.Report(LogKeys.ListFieldMissing, index + 1, field);
                continue;
            }

            if (numeric)
            {
                if (!TryNumber(value, out var number))
                {
                    Expression.Report(LogKeys.ListFieldNotNumber, index + 1, field, value.AsText());
                    continue;
                }

                if (NumberMatches(number, wantedNumber, compare))
                {
                    kept.Add(row);
                }

                continue;
            }

            if (TextCompare.Matches(value.AsText(), wanted.AsText(), way, options))
            {
                kept.Add(row);
            }
        }

        return Value.FromList(kept);
    }

    /// <summary>
    /// One value per record from the same field. Rows without the field are left out and reported;
    /// a list in which no record has it says which fields the records do have, because those names
    /// come from the action that made the records.
    /// </summary>
    private static Value Column(IReadOnlyList<Func<Value>> args)
    {
        var rows = L(args, 0);
        var field = S(args, 1).Trim();
        if (field.Length == 0)
        {
            throw ExpressionException.TypeMismatch("column needs the name of a field to read");
        }

        var values = new List<Value>();
        var found = false;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!row.IsRecord)
            {
                Expression.Report(LogKeys.ListRowNotRecord, index + 1);
                continue;
            }

            if (row.Field(field) is not { } value)
            {
                Expression.Report(LogKeys.ListFieldMissing, index + 1, field);
                continue;
            }

            found = true;
            values.Add(value);
        }

        if (found || rows.Count == 0)
        {
            return Value.FromList(values);
        }

        var names = rows.FirstOrDefault(row => row.IsRecord)?.Fields
            .Select(one => one.Name)
            .ToList() ?? [];
        throw ExpressionException.TypeMismatch(names.Count == 0
            ? $"no record in the list has a field \"{field}\"; the records have no fields"
            : $"no record in the list has a field \"{field}\"; they have {string.Join(", ", names)}");
    }

    private static bool IsNumberWay(string compare)
        => compare is ">" or ">=" or "<" or "<=" or "=" or "!=";

    private static bool NumberMatches(double value, double wanted, string compare) => compare switch
    {
        ">" => value > wanted,
        ">=" => value >= wanted,
        "<" => value < wanted,
        "<=" => value <= wanted,
        "=" => value == wanted,
        _ => value != wanted,
    };

    private static double AskedForNumber(Value value, string compare)
    {
        if (TryNumber(value, out var number))
        {
            return number;
        }

        throw ExpressionException.TypeMismatch(
            $"\"{compare}\" compares numbers, and \"{value.AsText()}\" is not one");
    }

    /// <summary>
    /// A number, or text a person would read as one. A CSV cell is text even when it says 42:
    /// refusing it here would make a numeric where over a table read from a file skip every row.
    /// Text that does not read as a number is still left out and reported.
    /// </summary>
    private static bool TryNumber(Value value, out double number)
    {
        if (value.IsNumber)
        {
            number = value.Number;
            return true;
        }

        if (value.Kind is ValueKind.Bool)
        {
            number = value.Flag ? 1 : 0;
            return true;
        }

        if (value.Kind is ValueKind.Text
            && double.TryParse(value.Text, NumberStyles.Any, CultureInfo.InvariantCulture,
                out var parsed))
        {
            number = parsed;
            return true;
        }

        number = 0;
        return false;
    }

    private static string TextWay(string compare)
    {
        try
        {
            return TextCompare.Read(compare);
        }
        catch (ArgumentException)
        {
            throw ExpressionException.TypeMismatch(
                $"\"{compare}\" is not a comparison; use {string.Join(", ", TextCompare.Values)} "
                + "or >, >=, <, <=, =, !=");
        }
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
        return found ? JsonValues.FromJson(node) : Value.Null;
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

            made[name] = JsonValues.Node(args[index + 1]());
        }

        return made.ToJsonString();
    }

    private static string JsonSet(IReadOnlyList<Func<Value>> args)
    {
        var steps = Path(S(args, 1));
        var value = JsonValues.Node(V(args, 2));
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
