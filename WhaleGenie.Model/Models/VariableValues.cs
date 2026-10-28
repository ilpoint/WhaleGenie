using System;
using System.Globalization;
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
            store.System.Set(variable.Name, Read(variable.Type, variable.DefaultValue, store));
        }

        return store;
    }

    public static bool TryRead(string type, string written, IVariableResolver? variables,
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

        if (type is "date" or "color" && text[0] != '$')
        {
            value = Value.FromText(text);
            error = string.Empty;
            return true;
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
            "number" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var number)
                ? Value.FromNumber(number)
                : Value.Null,
            "bool" => bool.TryParse(text, out var flag)
                ? Value.FromBool(flag)
                : Value.Null,
            "list" => ListText.TryParse(text, variables, out var items, out _)
                ? Value.FromList(items)
                : Value.Null,
            _ => Value.FromText(text),
        };

        error = value.IsNull && type is "number" or "bool" or "list"
            ? text
            : string.Empty;
        return error.Length == 0;
    }

    public static Value Read(string type, string written, IVariableResolver? variables = null)
        => TryRead(type, written, variables, out var value, out _) ? value : Empty(type);

    public static string Preview(string type, string written, IVariableResolver? variables = null)
        => written.Trim().Length == 0
            ? string.Empty
            : TryRead(type, written, variables, out var value, out var error)
                ? value.AsText()
                : error;

    public static bool HasError(string type, string written, IVariableResolver? variables = null)
        => !TryRead(type, written, variables, out _, out _);

    private static bool ConvertToType(string type, Value source, out Value value, out string error)
    {
        try
        {
            value = type switch
            {
                "number" => Value.FromNumber(source.AsNumber()),
                "bool" => Value.FromBool(source.AsBool()),
                "list" => Value.FromList(source.AsList()),
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

    private static Value Empty(string type) => type switch
    {
        "number" => Value.FromNumber(0),
        "bool" => Value.FromBool(false),
        "list" => Value.FromList([]),
        _ => Value.FromText(string.Empty),
    };
}
