using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// How a step reads what it was given: one value field, one line of text, one number. These are
/// asked of every action, so they are written here rather than beside any one family.
/// </summary>
public sealed partial class MacroRunner
{
    /// <summary>
    /// Reads one parameter the way its catalogue entry says it is read. A field that names a
    /// variable takes the bare name; a field that holds one value takes an expression or a
    /// literal; a field that holds text has the names in it filled in where they stand.
    /// </summary>
    private Value ReadValue(ExecutableStep step, string name)
    {
        // A field nobody answered is "nothing was said" in every read mode. A number step that
        // leaves a setting out gets its catalogue default, not an empty value that fails to parse.
        var written = step.Written(name);
        if (Formula.IsNothing(written))
        {
            return Value.Null;
        }

        return ReadModeOf(step, name) switch
        {
            ActionReadMode.VariableName => ReadName(written),
            ActionReadMode.WholeValue => Read(written),
            _ => Value.FromText(ReadText(step, name)),
        };
    }

    /// <summary>
    /// Reads one parameter as text. A field that names a variable answers with the name it writes;
    /// every other field answers with what it comes to, worked out rather than written back into a
    /// line and read again.
    /// </summary>
    private string ReadText(ExecutableStep step, string name)
    {
        var written = step.Written(name);
        return ReadModeOf(step, name) switch
        {
            ActionReadMode.VariableName => ReadNameKey(step, name).Name,
            _ => Read(written).AsText(),
        };
    }

    /// <summary>
    /// The name a field writes and the identity it is bound to. A name written into a step is not
    /// read through the formula writer, because that writer puts a dollar in front of a bare name;
    /// the shape says which name it is, and the id says which declaration.
    /// </summary>
    private VariableKey ReadNameKey(ExecutableStep step, string name)
        => Formula.NameOf(step.Written(name));

    private static ActionReadMode ReadModeOf(ExecutableStep step, string name)
        => ActionSemanticsCatalog.Find(step.Type)?.Parameter(name)?.ReadMode ?? ActionReadMode.Text;

    /// <summary>
    /// A field that names a variable rather than holding one: the name is looked up, and what it
    /// holds is the value. A name nothing defines stays the text it was written as, so a step that
    /// waits on a handle no run ever set reports that rather than failing to resolve.
    /// </summary>
    private Value ReadName(ExpressionNode written)
    {
        var key = Formula.NameOf(written);
        if (key.Name.Length == 0)
        {
            return Value.Null;
        }

        return Variables.TryGet(key, out var held) ? held : Value.FromText(key.Name);
    }

    /// <summary>What a shape comes to, with the names in it worked out where they stand.</summary>
    private Value Read(ExpressionNode written) => Expression.Evaluate(written, Variables);

    /// <summary>
    /// Reads a line that stands for one value. Only the places that still hold a line — a default
    /// written in the catalogue, an argument the run dialog typed — come through here; a field of a
    /// step is already a shape.
    /// </summary>
    private Value Read(string text)
        => Expression.Evaluate(FormulaText.Read(text, ActionReadMode.WholeValue), Variables);

    /// <summary>The text a field comes to, with the names in it worked out where they stand.</summary>
    private string Text(ExecutableStep step, string name) => Read(step.Written(name)).AsText();

    /// <summary>One column of one row, worked out where it stands.</summary>
    private string Text(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written) ? Read(written).AsText() : string.Empty;

    /// <summary>
    /// One column of one row as the word it says. Only a column that names something — a key, a
    /// kind, the name a result is kept under — is read this way; a column holding a name rather
    /// than a piece of text answers with the name it holds.
    /// </summary>
    private static string Word(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => FormulaText.Write(row.GetValueOrDefault(column)).Trim();

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

    /// <summary>
    /// A number a step names, read the way every other field is: as a value. A field nobody
    /// answered takes whatever the catalogue says an empty cell means, so "zero", "the edge of the
    /// screen" and "the value the action starts with" are decided in one place rather than at each
    /// call. A value that cannot be worked out is left as the broken expression it is, so the step
    /// reports <c>Run.BadExpression</c> like a text field would — swallowing it into a zero is how
    /// a click meant for something that was found ends up at the corner of the screen, with
    /// nothing anywhere saying why.
    /// </summary>
    private int Number(ExecutableStep step, string name)
    {
        if (!Given(step, name))
        {
            return EmptyNumber(step, name);
        }

        var value = ReadValue(step, name);

        // A record is a set of fields, not a number: reading one as a number would either pick a
        // field nobody named or quietly come out as zero, so a step that put one here is told.
        if (value.IsRecord)
        {
            throw new StepFailure(RunKeys.RecordWhereNumber, name);
        }

        var number = (int)value.AsNumber();

        // The range the field declares — its own limit, or the one its unit gives — is what the
        // number box offers; the engine holds to the same range instead of clamping behind the
        // author's back or passing a value the action cannot mean.
        if (ActionSemanticsCatalog.Find(step.Type)?.Parameter(name) is { Shape: ActionValueShape.Number } facts
            && (number < facts.MinimumValue || number > facts.MaximumValue))
        {
            throw new StepFailure(RunKeys.OutOfRange,
                name + ": " + number.ToString(CultureInfo.InvariantCulture));
        }

        return Vary(step, name, number);
    }

    /// <summary>
    /// What a blank cell comes to, from the catalogue. "To the edge" and "keep what the window has"
    /// are answered where the rectangle or the window is at hand, so those two read as zero here.
    /// </summary>
    private int EmptyNumber(ExecutableStep step, string name)
        => Empties(step, name) switch
        {
            ActionEmptyMeans.ScreenRefresh => Math.Max(1, _devices.Screen.RefreshMilliseconds),
            ActionEmptyMeans.Unset => Starting(step, name),
            _ => 0,
        };

    /// <summary>
    /// The number an action starts a field with, for a field nobody wrote. It is the same value a
    /// new step is given, so a hand-written step and one the editor made agree.
    /// </summary>
    private static int Starting(ExecutableStep step, string name)
    {
        var written = ActionSemanticsCatalog.Find(step.Type)?.Parameter(name)?.DefaultValue;
        return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value)
            ? (int)value
            : 0;
    }

    /// <summary>True when the step wrote anything at all into a number field.</summary>
    private static bool Given(ExecutableStep step, string name)
        => !Formula.IsNothing(step.Written(name));

    /// <summary>What a blank cell of this field means, from the catalogue.</summary>
    private static ActionEmptyMeans Empties(ExecutableStep step, string name)
        => ActionSemanticsCatalog.Find(step.Type)?.Parameter(name)?.EmptyMeans
            ?? ActionEmptyMeans.Zero;

    /// <summary>
    /// Bends a number by whatever slack the step asked for. Only lengths of time are written with
    /// one, so this is what stops a macro's waits from looking like a machine's: 500 written with
    /// 20% of give comes out anywhere from 400 to 600, a fresh number every time the step runs.
    /// </summary>
    private static int Vary(ExecutableStep step, string name, int value)
    {
        var jitter = Math.Min(1m, Math.Max(0m, step.Jitter(name)));
        if (jitter == 0m || value == 0)
        {
            return value;
        }

        var bend = 1 + (((System.Random.Shared.NextDouble() * 2) - 1) * (double)jitter);
        return (int)Math.Round(value * bend);
    }

}
