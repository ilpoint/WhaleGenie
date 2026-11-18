using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// The one place that decides what a value of a kind may hold, what an empty one starts at, whether
/// two values mean the same thing, and how a value is written down for a comparison. The engine, the
/// value tables, the saved globals and the design-time checks all ask here, so "is this a number"
/// cannot mean one thing at a write and another at a read.
/// </summary>
public static class ValueRules
{
    /// <summary>
    /// The one grammar a line is read as a number by. No thousands separators: a comma separates the
    /// items of a list in this language, so "1,000" is two pieces of writing rather than a number
    /// with a decoration in it. Exponent and sign are allowed, because a number may be written small.
    /// </summary>
    public static bool TryNumber(string? text, out double number)
        => double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out number);

    /// <summary>
    /// True when the value can stand for the kind. Empty is allowed for every kind: it means "this
    /// attempt did not supply a value", and a result that was never written is empty rather than
    /// wrong. Text accepts anything because every value has a text reading; a picture does not,
    /// because a line that reads like a handle is not one.
    /// </summary>
    public static bool Accepts(ValueKind kind, Value value)
    {
        if (value.IsNull || (value.Kind is ValueKind.Text && value.Text.Length == 0))
        {
            return true;
        }

        return kind switch
        {
            ValueKind.Text => true,
            ValueKind.Image => value.IsImage,
            ValueKind.Number => value.IsNumber || TryNumber(value.AsText(), out _),
            ValueKind.Bool => value.Kind is ValueKind.Bool
                || bool.TryParse(value.AsText(), out _)
                || value.AsText() is "0" or "1",
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
    /// nothing at all, so a number that was never filled in adds up instead of failing.
    /// </summary>
    public static Value Empty(ValueKind kind) => kind switch
    {
        ValueKind.Number => Value.FromNumber(0),
        ValueKind.Bool => Value.FromBool(false),
        ValueKind.List => Value.FromList([]),
        ValueKind.Image => Value.FromImage(string.Empty),
        _ => Value.FromText(string.Empty),
    };

    /// <summary>
    /// Reads a line as the kind, using the same grammar <see cref="Accepts"/> uses. A kind that is
    /// not written down as text — a list, a record, a picture — cannot be read this way.
    /// </summary>
    public static bool TryParse(string text, ValueKind kind, out Value value)
    {
        switch (kind)
        {
            case ValueKind.Number:
                if (TryNumber(text, out var number))
                {
                    value = Value.FromNumber(number);
                    return true;
                }

                break;
            case ValueKind.Bool:
                if (bool.TryParse(text, out var flag))
                {
                    value = Value.FromBool(flag);
                    return true;
                }

                if (text is "0" or "1")
                {
                    value = Value.FromBool(text == "1");
                    return true;
                }

                break;
            case ValueKind.Date:
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var moment))
                {
                    value = Value.FromDate(moment);
                    return true;
                }

                break;
            case ValueKind.Text:
                value = Value.FromText(text);
                return true;
        }

        value = Value.Null;
        return false;
    }

    /// <summary>
    /// Whether two values mean the same thing. The same kind compares as that kind; a line beside a
    /// number, a flag or a date is read as that kind first, which is what makes <c>1 == "1.0"</c>
    /// and <c>today() == "2026-11-12"</c> read the way they look. A line that cannot be read that
    /// way is not equal to it, and two lines are compared as the lines they are.
    /// </summary>
    public static bool Same(Value left, Value right)
    {
        if (left.Kind == right.Kind)
        {
            return left.Equals(right);
        }

        if (left.Kind is ValueKind.Text)
        {
            return TryParse(left.Text, right.Kind, out var parsed) && parsed.Equals(right);
        }

        if (right.Kind is ValueKind.Text)
        {
            return TryParse(right.Text, left.Kind, out var parsed) && parsed.Equals(left);
        }

        return false;
    }

    /// <summary>
    /// A value written as the one line that stands for it in a comparison: the same idea as
    /// <see cref="Same"/>, made a string so a set can look it up. A line that reads as a number or a
    /// flag takes that form, which is how <c>1</c> and <c>"1"</c> land on the same line.
    /// </summary>
    public static string Canonical(Value value)
    {
        switch (value.Kind)
        {
            case ValueKind.Null:
                return "z";
            case ValueKind.Number:
                return "n:" + value.Number.ToString("R", CultureInfo.InvariantCulture);
            case ValueKind.Bool:
                return value.Flag ? "b:1" : "b:0";
            case ValueKind.Date:
                return "d:" + value.Date.UtcTicks.ToString(CultureInfo.InvariantCulture);
            case ValueKind.Image:
                return "i:" + value.Text.ToLowerInvariant();
            case ValueKind.Text:
                {
                    var text = value.Text;
                    if (TryNumber(text, out var number))
                    {
                        return "n:" + number.ToString("R", CultureInfo.InvariantCulture);
                    }

                    if (bool.TryParse(text, out var flag))
                    {
                        return flag ? "b:1" : "b:0";
                    }

                    if (text is "0" or "1")
                    {
                        return text == "1" ? "b:1" : "b:0";
                    }

                    return "t:" + text.ToLowerInvariant();
                }
            case ValueKind.List:
                return "l:[" + string.Join(",", value.Items.Select(Canonical)) + "]";
            default:
                return "r:{" + string.Join(",", value.Fields.Select(field =>
                    field.Name + "=" + Canonical(field.Value))) + "}";
        }
    }
}
