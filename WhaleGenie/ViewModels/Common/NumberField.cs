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
    /// <summary>True when the text is a plain number, which is what the numbered box holds.</summary>
    public static bool IsNumberText(string? text)
        => decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// The same check for a field that already keeps a shape. Nothing is written out to be read
    /// back: the tree is worked out as it stands, and the line under the box says the same thing it
    /// would say for the line that tree came from.
    /// </summary>
    public static (string Message, bool Failed) Check(ExpressionNode written,
        IReadOnlyList<string> known)
    {
        if (Formula.IsNothing(written))
        {
            return (string.Empty, false);
        }

        if (!Expression.TryEvaluate(written, new KnownVariables(known), out var value, out var error))
        {
            return Certain(error!.Code)
                ? (ExpressionText.Describe(error), true)
                : (Strings.Get("Add.ExpressionWhenRun"), false);
        }

        return Expression.References(written).Count > 0
            ? (Strings.Get("Add.ExpressionValid"), false)
            : (Strings.Format("Add.ExpressionResult", value.AsText()), false);
    }

    /// <summary>
    /// Whether a failure is one the macro can be blamed for here and now: the text does not read,
    /// or names something that is not there — the names in scope and the functions of the language
    /// are both known while the macro is being written. What a value turns out to hold is not, so
    /// that half is left to the run.
    /// </summary>
    private static bool Certain(ExpressionErrorCode code) => code is
        ExpressionErrorCode.Syntax
        or ExpressionErrorCode.UnknownVariable
        or ExpressionErrorCode.UnknownFunction
        or ExpressionErrorCode.ArgumentCount;
}
