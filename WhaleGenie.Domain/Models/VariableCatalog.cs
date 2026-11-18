using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// The variables WhaleGenie provides itself, and the words they are shown by. The project's own
/// shared variables are not here: they belong to a project, so they live in a
/// <see cref="VariableSession"/> rather than beside the built-in ones.
/// </summary>
public static class VariableCatalog
{
    /// <summary>Built-in variables, read-only inside a macro.</summary>
    public static IReadOnlyList<VariableDefinition> SystemVariables { get; } =
    [
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

        SystemVariable(VariableStore.LoopIndexName, "number",
            "The round of the loop that is running, from 0. It has no value outside one.", string.Empty,
            seeded: false),

        SystemVariable(MacroRunner.CaughtKeyName, "text",
            "The failure key being filtered inside a try step. Empty outside that check.", string.Empty),
        SystemVariable(MacroRunner.CaughtDetailName, "text",
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
    /// either of the two recognises the other. The code name is written as the name it is: the sign
    /// that reads it belongs to a line of formula, not to the name.
    /// </summary>
    public static string Display(string name)
        => Label(name) is { Length: > 0 } chinese ? $"{chinese} ({name})" : name;

    /// <summary>
    /// What a variable is called inside a sentence: the Chinese name where it has one and the name
    /// its author gave it otherwise. Nothing goes beside it here — a sentence reads 取【剪贴板】里的
    /// 【第1项】 — because the name the code calls it by is already on the line above, which is where
    /// the formula itself is written.
    /// </summary>
    public static string Spoken(string name)
        => Label(name) is { Length: > 0 } chinese ? chinese : name;

    /// <summary>True when WhaleGenie provides this variable itself.</summary>
    public static bool IsSystem(string name) => SystemByName.ContainsKey(name);

    private static VariableDefinition SystemVariable(string name, string type, string description,
        string defaultValue, bool seeded = true)
        => new()
        {
            Name = name,
            Scope = VariableScope.System,
            Type = VariableTypes.KindOf(type),
            Description = description,
            Default = Formula.Text(defaultValue),
            Seeded = seeded,
        };
}
