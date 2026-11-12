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
    /// rather than wrong. Text accepts anything because every value has a text reading; a picture
    /// is the one kind that is not also text, because a line that reads like a handle is not one.
    /// </summary>
    public static bool Fits(ValueKind declared, Value value)
    {
        if (value.IsNull)
        {
            return true;
        }

        if (value.Kind is ValueKind.Text && value.Text.Length == 0)
        {
            return true;
        }

        return declared switch
        {
            ValueKind.Text => true,
            ValueKind.Image => value.IsImage,
            ValueKind.Number => value.IsNumber
                || double.TryParse(value.AsText(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _),
            ValueKind.Bool => value.Kind is ValueKind.Bool
                || bool.TryParse(value.AsText(), out _),
            ValueKind.List => value.IsList,
            ValueKind.Record => value.IsRecord,
            ValueKind.Date => value.IsDate
                || DateTimeOffset.TryParse(value.AsText(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out _),
            _ => false,
        };
    }

    /// <summary>
    /// The value a name of this kind holds when nothing was written for it: zero, false, an empty
    /// list, an empty line. A declaration that leaves its value empty starts at that rather than at
    /// nothing at all, so a number that was never filled in adds up instead of failing. One place
    /// decides it for the run and for the editor's preview.
    /// </summary>
    public static Value Empty(ValueKind kind) => kind switch
    {
        ValueKind.Number => Value.FromNumber(0),
        ValueKind.Bool => Value.FromBool(false),
        ValueKind.List => Value.FromList([]),
        ValueKind.Image => Value.FromImage(string.Empty),
        _ => Value.FromText(string.Empty),
    };
}
