using System.Globalization;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Whether one value fits the kind a macro declaration asked for. It is shared by the call and the
/// direct run, and by the design-time checks that judge a constant argument or default, so the word
/// "fits" cannot mean one thing at a call and another while the macro is being written.
/// </summary>
public static class MacroValues
{
    /// <summary>
    /// True when the value can stand for the declared kind. Empty is allowed for every kind: it
    /// means "this attempt did not supply a value", and a result that was never written is empty
    /// rather than wrong. Text and image accept anything because every value has a text reading.
    /// </summary>
    public static bool Fits(ActionReturnType declared, Value value)
    {
        if (value.IsNull)
        {
            return true;
        }

        if (value.Kind is ValueKind.Text && value.Text.Length == 0)
        {
            return true;
        }

        if (declared is ActionReturnType.Text or ActionReturnType.Image)
        {
            return true;
        }

        return declared switch
        {
            ActionReturnType.Number => value.IsNumber
                || double.TryParse(value.AsText(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _),
            ActionReturnType.Flag => value.Kind is ValueKind.Bool
                || bool.TryParse(value.AsText(), out _),
            ActionReturnType.List => value.IsList,
            ActionReturnType.Record => value.IsRecord,
            _ => false,
        };
    }
}
