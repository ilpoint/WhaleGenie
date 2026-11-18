using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One way of comparing a value with another, and how that way is written into the macro. The
/// catalogue below is the whole vocabulary: a row of the condition table picks one, and it says
/// what the row means, what it needs beside it, and the expression text it stands for.
/// </summary>
/// <remarks>
/// The ways are grouped by what they ask about, and the groups are in the order a person reads
/// them: a number first, then text, then a list, then a value that is already true or false. The
/// engine's own comparison words — <c>contains</c>, <c>startsWith</c>, <c>endsWith</c>, <c>equals</c>,
/// <c>regex</c> and the number ones — are the same words this table is built on, which is what lets
/// a switch branch, a condition row and the <c>compare()</c> function mean one thing rather than
/// three.
/// </remarks>
public enum ConditionCompare
{
    Equal,

    NotEqual,

    Greater,

    Less,

    AtLeast,

    AtMost,

    /// <summary>The text, or the items of a list, holds what is on the right.</summary>
    Contains,

    /// <summary>Neither the text nor any item of the list holds what is on the right.</summary>
    NotContains,

    StartsWith,

    EndsWith,

    /// <summary>The text matches the regular expression on the right.</summary>
    Matches,

    /// <summary>Nothing is in it: a text of no characters, or a list of no items.</summary>
    Empty,

    NotEmpty,

    /// <summary>The list holds exactly as many items as the number on the right.</summary>
    CountIs,

    /// <summary>The value on the left is true on its own, with nothing to compare it to.</summary>
    Holds,

    /// <summary>The value on the left is false on its own.</summary>
    Fails,
}

/// <summary>What a way of comparing asks about, which is the order the dropdown offers them in.</summary>
public enum ComparisonFamily
{
    Number,
    Text,
    List,
    Truth,
}

/// <summary>
/// How one comparison is written. The shape is what both the writer and the reader work from, so a
/// row written as <c>contains($a, x)</c> reads back as the row it came from rather than as one
/// opaque left-hand side.
/// </summary>
public enum ComparisonShape
{
    /// <summary>An operator between the two sides: <c>$a == 1</c>.</summary>
    Operator,

    /// <summary>A function of both sides: <c>contains($a, x)</c>.</summary>
    Call,

    /// <summary>The same call, denied: <c>not contains($a, x)</c>.</summary>
    NegatedCall,

    /// <summary>Nothing is in the left side: <c>length($a) == 0</c>.</summary>
    NothingIn,

    /// <summary>Something is in the left side: <c>length($a) > 0</c>.</summary>
    SomethingIn,

    /// <summary>How many the left side holds: <c>count($a) == 3</c>.</summary>
    HowMany,

    /// <summary>The left side on its own: <c>$a</c>.</summary>
    Bare,

    /// <summary>The left side denied: <c>not $a</c>.</summary>
    NegatedBare,
}

/// <summary>One way of comparing, with the words it goes by and the text it writes.</summary>
public sealed class ConditionCompareChoice
{
    internal ConditionCompareChoice(ConditionCompare compare, ComparisonFamily family, string labelKey,
        ComparisonShape shape, string written, bool needsValue)
    {
        Compare = compare;
        Family = family;
        LabelKey = labelKey;
        Shape = shape;
        Written = written;
        NeedsValue = needsValue;
    }

    public ConditionCompare Compare { get; }

    /// <summary>What this way asks about, which is what groups it in the dropdown.</summary>
    public ComparisonFamily Family { get; }

    /// <summary>The message-table key of the words on screen, so a language change is picked up.</summary>
    public string LabelKey { get; }

    /// <summary>How it is written, which is what the writer and the reader share.</summary>
    public ComparisonShape Shape { get; }

    /// <summary>An example of the expression it writes, shown where the way is explained.</summary>
    public string Written { get; }

    /// <summary>True when this comparison needs something on the right of it.</summary>
    public bool NeedsValue { get; }

    public string Label => Strings.Get(LabelKey);

    public override string ToString() => Label;
}

/// <summary>
/// The whole vocabulary of comparisons, in one list. Nothing else in the program writes these words
/// out a second time: a condition row, a switch branch and the comparison argument of
/// <c>compare()</c> all answer to this order and these names.
/// </summary>
public static class Comparisons
{
    /// <summary>
    /// Every way of comparing, in the order the dropdowns offer them: the number ways first, then
    /// the ones that read text, then what a list is asked, then a value that is already true.
    /// </summary>
    public static IReadOnlyList<ConditionCompareChoice> All { get; } =
    [
        new(ConditionCompare.Equal, ComparisonFamily.Number, "Condition.Is",
            ComparisonShape.Operator, "$a == 1", true),
        new(ConditionCompare.NotEqual, ComparisonFamily.Number, "Condition.IsNot",
            ComparisonShape.Operator, "$a != 1", true),
        new(ConditionCompare.Greater, ComparisonFamily.Number, "Condition.Greater",
            ComparisonShape.Operator, "$a > 1", true),
        new(ConditionCompare.Less, ComparisonFamily.Number, "Condition.Less",
            ComparisonShape.Operator, "$a < 1", true),
        new(ConditionCompare.AtLeast, ComparisonFamily.Number, "Condition.AtLeast",
            ComparisonShape.Operator, "$a >= 1", true),
        new(ConditionCompare.AtMost, ComparisonFamily.Number, "Condition.AtMost",
            ComparisonShape.Operator, "$a <= 1", true),

        new(ConditionCompare.Contains, ComparisonFamily.Text, "Condition.Contains",
            ComparisonShape.Call, "contains($a, x)", true),
        new(ConditionCompare.NotContains, ComparisonFamily.Text, "Condition.NotContains",
            ComparisonShape.NegatedCall, "not contains($a, x)", true),
        new(ConditionCompare.StartsWith, ComparisonFamily.Text, "Condition.StartsWith",
            ComparisonShape.Call, "startsWith($a, x)", true),
        new(ConditionCompare.EndsWith, ComparisonFamily.Text, "Condition.EndsWith",
            ComparisonShape.Call, "endsWith($a, x)", true),
        new(ConditionCompare.Matches, ComparisonFamily.Text, "Condition.Matches",
            ComparisonShape.Call, "match($a, x)", true),
        new(ConditionCompare.Empty, ComparisonFamily.Text, "Condition.Empty",
            ComparisonShape.NothingIn, "length($a) == 0", false),
        new(ConditionCompare.NotEmpty, ComparisonFamily.Text, "Condition.NotEmpty",
            ComparisonShape.SomethingIn, "length($a) > 0", false),

        new(ConditionCompare.CountIs, ComparisonFamily.List, "Condition.CountIs",
            ComparisonShape.HowMany, "count($a) == 3", true),

        new(ConditionCompare.Holds, ComparisonFamily.Truth, "Condition.Holds",
            ComparisonShape.Bare, "$a", false),
        new(ConditionCompare.Fails, ComparisonFamily.Truth, "Condition.Fails",
            ComparisonShape.NegatedBare, "not $a", false),
    ];

    private static readonly Dictionary<ConditionCompare, ConditionCompareChoice> ByWord =
        All.ToDictionary(choice => choice.Compare);

    /// <summary>The words of one family, in the order they are offered.</summary>
    public static IEnumerable<ConditionCompareChoice> Family(ComparisonFamily family)
        => All.Where(choice => choice.Family == family);

    /// <summary>How one way of comparing is named and written.</summary>
    public static ConditionCompareChoice Of(ConditionCompare compare) => ByWord[compare];

    /// <summary>
    /// The function a call-shaped comparison is written with, or null for one written another way.
    /// It is also what the reader looks for when it turns an expression back into a row.
    /// </summary>
    internal static string? FunctionName(ConditionCompare compare) => compare switch
    {
        ConditionCompare.Contains or ConditionCompare.NotContains => "contains",
        ConditionCompare.StartsWith => "startsWith",
        ConditionCompare.EndsWith => "endsWith",
        ConditionCompare.Matches => "match",
        _ => null,
    };

    /// <summary>True for the ways written as an operator between two sides.</summary>
    internal static string? OperatorCode(ConditionCompare compare) => compare switch
    {
        ConditionCompare.Equal => "==",
        ConditionCompare.NotEqual => "!=",
        ConditionCompare.Greater => ">",
        ConditionCompare.Less => "<",
        ConditionCompare.AtLeast => ">=",
        ConditionCompare.AtMost => "<=",
        _ => null,
    };
}
