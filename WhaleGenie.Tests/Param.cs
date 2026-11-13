using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Tests;

/// <summary>
/// A parameter a test hands a step. A test states a field the way a macro file does — a name and a
/// line of text — so the shapes it can take are written down here rather than at every call site.
/// </summary>
internal static class Fields
{
    /// <summary>One field holding one line, and how much it may move from run to run.</summary>
    public static ExecutableParameter Text(string name, string text, decimal jitter = 0m)
        => new()
        {
            Name = name,
            Written = FormulaText.Read(text, ActionReadMode.WholeValue),
            Jitter = jitter,
        };

    /// <summary>One field holding one line of text, where every character of it is a character.</summary>
    public static ExecutableParameter Run(string name, string text, decimal jitter = 0m)
        => new()
        {
            Name = name,
            Written = FormulaText.Read(text, ActionReadMode.Text),
            Jitter = jitter,
        };


    /// <summary>One field holding a name and nothing else.</summary>
    public static ExecutableParameter Reference(string name, string variable, decimal jitter = 0m)
        => new()
        {
            Name = name,
            Written = WhaleGenie.Core.Expressions.Formula.Name(variable),
            Jitter = jitter,
        };

    /// <summary>One field holding a formula, with the names it reads written as themselves.</summary>
    public static ExecutableParameter Computed(string name, string text, decimal jitter = 0m)
        => new()
        {
            Name = name,
            Written = WhaleGenie.Core.Expressions.Formula.FromFormula(text),
            Jitter = jitter,
        };

    /// <summary>Several pieces standing next to each other: text, a name, text again.</summary>
    public static ExecutableParameter Parts(string name, params ExpressionNode[] parts)
        => new() { Name = name, Written = WhaleGenie.Core.Expressions.Formula.Run(parts) };

    /// <summary>Text with no names in it at all.</summary>
    public static ExecutableParameter Literal(string name, string text, decimal jitter = 0m)
        => new()
        {
            Name = name,
            Written = WhaleGenie.Core.Expressions.Formula.Text(text),
            Jitter = jitter,
        };

    /// <summary>A yes/no field, written the way the file writes one.</summary>
    public static ExecutableParameter Flag(string name, bool value)
        => Text(name, value ? "true" : "false");

    /// <summary>A field holding a list of rows.</summary>
    public static ExecutableParameter Rows(string name,
        params IReadOnlyDictionary<string, ExpressionNode>[] rows)
        => new() { Name = name, Rows = rows };

    /// <summary>
    /// A field holding a list of rows that a test states as lines, the way a macro file keeps them.
    /// The rows are read here so that a fixture written in the file's words needs no other change.
    /// </summary>
    public static ExecutableParameter Rows(string name,
        params IReadOnlyDictionary<string, string>[] rows)
        => new()
        {
            Name = name,
            Rows = [.. rows.Select(row => (IReadOnlyDictionary<string, ExpressionNode>)row
                .ToDictionary(cell => cell.Key, cell => Cells.Line(cell.Value),
                    StringComparer.OrdinalIgnoreCase))],
        };

    /// <summary>A field holding child steps.</summary>
    public static ExecutableParameter Steps(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };
}

/// <summary>One row of a list parameter: what each of its columns says, by name.</summary>
internal static class Cells
{
    /// <summary>What a cell of a row says, as the line it was written as.</summary>
    public static string Spoken(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => FormulaText.Write(row.GetValueOrDefault(column));

    /// <summary>One cell, written the way a macro file writes it.</summary>
    public static ExpressionNode Line(string text)
        => FormulaText.Read(text, ActionReadMode.Text);

    public static IReadOnlyDictionary<string, ExpressionNode> Of(
        params (string Column, string Text)[] cells)
        => cells.ToDictionary(cell => cell.Column,
            cell => FormulaText.Read(cell.Text, ActionReadMode.Text),
            StringComparer.OrdinalIgnoreCase);
}
