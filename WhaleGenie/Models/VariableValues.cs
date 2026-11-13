using System;
using System.Globalization;
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
    /// <summary>A value context for the editor: shared values plus the built-in starting values.</summary>
    public static VariableStore DesignContext()
    {
        var store = new VariableStore();
        foreach (var (name, value) in SharedVariables.Current.Values)
        {
            store.Global.Set(name, value);
        }

        foreach (var variable in VariableCatalog.SystemVariables)
        {
            store.System.Set(variable.Name, Read(variable.Type, variable.Default, store));
        }

        return store;
    }

    public static bool TryRead(ValueKind type, string written, IVariableResolver? variables,
        out Value value, out string error)
    {
        var text = (written ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            value = Empty(type);
            error = string.Empty;
            return true;
        }

        if (text.StartsWith("$$", StringComparison.Ordinal))
        {
            value = Value.FromText(text[1..]);
            error = string.Empty;
            return ConvertToType(type, value, out value, out error);
        }

        if (Expression.TryEvaluate(text, variables, out var evaluated, out var expressionError))
        {
            return ConvertToType(type, evaluated, out value, out error);
        }

        if (text[0] == '$')
        {
            value = Value.Null;
            error = expressionError?.Detail ?? text;
            return false;
        }

        value = type switch
        {
            ValueKind.Number => double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number)
                ? Value.FromNumber(number)
                : Value.Null,
            ValueKind.Bool => bool.TryParse(text, out var flag)
                ? Value.FromBool(flag)
                : Value.Null,
            ValueKind.List => ListText.TryParse(text, variables, out var items, out _)
                ? Value.FromList(items)
                : Value.Null,
            ValueKind.Date => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var moment)
                ? Value.FromDate(moment)
                : Value.Null,
            _ => Value.FromText(text),
        };

        error = value.IsNull && type is ValueKind.Number or ValueKind.Bool
            or ValueKind.List or ValueKind.Date
            ? text
            : string.Empty;
        return error.Length == 0;
    }

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

    public static Value Read(ValueKind type, string written, IVariableResolver? variables = null)
        => TryRead(type, written, variables, out var value, out _) ? value : Empty(type);

    public static string Preview(ValueKind type, string written, IVariableResolver? variables = null)
        => written.Trim().Length == 0
            ? string.Empty
            : TryRead(type, written, variables, out var value, out var error)
                ? value.AsText()
                : error;

    public static bool HasError(ValueKind type, string written, IVariableResolver? variables = null)
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
                ValueKind.Date => Moment(source),
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
    /// A value read as a moment. A date stays one; a line is read as a date when it is written like
    /// one, so a value kept by an older build still opens rather than stopping the project.
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
