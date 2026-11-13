using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// One captured parameter value and the row shape a table parameter carries.
/// <summary>
/// One parameter value captured for a step: the shape it was written in, and the rows when the
/// field holds a list of them. How it is written to a file is in the other half of this type.
/// </summary>
public partial class StepParameter
{
    public required string Name { get; init; }

    public required ActionParameterKind Kind { get; init; }

    /// <summary>
    /// What the field holds, as the shape it was written in. A field is not a line of text: a piece
    /// of prose with a name in it, a number, and a formula are three shapes, and keeping them apart
    /// is what stops the editor having to guess which one a line means.
    /// </summary>
    public ExpressionNode Written { get; set; } = Formula.Nothing;

    /// <summary>
    /// The value as the line it is said as. This is a rendering: the checks, the labels and the
    /// editor read it when they want the value put into words, and nothing is read back out of it
    /// — the shape above is the value. A field is written once, where it is filled in, and from
    /// there on it is carried as it stands.
    /// </summary>
    public string Value => FormulaText.Write(Written);

    /// <summary>
    /// How much the value may move from run to run, written as a fraction of it: 0.2 is "20%
    /// either way", so 500 lands somewhere between 400 and 600. Only lengths of time are given
    /// one, which is what keeps a wait from looking mechanical.
    /// </summary>
    public decimal Jitter { get; init; }

    /// <summary>True when the value was written with some give in it.</summary>
    public bool HasJitter => Jitter > 0m;

    /// <summary>Child steps, filled in when <see cref="Kind"/> is <see cref="ActionParameterKind.Steps"/>.</summary>
    public List<MacroStep> Steps { get; init; } = [];

    /// <summary>
    /// Whether this list is open in the editor. A block that runs several lists gives each of them
    /// a row of its own, and those rows fold like a block does. It is the editor's memory rather
    /// than part of the macro, so it is never written to the file.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// The rows of a parameter that holds a list of them, filled in when <see cref="Kind"/> is
    /// <see cref="ActionParameterKind.Region"/>, <see cref="ActionParameterKind.KeySequence"/> or
    /// <see cref="ActionParameterKind.Images"/>; a <see cref="ActionParameterKind.Returns"/> holds
    /// one row per result the step hands over, a <see cref="ActionParameterKind.Narrow"/> one row
    /// per thing the element has to be like, and a <see cref="ActionParameterKind.ColourPoints"/>
    /// one row per colour the pattern wants beside its base colour. What the columns of a row mean
    /// belongs to the action, so this holds the names and what they say rather than a shape of its
    /// own.
    /// </summary>
    public List<StepParameterRow> Rows { get; init; } = [];

    /// <summary>
    /// True when the value is a list of rows rather than one thing. Those are the parameters whose
    /// value is written in the file as an array of objects, and what a reader makes of one depends
    /// on the action rather than on the shape: every place a step looks is a name it has to have,
    /// while every press of a key run is only itself.
    /// </summary>
    public bool IsRowList => Kind is ActionParameterKind.Region
        or ActionParameterKind.KeySequence or ActionParameterKind.Images or ActionParameterKind.Returns
        or ActionParameterKind.Arguments or ActionParameterKind.Narrow
        or ActionParameterKind.ColourPoints;

    /// <summary>Every value written in the rows of a list parameter, by row.</summary>
    public IEnumerable<ExpressionNode> RowValues => Rows.SelectMany(row => row.Cells.Values);

}

/// <summary>
/// One row of a parameter that holds a list of rows, such as the regions a search looks in: what
/// each of the row's columns says, by name. Which columns there are and what they mean belongs to
/// the action, so a row is written down as it is rather than as a shape of its own — the same row
/// therefore serves a search region and, later, a chord of a key sequence.
/// </summary>
public class StepParameterRow
{
    /// <summary>
    /// What each column of this row holds, by column name. A cell is a shape for the same reason a
    /// value is: a name in one is a name rather than a spelling, and nothing has to guess whether a
    /// <c>$</c> in it is a reference or a punctuation mark.
    /// </summary>
    public Dictionary<string, ExpressionNode> Cells { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a column holds, or nothing when the row has no such column.</summary>
    public ExpressionNode Cell(string column)
        => Cells.TryGetValue(column, out var value) ? value : Formula.Nothing;

    /// <summary>
    /// The line a column says, for the places that still speak lines. Only out: a column is a
    /// shape, and this rendering is never read back into one.
    /// </summary>
    public string Text(string column) => FormulaText.Write(Cell(column));

    /// <summary>Puts a shape in a column. The only way a column gets a value.</summary>
    public void Set(string column, ExpressionNode value) => Cells[column] = value;

    /// <summary>
    /// Puts a line typed into an editor into a column. This is the one place a person's words
    /// become a shape; a value that came from the picker or the file is a shape already and uses
    /// <see cref="Set"/>. It is named on its own so the line never looks like the value.
    /// </summary>
    public void SetLine(string column, string line) => Cells[column] = Formula.FromLine(line);

    /// <summary>A row that says one thing in one column, which is how a named region is written.</summary>
    public static StepParameterRow Of(string column, ExpressionNode value) => new()
    {
        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            [column] = value,
        },
    };

    /// <summary>A copy, so a duplicated step does not share its rows with the step it came from.</summary>
    public StepParameterRow Copy() => new()
    {
        Cells = new Dictionary<string, ExpressionNode>(Cells, StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>A single step of a macro, saved as one node of the macro's JSON tree.</summary>
