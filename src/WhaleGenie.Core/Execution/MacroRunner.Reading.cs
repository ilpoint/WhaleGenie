using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
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
        => new(step, Variables, _devices.Screen.RefreshMilliseconds, Evaluation);

    /// <summary>
    /// The rectangle a record field names, or null when nobody filled it in. A value written there
    /// that is not a record carrying all four sides is refused with what was written.
    /// </summary>
    private (int Left, int Top, int Width, int Height)? RecordedRect(ExecutableStep step,
        string name)
    {
        var value = Args(step).ValueOf(name);
        if (value.IsNull)
        {
            return null;
        }

        var written = step.Text(name).Trim();
        return value.IsRecord && Sides(value) is { } sides
            ? sides
            : throw new StepFailure(RunKeys.BadRegion, written);
    }

    /// <summary>
    /// Which hit a step means, counted from one. Anything that does not read as a number from one
    /// onwards falls back to the first, so a half-written step aims at something rather than at
    /// nothing.
    /// </summary>
    private int Index(ExecutableStep step, string name, int fallback)
    {
        var text = Args(step).Text(name).Trim();
        return text.Length == 0 ? fallback : Math.Clamp(Args(step).Number(name), 1, RunLimits.Matches);
    }

    /// <summary>
    /// A path a step named, with any <c>variable</c> already filled in. Vision and the file family
    /// both name their target this way, so the reading lives here rather than with either of them.
    /// </summary>
    private string PathOf(ExecutableStep step, string parameter = "path")
        => Args(step).ValueOf(parameter).AsText();

    /// <summary>
    /// A yes/no setting. A step that says nothing takes the catalogue's own default, so a
    /// missing switch behaves the way the action editor showed it rather than always being on.
    /// </summary>
    private static bool Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The severity a log line was written with, and the everyday one when it says none.</summary>
    private static LogLevel Level(string level) => level.ToLowerInvariant() switch
    {
        "warn" => LogLevel.Warn,
        "error" => LogLevel.Error,
        "debug" => LogLevel.Debug,
        _ => LogLevel.Info,
    };

    /// <summary>What a shape comes to, with the names in it worked out where they stand.</summary>
    private Value Read(ExpressionNode written)
        => Expression.Evaluate(written, Variables, Evaluation);

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
        => ShapeWords.Of(row.GetValueOrDefault(column));

    /// <summary>
    /// What a column of a row comes to, worked out from the shape itself. A cell may hold a number,
    /// a name or a sum; nothing renders it into a line to be read back. This is the reader for a
    /// column whose value is used, as opposed to one that names something.
    /// </summary>
    private Value CellValue(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written) ? Read(written) : Value.Null;

    /// <summary>What a row of a list parameter says in one column, or nothing when it has none.</summary>
    private static ExpressionNode Column(IReadOnlyDictionary<string, ExpressionNode> row,
        string column)
        => row.GetValueOrDefault(column) ?? Formula.Nothing;

    /// <summary>
    /// One number of a row that may be left empty, where empty means the run's own value: a row
    /// only has to spell out the hold or the gap that is not the one everywhere else.
    /// </summary>
    private int Cell(IReadOnlyDictionary<string, ExpressionNode> row, string column, int fallback)
        => Formula.IsNothing(Column(row, column))
            ? fallback
            : (int)Math.Round(Read(Column(row, column)).AsNumber());

    /// <summary>One side of a rectangle held in a record, in whole screen pixels.</summary>
    private static int Whole(Value side) => (int)Math.Round(side.AsNumber());

    /// <summary>
    /// The four sides a record says a rectangle has, in whole screen pixels: the corner it starts at
    /// and how far it reaches from there. Nothing when the record is not a rectangle, so a record of
    /// some other shape is refused by whoever asked for one.
    /// </summary>
    private static (int Left, int Top, int Width, int Height)? Sides(Value record)
    {
        if (record.Field(FieldNames.Left) is not { } left
            || record.Field(FieldNames.Top) is not { } top
            || record.Field(FieldNames.Width) is not { } width
            || record.Field(FieldNames.Height) is not { } height)
        {
            return null;
        }

        return (Whole(left), Whole(top), Math.Max(1, Whole(width)), Math.Max(1, Whole(height)));
    }

    /// <summary>
    /// The switches a text comparison carries. Spacing is a question only "exactly" asks by
    /// default; leaving the switch out keeps that default, while writing it false keeps the spaces
    /// the text actually has.
    /// </summary>
    private TextCompareOptions OptionsOf(ExecutableStep step, string way)
        => TextRule.Options(way, Flag(step, "caseSensitive", false), Flag(step, "trim", true),
            Flag(step, "multiline", false));
}
