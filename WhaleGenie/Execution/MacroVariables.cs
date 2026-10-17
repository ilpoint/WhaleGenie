using System;
using System.Collections.Generic;
using System.Globalization;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>
/// Builds the variables a macro starts from: the built-in ones, the shared ones at the values the
/// Variable Center holds, and — when the caller has one — the reader that answers the built-in ones
/// at the moment a step reads them. Every run begins here, whether it was started from the run
/// window or by a trigger on the main window.
/// </summary>
public static class MacroVariables
{
    /// <param name="system">
    /// Where the values that move are read from. Left out by a caller that has no devices — a check
    /// that drives the engine directly — and then the built-in variables hold their declared
    /// starting values and nothing is read off a machine.
    /// </param>
    public static VariableStore Seed(SystemVariableReader? system = null, VariableBag? shared = null)
    {
        var store = new VariableStore(shared);

        // The declared values are what a machine that cannot answer falls back on; the reader, when
        // there is one, is asked before them every time a step reads a built-in variable.
        foreach (var definition in VariableCatalog.SystemVariables)
        {
            store.System.Set(definition.Name, Convert(definition.Type, definition.DefaultValue));
        }

        store.Live = system;

        // A global's declared value is what it starts at, not what every run puts back. A second
        // run starting while the first is still going must see what the first wrote, so a name that
        // is already in the shared table keeps its value. A name the project no longer declares is
        // taken out, or a removed variable would stay writable for the rest of the program.
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in VariableCatalog.Globals)
        {
            declared.Add(definition.Name);
            store.Global.SetIfAbsent(definition.Name,
                Convert(definition.Type, definition.DefaultValue));
        }

        foreach (var name in store.Global.Values.Keys)
        {
            if (!declared.Contains(name))
            {
                store.Global.Remove(name);
            }
        }

        return store;
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
