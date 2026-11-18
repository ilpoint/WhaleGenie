using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// Reads the text a user types for a list variable. Text that looks like an expression
/// (<c>[1, 2, 3]</c>, <c>split($text, ",")</c>) is evaluated and has to produce a list;
/// anything else is read as items separated by commas.
/// </summary>
public static class ListText
{
    /// <summary>Separators accepted between the items of a plain list, in either alphabet.</summary>
    private static readonly char[] Separators = [',', ';', '，', '；'];

    /// <summary>
    /// Reads items out of list text. Returns <c>false</c> with a reason when the text was
    /// written as an expression but does not produce a list.
    /// </summary>
    public static bool TryParse(string? text, out IReadOnlyList<Value> items, out ExpressionException? error)
        => TryParse(text, null, out items, out error);

    /// <summary>
    /// The same read, with the variables an expression inside the text may use.
    /// </summary>
    public static bool TryParse(string? text, IVariableResolver? variables,
        out IReadOnlyList<Value> items, out ExpressionException? error)
    {
        error = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            items = [];
            return true;
        }

        if (!LooksLikeExpression(trimmed))
        {
            items = SplitOnSeparators(trimmed);
            return true;
        }

        if (!Expression.TryEvaluate(trimmed, variables, out var value, out error))
        {
            items = [];
            return false;
        }

        if (!value.IsList)
        {
            error = ExpressionException.TypeMismatch($"\"{trimmed}\" is not a list");
            items = [];
            return false;
        }

        items = value.Items;
        return true;
    }

    /// <summary>Writes the items the way a summary line shows them.</summary>
    public static string Summarize(IReadOnlyList<Value> items)
        => string.Join(", ", items.Select(item => item.AsText()));

    /// <summary>True when the text is meant as an expression rather than a plain list.</summary>
    private static bool LooksLikeExpression(string text)
        => text[0] is '[' or '$' or '(' || text.Contains('(');

    private static IReadOnlyList<Value> SplitOnSeparators(string text)
        => [.. text
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => Value.FromText(item))];
}
