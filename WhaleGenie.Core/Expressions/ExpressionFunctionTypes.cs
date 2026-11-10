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

// What a function looks like to the editor, what goes in one argument, and the sentences
// shared by arguments of the same name.
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
            ["compare.needle"] = ("Wanted",
                "The text or number to compare against. With the regex way, this is the pattern."),
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
