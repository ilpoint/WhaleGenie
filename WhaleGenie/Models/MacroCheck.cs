using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// Something about a step that the editor can tell is wrong before the macro runs, together with
/// the step it came from so the list can point at the row rather than leaving the user to find it.
/// </summary>
/// <summary>
/// One thing wrong with a step. <paramref name="Code"/> is the stable name of the problem — the
/// part a program branches on — and <paramref name="Message"/> is the sentence a person reads.
/// A client that has to say "this is the same problem as last time" cannot do it by comparing
/// sentences, because they are translated.
/// </summary>
public sealed record MacroProblem(
    MacroStep Step, string Code, string Message, MacroProblemSeverity Severity);

/// <summary>
/// How much a problem matters. There are only two answers on purpose: a step either cannot work, or
/// it is worth reading before a run. Anything finer would be a judgement the check is not in a
/// position to make.
/// </summary>
public enum MacroProblemSeverity
{
    /// <summary>Fails the moment the step runs, with no way for the author to have meant it.</summary>
    Blocking,

    /// <summary>Worth reading before a run, but a macro may be written this way on purpose.</summary>
    Hint,
}

/// <summary>
/// Reads a macro the way the engine would and reports what cannot work, while the user is still
/// looking at the steps. Only what the steps themselves decide is reported: what a variable holds,
/// whether a file is there, what is on screen and which program is running are all things only a
/// run can find out, and guessing at them would teach the user to ignore the marks.
/// </summary>
public static partial class MacroCheck
{
    /// <summary>The formula function that reads one field of a record, as the reader spells it.</summary>
    private const string FieldFunction = "field";

    /// <summary>
    /// The step problems that stop a macro from working at all. Everything else is a hint: a shadowed
    /// loop variable, two macros that both send input, a variable nothing has defined yet — all of
    /// them can be deliberate, and the person writing the macro may know something the check does not.
    /// The two consumers used to disagree about this: the editor drew every problem as a hint while
    /// the MCP side refused to apply any of them.
    /// </summary>
    private static readonly HashSet<string> Blocking = new(StringComparer.Ordinal)
    {
        // The step names something this build does not have, or leaves a required field empty.
        MacroProblemCode.UnknownAction, MacroProblemCode.UnknownParameter, MacroProblemCode.MissingField, MacroProblemCode.MissingCondition, MacroProblemCode.BadOption,
        MacroProblemCode.NoPlace, MacroProblemCode.PlaceAndNumbers, MacroProblemCode.WindowBounds,

        // A formula or a record the engine cannot read.
        MacroProblemCode.BadExpression, MacroProblemCode.UnknownField, MacroProblemCode.NotARecord, MacroProblemCode.RecordWhereNumber, MacroProblemCode.RecordInText,

        // A child list whose rows do not fit its columns.
        MacroProblemCode.UnknownRowColumn, MacroProblemCode.MissingRowColumn, MacroProblemCode.ExclusiveRowColumn,

        // A call whose arguments or results do not fit the macro being called.
        MacroProblemCode.MacroNotFound, MacroProblemCode.NoMacro, MacroProblemCode.MissingArgument, MacroProblemCode.UndeclaredArgument, MacroProblemCode.DuplicateArgument,
        MacroProblemCode.ArgumentType, MacroProblemCode.GivenWithValue, MacroProblemCode.BadResultName, MacroProblemCode.DuplicateResultKey,
        MacroProblemCode.DuplicateResultName, MacroProblemCode.UndeclaredResult, MacroProblemCode.UndeclaredActionResult, MacroProblemCode.BadUserFailureKey,

        // A name the engine will refuse to write, or a loop that names a counter it is not inside.
        MacroProblemCode.ReservedVariable, MacroProblemCode.GlobalMustExist, MacroProblemCode.LoopIndexOutsideLoop,

        // A step standing where the engine will not take it, or a jump out of a loop it is not in.
        MacroProblemCode.WrongStepKind, MacroProblemCode.CaseOutsideSwitch, MacroProblemCode.HiddenStep, MacroProblemCode.BreakOutsideLoop, MacroProblemCode.ContinueOutsideLoop,
    };

    private static bool CallsMacro(MacroStep step)
        => step.Definition?.Semantics.SignatureSource is ActionSignatureSource.CalledMacro;

    /// <summary>
    /// The rows of a call's argument list, in the order they were written. The names a row may hold
    /// belong to the called macro rather than to the catalogue, so this list is the only place a
    /// check can learn which argument a step fills in, and the only place an argument that the
    /// called macro no longer declares can be seen sitting in a step.
    /// </summary>
    private static IEnumerable<StepParameterRow> ArgumentRows(MacroStep step)
        => step.Parameters
            .Where(parameter => parameter.Name == FieldNames.Arguments)
            .SelectMany(parameter => parameter.Rows);

    /// <summary>What one row of a call's argument list says, read the way the engine reads it.</summary>
    private static (string Name, string Value, bool Given) Argument(StepParameterRow row)
        => (row.Text(FieldNames.Name).Trim(),
            row.Text(FieldNames.Value).Trim(),
            string.Equals(row.Text(FieldNames.Given).Trim(), "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>The steps that give a break or a continue something to act on.</summary>
    private static readonly HashSet<string> LoopKeys = new(StringComparer.Ordinal)
    {
        "control.repeat",
        "control.while",
        "control.forEach",
        "control.for",
    };

    /// <summary>The steps that cannot run without a condition chosen in their form.</summary>
    private static readonly HashSet<string> ConditionKeys = new(StringComparer.Ordinal)
    {
        "control.if",
        "control.while",
        "control.waitUntil",
    };

    /// <summary>
    /// The steps whose body is a place a recursive call can stop at: a branch that may not be
    /// taken, a case that may not match, or a loop that may run no round at all. A call outside
    /// all of them has nothing to end the recursion.
    /// </summary>
    private static readonly HashSet<string> GuardKeys = new(StringComparer.Ordinal)
    {
        "control.if",
        "control.switch",
        "control.repeat",
        "control.while",
        "control.forEach",
        "control.for",
    };

    /// <summary>
    /// The fields one place or rectangle is answered by: the record in the first, or the numbers
    /// in the rest. One of the two has to be written down.
    /// </summary>
    private static readonly (string Place, string[] Numbers)[] PlaceGroups =
    [
        ("place", ["x", "y"]),
        ("rect", ["x", "y", FieldNames.Width, FieldNames.Height]),
        ("startPlace", ["startX", "startY"]),
        ("endPlace", ["endX", "endY"]),
    ];

    /// <summary>
    /// What is wrong with the macro's own declarations in a way that makes a call impossible: a
    /// name left empty, one the macro language cannot read, one that collides with the supplied-
    /// empty marker, one written twice, one the run keeps, or a required argument that also has a
    /// default. The step list has no row to put these on, so the editor shows them above it, and
    /// the save button refuses to write the macro.
    /// </summary>
    public static IReadOnlyList<string> DeclarationFaults(MacroCallable own)
        => [.. DeclarationFaultProblems(own).Select(problem => problem.Message)];

    /// <summary>
    /// The same faults, each with the name a program can branch on. The code carries the fault
    /// rather than the sentence: a client told "RequiredWithDefault" can tell the author which of
    /// the two to drop, while the sentence is only ever in one language.
    /// </summary>
    public static IReadOnlyList<(string Code, string Message)> DeclarationFaultProblems(MacroCallable own)
        => [.. own.Problems().Select(problem =>
            ($"Declaration.{problem.Fault}", FaultMessage(own, problem)))];

    /// <summary>
    /// Everything the declaration editor has to say: the fatal faults above, plus warnings about
    /// results that no step writes and defaults that look wrong for their declared kind. A warning
    /// is worth showing while the macro is still being written, but it does not block saving.
    /// </summary>
    public static IReadOnlyList<string> DeclarationProblems(MacroCallable own)
        => [.. DeclarationFaults(own), .. DeclarationWarnings(own)];

    /// <summary>
    /// The same warnings on their own. An external author is told what blocks the write and what is
    /// only worth reading apart from each other, the way the editor draws them.
    /// </summary>
    public static IReadOnlyList<string> DeclarationWarnings(MacroCallable own)
    {
        var warnings = new List<string>();
        var open = WritesAnyName(own.OwnSteps);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Every(own.OwnSteps))
        {
            step.CollectVariables(written);
        }

        // A script, a file of variables or a computed variable name can write a result the steps
        // do not mention by name. In that case saying "nothing writes this" would be a guess.
        foreach (var result in own.Results)
        {
            var name = result.Name.Trim();
            if (!VariableName.IsName(name)
                || own.Parameter(name) is not null
                || open
                || written.Contains(name))
            {
                continue;
            }

            warnings.Add(Strings.Format("Editor.Problem.ResultNeverWritten", own.Name, name));
        }

        // A default is read as one value at the call site. A constant that plainly cannot be the
        // declared kind would fail every call that leaves the field empty, so it is worth saying
        // while the interface is being written instead of at the first call.
        foreach (var parameter in own.Parameters)
        {
            var name = parameter.Name.Trim();
            if (parameter.Required || name.Length == 0 || !VariableName.IsName(name))
            {
                continue;
            }

            // A default that starts with $ is read as an expression, the same as any whole-value
            // field. A broken one would only fail the calls that leave the parameter out, so it is
            // better to point at it while the interface is being written.
            var defaultText = parameter.Default.Trim();
            if (defaultText.StartsWith('$') && !defaultText.StartsWith("$$", StringComparison.Ordinal)
                && !Expression.TryParse(defaultText, out _, out var error))
            {
                warnings.Add(Strings.Format("Editor.Problem.BadExpression", error?.Detail ?? defaultText));
            }

            if (ConstantValue(parameter.Default) is { } value
                && !MacroValues.Fits(parameter.Type, value))
            {
                warnings.Add(Strings.Format("Editor.Problem.BadDefaultType", own.Name, name,
                    Strings.Get("ActionType." + parameter.Type, parameter.Type.ToString())));
            }
        }

        // An initial value is read at the start of every run, so one that cannot be read — or that
        // plainly cannot be the kind it declares — fails the macro before any step has done
        // anything. That is worth saying while the declaration is being written.
        foreach (var local in own.OwnLocals)
        {
            var name = local.Name.Trim();
            if (name.Length == 0 || !VariableName.IsName(name))
            {
                continue;
            }

            var initial = local.Initial.Trim();
            if (initial.StartsWith('$') && !initial.StartsWith("$$", StringComparison.Ordinal)
                && !Expression.TryParse(initial, out _, out var error))
            {
                warnings.Add(Strings.Format("Editor.Problem.BadExpression", error?.Detail ?? initial));
            }

            if (ConstantValue(local.Initial) is { } value && !MacroValues.Fits(local.Type, value))
            {
                warnings.Add(Strings.Format("Editor.Problem.BadInitialType", own.Name, name,
                    Strings.Get("ActionType." + local.Type, local.Type.ToString())));
            }
        }

        return warnings;
    }

    /// <summary>
    /// What a constant value field reads as, or null when it depends on a variable or a function
    /// whose answer is only known while the macro runs. It follows the same rules as the engine's
    /// value reader for the cases a check can settle on its own.
    /// </summary>
    private static Value? ConstantValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0
            || trimmed.StartsWith('$')
            || Expression.ReferencedNames(trimmed).Count > 0)
        {
            return null;
        }

        if (Expression.TryEvaluate(trimmed, null, out var value, out _))
        {
            return value;
        }

        // Text that is not an expression is still a value the engine can read; a number is read as
        // a number, and everything else stays text.
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var number)
            ? Value.FromNumber(number)
            : Value.FromText(trimmed);
    }

    /// <summary>
    /// True when the steps can write a name nobody can see in advance: a file of variables, one of
    /// the scripts that runs with the macro in reach, or a variable-name field whose value is
    /// itself computed. A static check has to stop guessing at that point.
    /// </summary>
    private static bool WritesAnyName(IEnumerable<MacroStep> steps)
    {
        foreach (var step in Every(steps))
        {
            if (step.Type is "file.loadVariables" or "script.python" or "script.csharp"
                or "script.ahk")
            {
                return true;
            }

            foreach (var parameter in step.Parameters)
            {
                if (step.DefinesVariable(parameter)
                    && parameter.Value.Trim().StartsWith('$'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string FaultMessage(MacroCallable own,
        (MacroSignatureFault Fault, string Name) problem) => problem.Fault switch
        {
            MacroSignatureFault.EmptyName =>
                Strings.Format("Editor.Problem.EmptyDeclaration", own.Name),
            MacroSignatureFault.InvalidName =>
                Strings.Format("Editor.Problem.InvalidDeclarationName", own.Name, problem.Name),
            MacroSignatureFault.RequiredWithDefault =>
                Strings.Format("Editor.Problem.RequiredWithDefault", own.Name, problem.Name),
            _ => Strings.Format("Editor.Problem.BadDeclaration", own.Name, problem.Name),
        };

    /// <summary>
    /// How many calls in the project no longer fit the declaration being edited, and which macros
    /// they live in. The declarations are edited here, but the calls that would break are in other
    /// macros; saying so while the name is changed is what keeps a rename from becoming a hunt.
    /// Nothing is rewritten: each caller still has to be opened and fixed by its author.
    /// </summary>
    public static string CallImpact(MacroCallable own, IEnumerable<MacroCallable> macros)
    {
        var ownName = own.Name.Trim();
        if (ownName.Length == 0)
        {
            return string.Empty;
        }

        var callers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var calls = 0;
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The macro being edited is not in the project list handed in, and a macro may call
        // itself, so it is included explicitly. A duplicate name is not scanned twice.
        foreach (var macro in macros.Append(own))
        {
            if (!scanned.Add(macro.Name.Trim()))
            {
                continue;
            }

            foreach (var step in Every(macro.OwnSteps))
            {
                if (!CallsMacro(step)
                    || !string.Equals(step.Text("macro").Trim(), ownName,
                        StringComparison.OrdinalIgnoreCase)
                    || !CallMismatch(step, own))
                {
                    continue;
                }

                calls++;
                callers.Add(macro.Name.Trim().Length == 0
                    ? Strings.Get("Common.None")
                    : macro.Name.Trim());
            }
        }

        return calls == 0
            ? string.Empty
            : Strings.Format("Editor.Problem.CallImpact", calls, string.Join("、", callers));
    }

    /// <summary>
    /// True when a call no longer says what the declaration asks for: a required argument is not
    /// filled in, an argument or `.given` marker is not declared, or a result the call asks for is
    /// not offered. Types are left to the call-step check; this summary is about names and presence.
    /// </summary>
    private static bool CallMismatch(MacroStep step, MacroCallable own)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ArgumentRows(step))
        {
            var (name, _, _) = Argument(row);
            if (name.Length == 0)
            {
                continue;
            }

            // A row naming something the declaration does not have, or a second row naming the same
            // argument, is a call that no longer says what it was written to say.
            if (own.Parameter(name) is null || !named.Add(name))
            {
                return true;
            }
        }

        foreach (var argument in CallableSignature.Arguments(own))
        {
            if (argument.Required && !named.Contains(argument.Name))
            {
                return true;
            }
        }

        foreach (var (result, _) in Wanted(step))
        {
            if (!own.Offers(result))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Everything wrong with the macro, in the order the steps are written.
    /// <paramref name="macros"/> is every macro a call can reach — the project list, each with what
    /// it declares — and <paramref name="own"/> is the macro being edited, which may call itself.
    /// </summary>
    /// <param name="browserShared">
    /// True when another macro in the same project also uses the browser. There is one browser and
    /// one current page for the whole program, so two runs take turns there; a tab one changes is
    /// the page the other reads next.
    /// </param>
    public static IReadOnlyList<MacroProblem> Inspect(IEnumerable<MacroStep> steps,
        MacroCallable? own, IEnumerable<MacroCallable> macros, bool browserShared = false) =>
        new Inspection(steps, own, macros, browserShared).Run();

}
