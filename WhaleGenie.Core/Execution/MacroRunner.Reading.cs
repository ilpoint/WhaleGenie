using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// How a step reads what it was given, and how a row of a table is read. The rules for a step's own
/// fields live in <see cref="StepArguments"/>, which is what an action is handed; what stays here is
/// the shorthand for making one and the readers for the rows a table action walks.
/// </summary>
public sealed partial class MacroRunner
{
    /// <summary>The reader for the fields of one step.</summary>
    private StepArguments Args(ExecutableStep step)
        => new(step, Variables, _devices.Screen.RefreshMilliseconds);

    /// <summary>What a shape comes to, with the names in it worked out where they stand.</summary>
    private Value Read(ExpressionNode written) => Expression.Evaluate(written, Variables);

    /// <summary>
    /// Reads a line that stands for one value. Only the places that still hold a line — a default
    /// written in the catalogue, an argument the run dialog typed — come through here; a field of a
    /// step is already a shape.
    /// </summary>
    private Value Read(string text) => Read(Formula.Text(text));

    /// <summary>One column of one row, worked out where it stands.</summary>
    private string Text(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written) ? Read(written).AsText() : string.Empty;

    /// <summary>
    /// One column of one row as the word it says. Only a column that names something — a key, a
    /// kind, the name a result is kept under — is read this way; a column holding a name rather
    /// than a piece of text answers with the name it holds.
    /// </summary>
    private static string Word(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => ShapeWord(row.GetValueOrDefault(column));

    /// <summary>
    /// What a column of a row comes to, worked out from the shape itself. A cell may hold a number,
    /// a name or a sum; nothing renders it into a line to be read back. This is the reader for a
    /// column whose value is used, as opposed to one that names something.
    /// </summary>
    private Value CellValue(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written) ? Read(written) : Value.Null;

    /// <summary>
    /// The word a shape holds, read from the shape itself rather than from a line rendered and
    /// read back. A name answers with the name, a run with the words it is made of, and anything
    /// else with the plain value it was written as. There is no sign in front of a name to strip.
    /// </summary>
    /// <remarks>
    /// The other <see cref="Word(System.Collections.Generic.IReadOnlyDictionary{string, ExpressionNode}, string)"/>
    /// overload still renders a line, because the columns it serves can hold a formula that has to
    /// be worked out by the caller; changing them all at once is a different question.
    /// </remarks>
    private static string ShapeWord(ExpressionNode? value) => value switch
    {
        null => string.Empty,
        VariableNode variable => variable.Name.Trim(),
        ConcatNode run => string.Concat(run.Parts.Select(ShapeWord)),
        _ => Formula.Plain(value).Trim(),
    };

    /// <summary>True when a column of a row holds anything at all.</summary>
    private static bool Said(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => !Formula.IsNothing(row.GetValueOrDefault(column));
}
