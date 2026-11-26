using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// Reads the items of a list value. A field holds a shape: a list is a list of pieces, and a line
/// of prose is split on the separators people use when they write one out. Nothing here reads a
/// line as a formula — a shape that is worked out is worked out as it stands.
/// </summary>
public static class ListText
{
    /// <summary>Separators accepted between the items of a plain line, in either alphabet.</summary>
    private static readonly char[] Separators = [',', ';', '，', '；'];

    /// <summary>
    /// Reads the items a shape holds. Returns <c>false</c> with a reason when the shape is worked
    /// out and does not come to a list.
    /// </summary>
    public static bool TryRead(ExpressionNode written, IVariableResolver? variables,
        out IReadOnlyList<Value> items, out ExpressionException? error)
        => TryRead(written, variables, EvaluationContext.System, out items, out error);

    /// <summary>The same read, against the clock and dice the caller hands in.</summary>
    public static bool TryRead(ExpressionNode written, IVariableResolver? variables,
        EvaluationContext evaluation, out IReadOnlyList<Value> items, out ExpressionException? error)
    {
        error = null;
        if (Formula.IsNothing(written))
        {
            items = [];
            return true;
        }

        if (written is TextNode { Text: var text })
        {
            items = Split(text);
            return true;
        }

        try
        {
            var value = Expression.Evaluate(written, variables, evaluation);
            if (!value.IsList)
            {
                error = ExpressionException.TypeMismatch($"\"{value.AsText()}\" is not a list");
                items = [];
                return false;
            }

            items = value.Items;
            return true;
        }
        catch (ExpressionException failure)
        {
            error = failure;
            items = [];
            return false;
        }
    }

    /// <summary>A line of prose read as the items someone wrote out, one separator at a time.</summary>
    public static IReadOnlyList<Value> Split(string? text)
        => [.. (text ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => Value.FromText(item))];

    /// <summary>Writes the items the way a summary line shows them.</summary>
    public static string Summarize(IReadOnlyList<Value> items)
        => string.Join(", ", items.Select(item => item.AsText()));
}
