using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.Models;

/// <summary>
/// Reads a declared variable value with the same value language as action fields. Expressions are
/// stored as written, while current values are evaluated before they go into the shared table.
/// </summary>
public static class VariableValues
{
    /// <summary>
    /// Reads a declared value that is already the shape it was written in. Nothing is parsed here:
    /// the shape is worked out and the result is made the declared kind, which is the same rule the
    /// line above follows once the line has been read.
    /// </summary>
    public static bool TryRead(ValueKind type, ExpressionNode written, IVariableResolver? variables,
        out Value value, out string error)
    {
        if (Formula.IsNothing(written))
        {
            value = Empty(type);
            error = string.Empty;
            return true;
        }

        try
        {
            return ConvertToType(type, Expression.Evaluate(written, variables), out value, out error);
        }
        catch (ExpressionException failure)
        {
            value = Value.Null;
            error = failure.Detail;
            return false;
        }
    }

    public static Value Read(ValueKind type, ExpressionNode written,
        IVariableResolver? variables = null)
        => TryRead(type, written, variables, out var value, out _) ? value : Empty(type);

    public static string Preview(ValueKind type, ExpressionNode written,
        IVariableResolver? variables = null)
        => Formula.IsNothing(written)
            ? string.Empty
            : TryRead(type, written, variables, out var value, out var error)
                ? value.AsText()
                : error;

    public static bool HasError(ValueKind type, ExpressionNode written,
        IVariableResolver? variables = null)
        => !TryRead(type, written, variables, out _, out _);

    private static bool ConvertToType(ValueKind type, Value source, out Value value, out string error)
    {
        try
        {
            value = type switch
            {
                ValueKind.Number => Value.FromNumber(source.AsNumber()),
                ValueKind.Bool => Value.FromBool(source.AsBool()),
                ValueKind.List => Value.FromList(source.AsList()),
                ValueKind.Record => Record(source),
                ValueKind.Date => Moment(source),
                ValueKind.Image => Picture(source),
                _ => Value.FromText(source.AsText()),
            };
            error = string.Empty;
            return true;
        }
        catch (ExpressionException failure)
        {
            value = Value.Null;
            error = failure.Detail;
            return false;
        }
    }

    /// <summary>
    /// A value read as a record. A record stays one; a line is read as the JSON object a record is
    /// written as, which is the same text <see cref="Value.AsText"/> gives back.
    /// </summary>
    private static Value Record(Value source)
    {
        if (source.IsRecord)
        {
            return source;
        }

        var written = source.AsText();
        try
        {
            return JsonValues.FromJson(JsonNode.Parse(written)) is { IsRecord: true } record
                ? record
                : throw ExpressionException.TypeMismatch($"\"{written}\" is not a record");
        }
        catch (JsonException)
        {
            throw ExpressionException.TypeMismatch($"\"{written}\" is not a record");
        }
    }

    /// <summary>
    /// A value read as a picture. A picture is a handle to pixels a run holds, so it cannot be
    /// written down: a literal that spells a handle is a line, not the picture.
    /// </summary>
    private static Value Picture(Value source)
        => source.IsImage
            ? source
            : throw ExpressionException.TypeMismatch("a picture comes from a run, not from a written value");

    /// <summary>
    /// A value read as a moment. A date stays one; a line is read as a date when it is written like
    /// one.
    /// </summary>
    private static Value Moment(Value source)
    {
        if (source.IsDate)
        {
            return source;
        }

        return DateTimeOffset.TryParse(source.AsText(), CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var moment)
            ? Value.FromDate(moment)
            : throw ExpressionException.TypeMismatch($"\"{source.AsText()}\" is not a date");
    }

    /// <summary>
    /// What a declaration of this kind holds when nothing was written for it. The run and the
    /// editor's preview read it from the same place, so a preview cannot promise a number where the
    /// run starts from an empty value.
    /// </summary>
    private static Value Empty(ValueKind type) => ValueRules.Empty(type);
}
