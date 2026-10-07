using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WhaleGenie.Core.Expressions;

/// <summary>What kind of data a <see cref="Value"/> holds.</summary>
public enum ValueKind
{
    Null,
    Number,
    Bool,
    Text,
    List,
}

/// <summary>
/// One value an expression works with. Everything is one of a handful of kinds so the
/// engine stays small; a list holds values, which is what list variables need.
/// </summary>
public sealed class Value : IEquatable<Value>
{
    private readonly double _number;
    private readonly bool _flag;
    private readonly string _text;
    private readonly List<Value> _items;

    private Value(ValueKind kind, double number, bool flag, string text, List<Value>? items)
    {
        Kind = kind;
        _number = number;
        _flag = flag;
        _text = text;
        _items = items ?? [];
    }

    /// <summary>The empty value, used for a variable nothing has set yet.</summary>
    public static Value Null { get; } = new(ValueKind.Null, 0, false, string.Empty, null);

    public ValueKind Kind { get; }

    public static Value FromNumber(double number) => new(ValueKind.Number, number, false, string.Empty, null);

    public static Value FromBool(bool flag) => new(ValueKind.Bool, 0, flag, string.Empty, null);

    public static Value FromText(string? text) => new(ValueKind.Text, 0, false, text ?? string.Empty, null);

    public static Value FromList(IEnumerable<Value> items) => new(ValueKind.List, 0, false, string.Empty, [.. items]);

    public bool IsNull => Kind is ValueKind.Null;

    public bool IsList => Kind is ValueKind.List;

    public bool IsNumber => Kind is ValueKind.Number;

    /// <summary>The raw number when <see cref="Kind"/> is <see cref="ValueKind.Number"/>.</summary>
    public double Number => _number;

    /// <summary>The raw flag when <see cref="Kind"/> is <see cref="ValueKind.Bool"/>.</summary>
    public bool Flag => _flag;

    /// <summary>The raw text when <see cref="Kind"/> is <see cref="ValueKind.Text"/>.</summary>
    public string Text => _text;

    /// <summary>Items of a list, or an empty sequence for the other kinds.</summary>
    public IReadOnlyList<Value> Items => _items;

    /// <summary>
    /// Reads the value as a list: a list gives its items, the empty value gives nothing
    /// and anything else counts as a single item, so <c>append(1, 2)</c> still works.
    /// </summary>
    public IReadOnlyList<Value> AsList() => Kind switch
    {
        ValueKind.List => _items,
        ValueKind.Null => [],
        _ => [this],
    };

    /// <summary>Reads the value as a number, converting text and booleans when it can.</summary>
    public double AsNumber()
    {
        switch (Kind)
        {
            case ValueKind.Number:
                return _number;
            case ValueKind.Bool:
                return _flag ? 1 : 0;
            case ValueKind.Text:
                if (double.TryParse(_text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                throw ExpressionException.TypeMismatch($"\"{_text}\" is not a number");
            case ValueKind.List:
                throw ExpressionException.TypeMismatch("a list is not a number");
            default:
                return 0;
        }
    }

    /// <summary>Reads the value as a flag. Empty text, 0 and "false" are false.</summary>
    public bool AsBool() => Kind switch
    {
        ValueKind.Bool => _flag,
        ValueKind.Number => _number != 0,
        ValueKind.Text => _text.Length > 0
            && !string.Equals(_text, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_text, "0", StringComparison.Ordinal),
        ValueKind.List => _items.Count > 0,
        _ => false,
    };

    /// <summary>The value written out the way a user would read it.</summary>
    public string AsText() => Kind switch
    {
        ValueKind.Number => FormatNumber(_number),
        ValueKind.Bool => _flag ? "true" : "false",
        ValueKind.Text => _text,
        ValueKind.List => string.Join(", ", _items.Select(item => item.AsText())),
        _ => string.Empty,
    };

    /// <summary>Writes a number without a trailing ".0" and without scientific notation.</summary>
    public static string FormatNumber(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return number == Math.Floor(number) && Math.Abs(number) < 1e15
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>Numeric comparison, so 1 equals 1.0 and "1" equals 1.</summary>
    public bool NumericEquals(Value other)
    {
        try
        {
            return AsNumber().Equals(other.AsNumber());
        }
        catch (ExpressionException)
        {
            return string.Equals(AsText(), other.AsText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool Equals(Value? other)
    {
        if (other is null)
        {
            return false;
        }

        if (Kind is ValueKind.List || other.Kind is ValueKind.List)
        {
            return Kind == other.Kind && _items.SequenceEqual(other._items);
        }

        if (Kind is ValueKind.Number && other.Kind is ValueKind.Number)
        {
            return _number.Equals(other._number);
        }

        if (Kind is ValueKind.Bool && other.Kind is ValueKind.Bool)
        {
            return _flag == other._flag;
        }

        if (Kind is ValueKind.Null && other.Kind is ValueKind.Null)
        {
            return true;
        }

        return string.Equals(AsText(), other.AsText(), StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as Value);

    public override int GetHashCode() => AsText().GetHashCode(StringComparison.OrdinalIgnoreCase);

    public override string ToString() => AsText();
}
