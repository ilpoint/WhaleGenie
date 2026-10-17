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
        // leaves a setting out gets its catalogue default, not an empty text that fails to parse.
        if (step.Text(name).Trim().Length == 0)
        {
            return Value.Null;
        }

        return ReadModeOf(step, name) switch
        {
            ActionReadMode.VariableName => ReadBareName(step.Text(name)),
            ActionReadMode.WholeValue => Read(step.Text(name)),
            _ => Value.FromText(ReadText(step, name)),
        };
    }

    /// <summary>
    /// Reads one parameter as text. Only a field the catalogue calls text has names filled in:
    /// a choice, a number or a name being written is handed over exactly as it stands.
    /// </summary>
    private string ReadText(ExecutableStep step, string name)
    {
        var written = step.Text(name);
        return ReadModeOf(step, name) == ActionReadMode.Text ? Interpolate(written) : written;
    }

    private static ActionReadMode ReadModeOf(ExecutableStep step, string name)
        => ActionSemanticsCatalog.Find(step.Type)?.Parameter(name)?.ReadMode ?? ActionReadMode.Text;

    /// <summary>
    /// A bare variable name, read from the store or left as the text it is when nothing defines
    /// it. A dollar sign written in front is accepted because the picker hands names back that
    /// way, but the reader itself writes the bare form.
    /// </summary>
    private Value ReadBareName(string text)
    {
        var name = text.Trim().TrimStart('$');
        if (name.Length == 0)
        {
            return Value.Null;
        }

        return Variables.TryGet(name, out var value) ? value : Value.FromText(name);
    }

    /// <summary>
    /// Reads text that stands for one value: an expression, a number or ordinary text. A bare
    /// name is text here; only a field that names a variable makes it a variable.
    /// </summary>
    private Value Read(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Value.Null;
        }

        // A doubled dollar is how a value field writes a literal dollar. It has to be answered
        // before the expression reader, or "$100 off" would be taken for an expression that has
        // to parse and would fail instead of becoming the text it was written as.
        if (trimmed.StartsWith("$$", StringComparison.Ordinal))
        {
            return Value.FromText(Interpolate(trimmed));
        }

        if (Expression.TryEvaluate(trimmed, Variables, out var value, out var error))
        {
            return value;
        }

        // Text that was written as an expression has to work, so a typo shows up as a
        // failure rather than quietly becoming a literal.
        if (LooksLikeExpression(trimmed))
        {
            throw error!;
        }

        // Numbers are read without a thousands separator: text like "10,20,30,40" or "1,000"
        // is text, not a number, and a comma means a separator rather than a digit group.
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return Value.FromNumber(number);
        }

        return Value.FromText(Interpolate(trimmed));
    }

    /// <summary>
    /// True when the text can only be meant as an expression: it opens with the variable sign, so
    /// the author was writing a value rather than words.
    /// </summary>
    /// <remarks>
    /// Brackets and parentheses used to count as the same sign, and they are exactly what a
    /// selector or a path is made of — <c>[data-testid="row-2"]</c>, <c>div &gt; p:nth-of-type(2)</c>,
    /// <c>C:\Program Files (x86)\a.log</c> — so a value that was plainly one of those came back as
    /// "not a valid expression" instead of being used. A "$" at the front is the one opening that
    /// cannot be anything else; a "$" in the middle is left alone, because a written-out dollar
    /// sign looks like that too.
    /// </remarks>
    private static bool LooksLikeExpression(string text) => text[0] == '$';

    /// <summary>Fills in every known <c>$name</c>; <c>$$</c> stands for one literal dollar.</summary>
    private string Interpolate(string text)
        => VariableText.Replace(text,
            name => Variables.TryGet(name, out var value) ? value.AsText() : null);

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

        return Vary(step, name, (int)value.AsNumber());
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
        => step.Text(name).Trim().Length > 0;

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
