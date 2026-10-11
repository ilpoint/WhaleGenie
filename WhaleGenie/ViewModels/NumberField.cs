using System.Collections.Generic;
using System.Globalization;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// How a number written anywhere in the dialog is read: whether what is in the box is a plain
/// number or something to work out, and what an expression says about itself. The field of a step
/// and the four numbers of a place to look are the same kind of box, so they decide it in one
/// place rather than each keeping its own idea of what counts as a number.
/// </summary>
internal static class NumberField
{
    /// <summary>True when the text is a plain number, which is what the spinner shows.</summary>
    public static bool IsNumberText(string? text)
        => decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// What the line under a box written as an expression says: what it comes to, or why it cannot
    /// be read. Nothing is said about an empty one, because there is nothing to say about it.
    /// </summary>
    public static (string Message, bool Failed) Check(string? source,
        IReadOnlyList<string> known)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return (string.Empty, false);
        }

        if (!Expression.TryEvaluate(source, new KnownVariables(known), out var value, out var error))
        {
            return (ExpressionText.Describe(error!), true);
        }

        // Without variables the answer is exact, so it is worth showing. With variables
        // there is nothing to run yet, so the box only confirms that the text reads.
        return (source.Contains('$')
            ? Strings.Get("Add.ExpressionValid")
            : Strings.Format("Add.ExpressionResult", value.AsText()), false);
    }
}
