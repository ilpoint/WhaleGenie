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

    /// <summary>
    /// A moment: a day, or a day and a time. It is its own kind rather than text because two dates
    /// have an order before a comparison, and because "add a day" is not "add 1 to a line".
    /// </summary>
    Date,

    Text,
    List,

    /// <summary>
    /// Named fields in the order they were written. A hit found on the screen and one window out of
    /// a listing are records: several answers about one thing, which belong together.
    /// </summary>
    Record,

    /// <summary>
    /// A picture the run is holding: a capture, or what the clipboard had on it. The value is the
    /// handle the picture is known by rather than its pixels, which live with the run and are let go
    /// of when it ends. It is its own kind so a picture is not mistaken for the line that describes
    /// it, and so a step that wants a picture says so.
    /// </summary>
    Image,
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
    private readonly DateTimeOffset _date;
    private readonly List<Value> _items;
    private readonly List<(string Name, Value Value)> _fields;

    private Value(ValueKind kind, double number, bool flag, string text, List<Value>? items,
        List<(string Name, Value Value)>? fields = null, DateTimeOffset date = default)
    {
        Kind = kind;
        _number = number;
        _flag = flag;
        _text = text;
        _date = date;
        _items = items ?? [];
        _fields = fields ?? [];
    }

    /// <summary>The empty value, used for a variable nothing has set yet.</summary>
    public static Value Null { get; } = new(ValueKind.Null, 0, false, string.Empty, null);

    public ValueKind Kind { get; }

    public static Value FromNumber(double number) => new(ValueKind.Number, number, false, string.Empty, null);

    public static Value FromBool(bool flag) => new(ValueKind.Bool, 0, flag, string.Empty, null);

    /// <summary>A moment, kept with the offset it was read at so its own wall time survives.</summary>
    public static Value FromDate(DateTimeOffset date)
        => new(ValueKind.Date, 0, false, string.Empty, null, date: date);

    /// <summary>
    /// A picture, written as the handle it is known by. The handle is what a log line, a file of
    /// results and a comparison can carry; the pixels stay with the run that took them.
    /// </summary>
    public static Value FromImage(string handle)
        => new(ValueKind.Image, 0, false, handle ?? string.Empty, null);

    public static Value FromText(string? text) => new(ValueKind.Text, 0, false, text ?? string.Empty, null);

    public static Value FromList(IEnumerable<Value> items) => new(ValueKind.List, 0, false, string.Empty, [.. items]);

    /// <summary>
    /// A record out of fields written in turn. The order is kept as it was given, because that is
    /// the order a reader — and the text a record writes itself as — sees them.
    /// </summary>
    public static Value FromRecord(IEnumerable<(string Name, Value Value)> fields)
        => new(ValueKind.Record, 0, false, string.Empty, null, [.. fields]);

    public bool IsNull => Kind is ValueKind.Null;

    public bool IsList => Kind is ValueKind.List;

    public bool IsNumber => Kind is ValueKind.Number;

    public bool IsDate => Kind is ValueKind.Date;

    public bool IsRecord => Kind is ValueKind.Record;

    public bool IsImage => Kind is ValueKind.Image;

    /// <summary>The raw number when <see cref="Kind"/> is <see cref="ValueKind.Number"/>.</summary>
    public double Number => _number;

    /// <summary>The raw flag when <see cref="Kind"/> is <see cref="ValueKind.Bool"/>.</summary>
    public bool Flag => _flag;

    /// <summary>The moment when <see cref="Kind"/> is <see cref="ValueKind.Date"/>.</summary>
    public DateTimeOffset Date => _date;

    /// <summary>The raw text when <see cref="Kind"/> is <see cref="ValueKind.Text"/>.</summary>
    public string Text => _text;

    /// <summary>Items of a list, or an empty sequence for the other kinds.</summary>
    public IReadOnlyList<Value> Items => _items;

    /// <summary>The fields of a record, or an empty sequence for the other kinds.</summary>
    public IReadOnlyList<(string Name, Value Value)> Fields => _fields;

    /// <summary>True when a record has a field by that name; names are read without caring about case.</summary>
    public bool HasField(string name)
        => _fields.Any(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>One field of a record, or nothing when it has none by that name.</summary>
    public Value? Field(string name)
    {
        foreach (var (field, value) in _fields)
        {
            if (string.Equals(field, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

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
            case ValueKind.Record:
                throw ExpressionException.TypeMismatch("a record is not a number");
            case ValueKind.Date:
                // A date has an order, not a size: turning it into a number would make "add a day"
                // and "add 86400" look like the same idea, and one of them is wrong twice a year.
                throw ExpressionException.TypeMismatch("a date is not a number");
            case ValueKind.Image:
                throw ExpressionException.TypeMismatch("a picture is not a number");
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
        ValueKind.Record => _fields.Count > 0,
        ValueKind.Date => true,
        ValueKind.Image => true,
        _ => false,
    };

    /// <summary>
    /// The value as a moment, for the comparisons and list orders that have to put dates in time
    /// order. Text is read as a date when it is written like one; anything else is not a date, and
    /// the caller keeps whatever it did before rather than treating a number as a day.
    /// </summary>
    public bool TryDate(out DateTimeOffset date)
    {
        if (Kind is ValueKind.Date)
        {
            date = _date;
            return true;
        }

        if (Kind is ValueKind.Text)
        {
            return DateTimeOffset.TryParse(_text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out date);
        }

        date = default;
        return false;
    }

    /// <summary>
    /// A moment as the text it is read and written by. A day with no time on it says only the day:
    /// a report that asks for "today" should not print a midnight nobody meant.
    /// </summary>
    public static string TextOf(DateTimeOffset moment)
        => moment.TimeOfDay == TimeSpan.Zero
            ? moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The value written out the way a user would read it.</summary>
    public string AsText() => Kind switch
    {
        ValueKind.Number => FormatNumber(_number),
        ValueKind.Bool => _flag ? "true" : "false",
        ValueKind.Text => _text,
        ValueKind.Date => TextOf(_date),
        ValueKind.List => string.Join(", ", _items.Select(item => item.AsText())),
        ValueKind.Record => JsonValues.Written(this),
        ValueKind.Image => _text,
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

    /// <summary>
    /// True when two values are the same value of the same kind. A number is not the line that
    /// writes it as a number and a picture is not its name: asking whether they are equal is a
    /// different question from asking whether one can stand for the other, and
    /// <see cref="ValueRules.Same"/> is where that second question is answered.
    /// </summary>
    public bool Equals(Value? other)
    {
        if (other is null || Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            ValueKind.Null => true,
            ValueKind.Number => _number.Equals(other._number),
            ValueKind.Bool => _flag == other._flag,
            ValueKind.Text or ValueKind.Image =>
                string.Equals(_text, other._text, StringComparison.OrdinalIgnoreCase),
            ValueKind.Date => _date.UtcTicks == other._date.UtcTicks,
            ValueKind.List => _items.SequenceEqual(other._items),
            ValueKind.Record => _fields.Count == other._fields.Count
                && _fields.Zip(other._fields).All(pair =>
                    string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
                    && Equals(pair.First.Value, pair.Second.Value)),
            _ => false,
        };
    }

    public override bool Equals(object? obj) => Equals(obj as Value);

    /// <summary>
    /// One line for the value, so two that are equal always hash alike: a number by its exact
    /// digits, a moment by its instant, a line by its letters, and a list or a record by what it
    /// holds. <see cref="ValueRules.Canonical"/> is the same idea written out.
    /// </summary>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(ValueRules.Canonical(this));

    public override string ToString() => AsText();
}
