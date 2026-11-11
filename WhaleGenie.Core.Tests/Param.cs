using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A parameter a test hands a step. A test states a field the way a macro file does — a name and a
/// line of text — so the shapes it can take are written down here rather than at every call site.
/// </summary>
internal static class Fields
{
    /// <summary>One field holding one line, and how much it may move from run to run.</summary>
    public static ExecutableParameter Text(string name, string text, decimal jitter = 0m)
        => new() { Name = name, Text = text, Jitter = jitter };

    /// <summary>A yes/no field, written the way the file writes one.</summary>
    public static ExecutableParameter Flag(string name, bool value)
        => Text(name, value ? "true" : "false");

    /// <summary>A field holding a list of rows.</summary>
    public static ExecutableParameter Rows(string name,
        params IReadOnlyDictionary<string, string>[] rows)
        => new() { Name = name, Rows = rows };

    /// <summary>A field holding child steps.</summary>
    public static ExecutableParameter Steps(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };
}

/// <summary>One row of a list parameter: what each of its columns says, by name.</summary>
internal static class Cells
{
    public static IReadOnlyDictionary<string, string> Of(params (string Column, string Text)[] cells)
        => cells.ToDictionary(cell => cell.Column, cell => cell.Text, StringComparer.OrdinalIgnoreCase);
}
