using System;
using System.Collections.Generic;
using System.Globalization;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One step's fields, read the way the catalogue says they are written: a name stays a name, a
/// value is worked out where it stands, a field holding text has the names inside it filled in, and
/// a number keeps to the range the field declares.
/// </summary>
/// <remarks>
/// It is the reading rules rather than the step, and it is handed to an action rather than reached
/// through the runner, so an action family can be read (and later run) without knowing anything else
/// about the run it is in. The two things it needs are the table the names are looked up in and how
/// long a screen refresh takes; both are given once when it is made.
/// </remarks>
internal readonly struct StepArguments
{
    private readonly ExecutableStep _step;
    private readonly VariableStore _variables;
    private readonly int _refreshMilliseconds;
    private readonly EvaluationContext _evaluation;

    public StepArguments(ExecutableStep step, VariableStore variables, int refreshMilliseconds,
        EvaluationContext evaluation)
    {
        _step = step;
        _variables = variables;
        _refreshMilliseconds = refreshMilliseconds;
        _evaluation = evaluation ?? EvaluationContext.System;
    }

    /// <summary>
    /// Reads one parameter the way its catalogue entry says it is read. A field that names a
    /// variable takes the bare name; a field that holds one value takes an expression or a
    /// literal; a field that holds text has the names in it filled in where they stand.
    /// </summary>
    public Value ValueOf(string name)
    {
        // A field nobody answered is "nothing was said" in every read mode. A number step that
        // leaves a setting out gets its catalogue default, not an empty value that fails to parse.
        var written = _step.Written(name);
        if (Formula.IsNothing(written))
        {
            return Value.Null;
        }

        return ValueFormOf(name) switch
        {
            ActionValueForm.Name => ReadName(written),
            ActionValueForm.Value => Read(written),
            _ => Value.FromText(Text(name)),
        };
    }

    /// <summary>
    /// Reads one parameter as text. A field that names a variable answers with the name it writes;
    /// every other field answers with what it comes to, worked out rather than written back into a
    /// line and read again.
    /// </summary>
    public string Text(string name)
        => ValueFormOf(name) switch
        {
            ActionValueForm.Name => Written(name),
            _ => Read(_step.Written(name)).AsText(),
        };

    /// <summary>
    /// The name a field writes. A name written into a step is not read through the formula writer,
    /// because that writer puts corner marks around a bare name; the shape says which name it is.
    /// </summary>
    public string Written(string name) => Formula.NameOf(_step.Written(name));

    /// <summary>
    /// A number a step names, read the way every other field is: as a value. A field nobody
    /// answered takes whatever the catalogue says an empty cell means, so "zero", "the edge of the
    /// screen" and "the value the action starts with" are decided in one place rather than at each
    /// call. A value that cannot be worked out is left as the broken expression it is, so the step
    /// reports <c>Run.BadExpression</c> like a text field would — swallowing it into a zero is how
    /// a click meant for something that was found ends up at the corner of the screen, with
    /// nothing anywhere saying why.
    /// </summary>
    public int Number(string name)
    {
        if (!Given(name))
        {
            return EmptyNumber(name);
        }

        var value = ValueOf(name);

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
        if (ActionSemanticsCatalog.Find(_step.Type)?.Parameter(name) is { Shape: ActionValueShape.Number } facts
            && (number < facts.MinimumValue || number > facts.MaximumValue))
        {
            throw new StepFailure(RunKeys.OutOfRange,
                name + ": " + number.ToString(CultureInfo.InvariantCulture));
        }

        return Vary(name, number);
    }

    /// <summary>True when the step wrote anything at all into a field.</summary>
    public bool Given(string name) => !Formula.IsNothing(_step.Written(name));

    /// <summary>
    /// What a blank cell comes to, from the catalogue. "To the edge" and "keep what the window has"
    /// are answered where the rectangle or the window is at hand, so those two read as zero here.
    /// </summary>
    private int EmptyNumber(string name)
        => EmptyMeans(name) switch
        {
            ActionEmptyMeans.ScreenRefresh => Math.Max(1, _refreshMilliseconds),
            ActionEmptyMeans.Unset => Starting(name),
            _ => 0,
        };

    /// <summary>
    /// The number an action starts a field with, for a field nobody wrote. It is the same value a
    /// new step is given, so a hand-written step and one the editor made agree.
    /// </summary>
    private int Starting(string name)
    {
        var written = ActionSemanticsCatalog.Find(_step.Type)?.Parameter(name)?.DefaultValue;
        return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value)
            ? (int)value
            : 0;
    }

    /// <summary>What a blank cell of this field means, from the catalogue.</summary>
    public ActionEmptyMeans EmptyMeans(string name)
        => ActionSemanticsCatalog.Find(_step.Type)?.Parameter(name)?.EmptyMeans
            ?? ActionEmptyMeans.Zero;

    /// <summary>
    /// Bends a number by whatever slack the step asked for. Only lengths of time are written with
    /// one, so this is what stops a macro's waits from looking like a machine's: 500 written with
    /// 20% of give comes out anywhere from 400 to 600, a fresh number every time the step runs.
    /// </summary>
    private int Vary(string name, int value)
    {
        var jitter = Math.Min(1m, Math.Max(0m, _step.Jitter(name)));
        if (jitter == 0m || value == 0)
        {
            return value;
        }

        var bend = 1 + (((Random.Shared.NextDouble() * 2) - 1) * (double)jitter);
        return (int)Math.Round(value * bend);
    }

    private ActionValueForm ValueFormOf(string name)
        => ActionSemanticsCatalog.Find(_step.Type)?.Parameter(name)?.ValueForm ?? ActionValueForm.Text;

    /// <summary>
    /// A field that names a variable rather than holding one: the name is looked up, and what it
    /// holds is the value. A name nothing defines stays the text it was written as, so a step that
    /// waits on a handle no run ever set reports that rather than failing to resolve.
    /// </summary>
    private Value ReadName(ExpressionNode written)
    {
        var name = Formula.NameOf(written);
        if (name.Length == 0)
        {
            return Value.Null;
        }

        return _variables.TryGet(name, out var held) ? held : Value.FromText(name);
    }

    /// <summary>
    /// The keys of one written combination, such as the Ctrl+Shift+S a shortcut field or a row of a
    /// sequence holds. Every place that presses a combination reads it here, so the way it is
    /// written is decided once.
    /// </summary>
    public static IReadOnlyList<string> Chords(string written)
        => [.. written.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>What a shape comes to, with the names in it worked out where they stand.</summary>
    private Value Read(ExpressionNode written)
        => Expression.Evaluate(written, _variables, _evaluation);
}
