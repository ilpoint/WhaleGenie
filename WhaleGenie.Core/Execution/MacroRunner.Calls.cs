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
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// Calling another macro: finding it, reading its arguments and seeding what it starts with.
public sealed partial class MacroRunner
{
    /// <summary>
    /// What a call names, what the called macro declares, and the values it is handed. The
    /// arguments are read while the caller's values are still the ones in force, and the names the
    /// call asks to have back are checked against the called macro's declaration before anything
    /// runs: a name it never offered is a mistake in the call, said now rather than after a
    /// half-finished run.
    /// </summary>
    private (string Name, IReadOnlyList<ExecutableStep> Steps, MacroSignature Signature,
        List<(string Result, string Name)> Wanted, VariableBag Called) ResolveCall(ExecutableStep step)
    {
        var name = ReadValue(step, "macro").AsText().Trim();
        var steps = name.Length == 0 ? null : _macros.Steps(name);
        if (steps is null)
        {
            throw new StepFailure(RunKeys.MacroNotFound, name);
        }

        if (_run.CallDepth >= CallDepthLimit)
        {
            throw new StepFailure(RunKeys.MacroTooDeep, name);
        }

        var signature = _macros.Signature(name) ?? MacroSignature.Empty;
        if (signature.Problem() is { } problem)
        {
            throw new StepFailure(RunKeys.BadDeclaration, problem.Name);
        }

        var wanted = WantedResults(step, signature);
        var supplied = ReadArguments(step, signature);

        // A macro's own values are its own, so a call says what goes in and what comes back rather
        // than leaning on whatever happened to be left lying around.
        var called = new VariableBag();

        // A declared result is empty until this attempt writes it. It is put in first, and the
        // arguments after it, so an in-out name keeps the value that was just passed in while a
        // result nobody wrote reads as empty rather than as the last attempt's answer.
        foreach (var result in signature.Results)
        {
            var resultName = result.Name.Trim();
            // Declared, but not given a type: a result is judged when it is handed back, by the
            // key the failure table already names for it. Checking it here would report the same
            // mistake twice and with the less specific of the two keys.
            called.Declare(resultName, result.Type);
            if (resultName.Length > 0)
            {
                called.Set(resultName, Value.Null);
            }
        }

        foreach (var argument in signature.Arguments)
        {
            var argumentName = argument.Name.Trim();
            supplied.TryGetValue(argumentName, out var input);
            // A declaration of the called macro, so a second one claiming the same name is refused
            // rather than quietly sharing the slot.
            called.Declare(argumentName, argument.Type);
            called.Set(argumentName, ReadArgument(argument, input?.Value ?? Formula.Nothing,
                input?.Given ?? false));
        }

        // The name of the macro that is running right now, which is what a macro writing a log line
        // or naming a file means by it.
        called.Set("sys.macroName", Value.FromText(name));
        return (name, steps, signature, wanted, called);
    }

    /// <summary>
    /// Reads a call's argument list once: which argument each row names, and what that row gives
    /// it. A call is written the same way as its results are asked for — one row per name — so the
    /// names and the values sit in the same place a reader already looks for the results.
    /// A row naming an argument the called macro does not declare, or a second row naming one that
    /// was already named, is the mistake the declaration exists to catch: it stops the call before
    /// the called macro runs, because either one would otherwise leave a value with nowhere to go or
    /// silently drop the one that was written first. A row with no name is a row the editor added
    /// and left empty, so it passes nothing and the argument it might have filled takes its default.
    /// </summary>
    private static Dictionary<string, CallArgument> ReadArguments(
        ExecutableStep step, MacroSignature signature)
    {
        var supplied = new Dictionary<string, CallArgument>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in step.Rows(FieldNames.Arguments))
        {
            var name = Word(row, FieldNames.Name);
            if (name.Length == 0)
            {
                continue;
            }

            if (signature.Argument(name) is null)
            {
                throw new StepFailure(RunKeys.UndeclaredArgument, name);
            }

            var value = row.GetValueOrDefault(FieldNames.Value) ?? Formula.Nothing;
            var given = string.Equals(Word(row, FieldNames.Given), "true",
                StringComparison.OrdinalIgnoreCase);
            if (!supplied.TryAdd(name, new CallArgument(value, given)))
            {
                throw new StepFailure(RunKeys.DuplicateArgument, name);
            }
        }

        return supplied;
    }

    /// <summary>What one row of a call's argument list says about the argument it names.</summary>
    private sealed record CallArgument(ExpressionNode Value, bool Given);

    /// <summary>
    /// Fills the run's own table from the declaration before the first step. A top-level run has no
    /// caller to read arguments from, so the same rule is used against the values the debug window
    /// supplied; a trigger supplies none and the defaults apply. A required value with no default
    /// stops the run here, before any step has done anything.
    /// </summary>
    private StepFailure? SeedStart(MacroSignature signature)
    {
        if (signature.Problem() is { } problem)
        {
            return new StepFailure(RunKeys.BadDeclaration, problem.Name);
        }

        var supplied = new Dictionary<string, MacroStartArgument>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in StartArguments ?? [])
        {
            var name = argument.Name.Trim();
            if (name.Length > 0)
            {
                supplied[name] = argument;
            }
        }

        // Every name this run declares is declared again, so a value a previous run left under the
        // same name is judged against this run's kind rather than the last one's.
        foreach (var argument in signature.Arguments)
        {
            Variables.Local.Declare(argument.Name.Trim(), argument.Type);
        }

        foreach (var result in signature.Results)
        {
            Variables.Local.Declare(result.Name.Trim(), result.Type);
        }

        try
        {
            var values = new List<(string Name, Value Value)>();
            foreach (var argument in signature.Arguments)
            {
                var name = argument.Name.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                var input = supplied.TryGetValue(name, out var found) ? found : null;
                // A run started from the dialog supplies lines a person typed, so this is one of
                // the few places a line becomes a value: a field of a step already is one.
                var written = input is null
                    ? Formula.Nothing
                    : Formula.Text(input.Text);
                var explicitlyEmpty = input?.Given ?? false;
                var value = ReadArgument(argument, written, explicitlyEmpty);
                values.Add((name, value));
            }

            // A result is empty until this attempt writes it — never the value the last attempt
            // left there. A name that is also an argument takes the argument written below.
            foreach (var result in signature.Results)
            {
                var name = result.Name.Trim();
                if (name.Length > 0)
                {
                    Variables.Local.Set(name, Value.Null);
                }
            }

            foreach (var (name, value) in values)
            {
                Variables.Local.Set(name, value);
            }

            // The macro's own declarations, in the order they were written: an initial value may
            // read a parameter, a built-in, or a declaration above it.
            SeedLocals(signature);
            return null;
        }
        catch (StepFailure failure)
        {
            return failure;
        }
        catch (ExpressionException failure)
        {
            return new StepFailure(RunKeys.BadExpression, failure.Detail);
        }
    }

    /// <summary>
    /// Writes the values a macro declares for itself into the table that is running, in the order
    /// they were written: an initial value may read a parameter, a built-in, or a declaration above
    /// it. They are set rather than filled in, because an initial value is where a run starts —
    /// not a fallback for a name that happens to be missing.
    /// </summary>
    private void SeedLocals(MacroSignature signature)
    {
        foreach (var local in signature.Locals)
        {
            var name = local.Name.Trim();
            Variables.Local.Declare(name, local.Type);
            if (name.Length > 0)
            {
                Variables.Local.Set(name, ReadDeclared(name, local.Initial, local.Type));
            }
        }
    }

    /// <summary>
    /// One argument value, read the same way whether it came from a call step or from the debug
    /// window: <c>n + 1</c> passes a number and <c>list</c> passes the list itself rather than its
    /// text, because the value cell is read like every other value field. A cell that is present but
    /// empty has not supplied anything: only the supplied-empty marker turns it into a value. A
    /// value that was not supplied takes the default, or fails when the declaration says it is
    /// required.
    /// </summary>
    private Value ReadArgument(MacroArgument argument, ExpressionNode written, bool given)
    {
        // Two answers to one question: the field says a value, the marker beside it says no value.
        // Picking one would be guessing which the author meant, so neither is used.
        if (given && !Formula.IsNothing(written))
        {
            throw new StepFailure(RunKeys.GivenWithValue, argument.Name);
        }

        if (Formula.IsNothing(written) && !given)
        {
            if (argument.Required)
            {
                throw new StepFailure(RunKeys.MissingArgument, argument.Name);
            }

            var fallback = Formula.IsNothing(argument.Default) ? Value.Null : Read(argument.Default);
            CheckValueFits(argument.Type, fallback, argument.Name, RunKeys.BadArgumentType);
            return fallback;
        }

        var value = Formula.IsNothing(written) ? Value.FromText(string.Empty) : Read(written);
        CheckValueFits(argument.Type, value, argument.Name, RunKeys.BadArgumentType);
        return value;
    }

    /// <summary>
    /// A value a macro declares for itself, read at the moment the macro starts. An empty cell is
    /// the kind's empty value rather than nothing at all: a number nobody filled in adds up from
    /// zero, which is what the editor's preview of the same declaration shows. An expression that
    /// cannot be read is raised the way any other bad value is, so the failure names the same key
    /// wherever the value was written.
    /// </summary>
    private Value ReadDeclared(string name, ExpressionNode initial, ValueKind type)
    {
        if (Formula.IsNothing(initial))
        {
            return ValueRules.Empty(type);
        }

        var value = Read(initial);
        CheckValueFits(type, value, name, RunKeys.BadInitial);
        return value;
    }

    /// <summary>
    /// A region row says where to look in one of two ways: a name, or four numbers. Both at once
    /// would be two answers to one question, and the reader would have to guess which one wins.
    /// </summary>
    private static bool HasTextAndNumbers(IReadOnlyDictionary<string, ExpressionNode> row)
        => Said(row, "text")
            && new[] { "x", "y", "width", "height" }.Any(column => Said(row, column));
    /// <summary>
    /// Checks a row list against the columns its field declares. A column nobody reads would be
    /// skipped in silence, which looks like the action ignoring what the row said; one the field
    /// requires but the row leaves out is a row that cannot say what it was meant to say.
    /// </summary>
    private static void CheckRowColumns(ActionParameterSemantics facts,
        IReadOnlyList<IReadOnlyDictionary<string, ExpressionNode>> rows)
    {
        foreach (var row in rows)
        {
            foreach (var column in row.Keys)
            {
                if (!facts.Columns.Any(known =>
                        string.Equals(known.Name, column, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new StepFailure(RunKeys.BadRowColumn, facts.Name + "." + column);
                }
            }

            if (string.Equals(facts.Name, "region", StringComparison.Ordinal)
                && HasTextAndNumbers(row))
            {
                throw new StepFailure(RunKeys.BadRowColumn, facts.Name + ".text");
            }
            foreach (var column in facts.Columns.Where(column => column.Required))
            {
                if (!row.Keys.Any(written =>
                        string.Equals(written, column.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new StepFailure(RunKeys.BadRowColumn, facts.Name + "." + column.Name);
                }
            }
        }
    }
    /// <summary>
    /// A declared kind is a promise the call makes; a value of another kind would reach the called
    /// macro as something its own steps cannot use, so it is refused where the promise is checked.
    /// Text accepts anything because every value has a text reading; an empty value is allowed for
    /// any kind because a result that was not handed over is empty rather than wrong.
    /// </summary>
    private static void CheckValueFits(ValueKind declared, Value value, string name, string key)
    {
        if (!ValueRules.Accepts(declared, value))
        {
            throw new StepFailure(key, name + ": " + value.Kind);
        }
    }

    /// <summary>
    /// The results a call asks to have back, in the order it asked: one per row of the step's
    /// <c>returns</c>, each row carrying the called macro's own name for the value and the name the
    /// caller wants it under. A name the called macro does not offer is a mistake in the call.
    /// </summary>
    private static List<(string Result, string Name)> WantedResults(ExecutableStep step,
        MacroSignature signature)
    {
        var wanted = new List<(string Result, string Name)>();
        foreach (var row in step.Rows("returns"))
        {
            var result = Word(row, FieldNames.Key);
            var name = row.TryGetValue(FieldNames.Name, out var written)
                ? Formula.NameOf(written)
                : string.Empty;
            if (result.Length == 0 || name.Length == 0)
            {
                continue;
            }

            if (!signature.Offers(result))
            {
                throw new StepFailure(RunKeys.UndeclaredResult, result);
            }

            wanted.Add((result, name));
        }

        return wanted;
    }

    /// <summary>
    /// Copies the values a call asked to get back into the caller's own table. A name the called
    /// macro offers but never set comes back empty rather than missing, so the step after the call
    /// reads this call's answer and not whatever an earlier pass left behind.
    /// </summary>
    private void Take(List<(string Result, string Name)> wanted, VariableBag produced)
    {
        foreach (var (result, name) in wanted)
        {
            Variables.Local.Set(name,
                produced.TryGet(result, out var value) && value is not null ? value : Value.Null);
        }
    }

    /// <summary>
    /// Checks what a finished run is about to hand over against the kinds its signature declared.
    /// A result it never wrote is empty rather than wrong, so that one is left to the caller.
    /// </summary>
    private static void CheckResults(MacroSignature signature,
        List<(string Result, string Name)> wanted, VariableBag produced)
    {
        foreach (var (result, _) in wanted)
        {
            if (signature.Result(result) is not { } declared)
            {
                continue;
            }

            var value = produced.TryGet(result, out var written) && written is not null
                ? written
                : Value.Null;
            CheckValueFits(declared.Type, value, result, RunKeys.VariableTypeMismatch);
        }
    }
    /// <summary>The entries of a field written one to a line, whichever line ending was used.</summary>
    private static string[] Lines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
