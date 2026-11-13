using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A parameter a test hands a step. A test states the shape it means — a piece of text, a name, a
/// call, an operation — rather than a line that has to be read into one. Nothing here parses a
/// line, so a fixture cannot quietly make the engine keep a language it no longer has.
/// </summary>
internal static class Fields
{
    /// <summary>
    /// One field holding the literal a test wrote down. A number is a number, a yes/no word is a
    /// flag, and everything else is prose: these are the shapes of literals in the test's own
    /// language, not a line read into a tree. A test that means a formula or a name says so with
    /// <see cref="Computed"/> or <see cref="Reference"/>, where the tree is built by hand.
    /// </summary>
    public static ExecutableParameter Text(string name, string text, decimal jitter = 0m)
        => new() { Name = name, Written = LiteralShape(text), Jitter = jitter };

    internal static ExpressionNode LiteralShape(string text)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            return Formula.Constant("true");
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            return Formula.Constant("false");
        }

        // Only a plain number is a number. AllowThousands would turn "1,000" into a
        // value the field never meant, so commas, signs in prose and dates stay text.
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Formula.Number(text, number)
            : Formula.Text(text);
    }

    /// <summary>One field holding a run of pieces already said as shapes.</summary>
    public static ExecutableParameter Run(string name, ExpressionNode value, decimal jitter = 0m)
        => new() { Name = name, Written = value, Jitter = jitter };

    /// <summary>One field holding a name and nothing else.</summary>
    public static ExecutableParameter Reference(string name, string variable, decimal jitter = 0m)
        => new() { Name = name, Written = Formula.Name(variable), Jitter = jitter };

    /// <summary>One field holding a formula, built as the tree the engine runs.</summary>
    public static ExecutableParameter Computed(string name, ExpressionNode tree, decimal jitter = 0m)
        => new() { Name = name, Written = tree, Jitter = jitter };

    /// <summary>Several pieces standing next to each other: text, a name, text again.</summary>
    public static ExecutableParameter Parts(string name, params ExpressionNode[] parts)
        => new() { Name = name, Written = Formula.Run(parts) };

    /// <summary>Text with no names in it at all.</summary>
    public static ExecutableParameter Literal(string name, string text, decimal jitter = 0m)
        => Text(name, text, jitter);

    /// <summary>A yes/no field, written the way the file writes one.</summary>
    public static ExecutableParameter Flag(string name, bool value)
        => new() { Name = name, Written = Formula.Constant(value ? "true" : "false") };

    /// <summary>A field holding a list of rows.</summary>
    public static ExecutableParameter Rows(string name,
        params IReadOnlyDictionary<string, ExpressionNode>[] rows)
        => new() { Name = name, Rows = rows };

    /// <summary>A field holding a list of rows whose cells are stated as prose.</summary>
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
    /// <summary>
    /// What a cell of a row says, for an assertion to compare. A name is said as the name itself:
    /// there is no sign in front of it, and a shape that names something is not prose to flatten.
    /// </summary>
    public static string Spoken(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => Spoken(row.GetValueOrDefault(column));

    private static string Spoken(ExpressionNode? value) => value switch
    {
        null => string.Empty,
        TextNode text => text.Text,
        NumberNode number => number.Text,
        DateNode date => date.Text,
        ConstantNode constant => constant.Word,
        VariableNode variable => variable.Name,
        ConcatNode run => string.Concat(run.Parts.Select(Spoken)),
        _ => Formula.Plain(value),
    };

    /// <summary>One cell holding the literal a test wrote down.</summary>
    public static ExpressionNode Line(string text) => Fields.LiteralShape(text);

    public static IReadOnlyDictionary<string, ExpressionNode> Of(
        params (string Column, string Text)[] cells)
        => cells.ToDictionary(cell => cell.Column, cell => Fields.LiteralShape(cell.Text),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>One row whose cells are already shapes: a reference, a call, or prose.</summary>
    public static IReadOnlyDictionary<string, ExpressionNode> Of(
        params (string Column, ExpressionNode Value)[] cells)
        => cells.ToDictionary(cell => cell.Column, cell => cell.Value,
            StringComparer.OrdinalIgnoreCase);
}
