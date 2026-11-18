using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// Every variable the editor can offer: the ones WhaleGenie provides, the shared ones the
/// user creates in the Variable Center, and the ones a macro creates for itself.
/// </summary>
public static class VariableCatalog
{
    /// <summary>Raised after a person adds or removes a project-level global variable.</summary>
    public static event Action? GlobalsChanged;

    /// <summary>Built-in variables, read-only inside a macro.</summary>
    public static IReadOnlyList<VariableDefinition> SystemVariables { get; } =
    [
        SystemVariable("sys.loopIndex", "number",
            "Which round of the innermost loop is running. Only visible inside a loop body.", "0"),
        SystemVariable("sys.macroName", "text",
            "The name of the macro that is running right now. A macro called by another one answers "
            + "with its own name.", string.Empty),
        SystemVariable("sys.macroFolder", "text",
            "The folder a path is counted from when a file step is given a relative one.",
            string.Empty),

        SystemVariable("sys.activeWindow", "text",
            "The title of the window that is in front right now.", string.Empty),
        SystemVariable("sys.activeProcess", "text",
            "The program that owns the window in front, without the .exe.", string.Empty),

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

        SystemVariable("sys.screenWidth", "number",
            "Width of the desk, every display together; the same space macro coordinates are in.",
            "0"),
        SystemVariable("sys.screenHeight", "number",
            "Height of the desk, every display together.", "0"),
        SystemVariable("sys.machineName", "text", "Name of this computer.", string.Empty),
        SystemVariable("sys.userName", "text", "Name of the logged-in user.", string.Empty),
        SystemVariable("sys.clipboard", "text", "Current clipboard text.", string.Empty),

        SystemVariable("caught.key", "text",
            "The failure key being filtered inside a try step. Empty outside that check.", string.Empty),
        SystemVariable("caught.detail", "text",
            "The detail of the failure being filtered inside a try step. Empty outside that check.",
            string.Empty),
    ];

    private static readonly Dictionary<string, VariableDefinition> SystemByName =
        SystemVariables.ToDictionary(variable => variable.Name);

    /// <summary>
    /// What a variable is called in Chinese, or nothing where it goes by its own name. Only the ones
    /// WhaleGenie ships have a word of their own: a macro's variables are called whatever their
    /// author called them, and a Chinese name would be a name nobody chose.
    /// </summary>
    public static string Label(string name) => Strings.Get($"Variable.{name}.label", string.Empty);

    /// <summary>
    /// How a variable is named on screen: the Chinese name with the code name beside it where it has
    /// one — 剪贴板 (sys.clipboard) — and the code name alone otherwise, so a reader who has met
    /// either of the two recognises the other. <paramref name="token"/> writes the code name the way
    /// a formula writes it, with the dollar sign, which is what the places a name goes into a field
    /// have always shown.
    /// </summary>
    public static string Display(string name, bool token)
    {
        var written = token ? "$" + name : name;
        return Label(name) is { Length: > 0 } chinese ? $"{chinese} ({written})" : written;
    }

    /// <summary>
    /// What a variable is called inside a sentence: the Chinese name where it has one and the name
    /// its author gave it otherwise. Nothing goes beside it here — a sentence reads 取【剪贴板】里的
    /// 【第1项】 — because the name the code calls it by is already on the line above, which is where
    /// the formula itself is written.
    /// </summary>
    public static string Spoken(string name)
        => Label(name) is { Length: > 0 } chinese ? chinese : name;

    /// <summary>Shared variables, managed from the Variable Center.</summary>
    public static ObservableCollection<VariableDefinition> Globals { get; } = [];

    /// <summary>True when WhaleGenie provides this variable itself.</summary>
    public static bool IsSystem(string name) => SystemByName.ContainsKey(name);

    /// <summary>True when the Variable Center defines this shared variable.</summary>
    public static bool IsGlobal(string name)
        => Globals.Any(variable =>
            string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a global variable by name.</summary>
    public static VariableDefinition? FindGlobal(string name)
        => Globals.FirstOrDefault(variable =>
            string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));

    public static void NotifyGlobalsChanged() => GlobalsChanged?.Invoke();

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
