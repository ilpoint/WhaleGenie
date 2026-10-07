using System;
using System.Globalization;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>
/// Builds the variables a macro starts from: the built-in ones read live, and the shared ones at
/// the values the Variable Center holds. Every run begins here, whether it was started from the
/// run window or by a trigger on the main window.
/// </summary>
public static class MacroVariables
{
    public static VariableStore Seed()
    {
        var store = new VariableStore();

        foreach (var definition in VariableCatalog.SystemVariables)
        {
            store.System.Set(definition.Name, Convert(definition.Type, Read(definition)));
        }

        foreach (var definition in VariableCatalog.Globals)
        {
            store.Global.Set(definition.Name, Convert(definition.Type, definition.DefaultValue));
        }

        return store;
    }

    private static string Read(VariableDefinition definition)
    {
        try
        {
            return SystemVariableReader.Read(definition.Name, 0, 0);
        }
        catch
        {
            // Screen and clipboard reads can fail outside a session; the default will do.
            return definition.DefaultValue;
        }
    }

    private static Value Convert(string type, string text) => type switch
    {
        "number" => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
            ? Value.FromNumber(number)
            : Value.FromNumber(0),
        "bool" => Value.FromBool(string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)),
        "list" => ListText.TryParse(text, out var items, out _)
            ? Value.FromList(items)
            : Value.FromList([]),
        _ => Value.FromText(text),
    };
}
