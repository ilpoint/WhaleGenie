using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Viktor.Models;

/// <summary>
/// Every variable the editor can offer: the ones Viktor provides, the shared ones the
/// user creates in the Variable Center, and the ones a macro creates for itself.
/// </summary>
public static class VariableCatalog
{
    /// <summary>Built-in variables, read-only inside a macro.</summary>
    public static IReadOnlyList<VariableDefinition> SystemVariables { get; } =
    [
        SystemVariable("sys.macroLoop", "number", "How many times the whole macro has looped.",
            "0"),
        SystemVariable("sys.loopIndex", "number",
            "Which round of the innermost loop is running. Only visible inside a loop body.", "0"),

        SystemVariable("sys.date", "text", "Today's date, as yyyy-MM-dd.", "2026-10-05"),
        SystemVariable("sys.dateLong", "text", "Today's date with the weekday, for example 2026-10-05 Monday.",
            "2026-10-05 Monday"),
        SystemVariable("sys.time", "text", "The current time, as HH:mm:ss.", "09:30:00"),
        SystemVariable("sys.dateTime", "text", "Date and time together, as yyyy-MM-dd HH:mm:ss.",
            "2026-10-05 09:30:00"),
        SystemVariable("sys.year", "number", "Current year.", "2026"),
        SystemVariable("sys.month", "number", "Current month, 1 to 12.", "10"),
        SystemVariable("sys.day", "number", "Day of the month, 1 to 31.", "5"),
        SystemVariable("sys.weekday", "text", "Name of the current weekday.", "Monday"),
        SystemVariable("sys.hour", "number", "Current hour, 0 to 23.", "9"),
        SystemVariable("sys.minute", "number", "Current minute, 0 to 59.", "30"),
        SystemVariable("sys.second", "number", "Current second, 0 to 59.", "0"),
        SystemVariable("sys.timestamp", "number", "Unix timestamp in seconds.", "0"),

        SystemVariable("sys.mouseX", "number", "Current mouse X position.", "0"),
        SystemVariable("sys.mouseY", "number", "Current mouse Y position.", "0"),
        SystemVariable("sys.mouseColor", "text", "Colour of the pixel under the cursor, as #RRGGBB.",
            "#000000"),

        SystemVariable("sys.screenWidth", "number", "Width of the main screen.", "0"),
        SystemVariable("sys.screenHeight", "number", "Height of the main screen.", "0"),
        SystemVariable("sys.machineName", "text", "Name of this computer.", string.Empty),
        SystemVariable("sys.userName", "text", "Name of the logged-in user.", string.Empty),
        SystemVariable("sys.clipboard", "text", "Current clipboard text.", string.Empty),
        SystemVariable("sys.random", "number", "Random number between 0 and 1.", "0"),
    ];

    private static readonly Dictionary<string, VariableDefinition> SystemByName =
        SystemVariables.ToDictionary(variable => variable.Name);

    /// <summary>Shared variables, managed from the Variable Center.</summary>
    public static ObservableCollection<VariableDefinition> Globals { get; } = [];

    /// <summary>True when Viktor provides this variable itself.</summary>
    public static bool IsSystem(string name) => SystemByName.ContainsKey(name);

    /// <summary>True when the Variable Center defines this shared variable.</summary>
    public static bool IsGlobal(string name)
        => Globals.Any(variable =>
            string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a global variable by name.</summary>
    public static VariableDefinition? FindGlobal(string name)
        => Globals.FirstOrDefault(variable =>
            string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Names a picker should offer: system and global variables first, then the ones
    /// the surrounding macro created for itself.
    /// </summary>
    public static IReadOnlyList<string> Names(IEnumerable<string>? localNames = null)
    {
        var names = new SortedSet<string>(SystemVariables.Select(variable => variable.Name),
            StringComparer.OrdinalIgnoreCase);
        names.UnionWith(Globals.Select(variable => variable.Name));
        names.UnionWith(localNames ?? Enumerable.Empty<string>());
        return [.. names];
    }

    private static VariableDefinition SystemVariable(string name, string type, string description,
        string defaultValue)
        => new()
        {
            Name = name,
            Scope = VariableScope.System,
            Type = type,
            Description = description,
            DefaultValue = defaultValue,
        };
}
