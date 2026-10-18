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
public sealed record MacroProblem(MacroStep Step, string Message);

/// <summary>
/// Reads a macro the way the engine would and reports what cannot work, while the user is still
/// looking at the steps. Only what the steps themselves decide is reported: what a variable holds,
/// whether a file is there, what is on screen and which program is running are all things only a
/// run can find out, and guessing at them would teach the user to ignore the marks.
/// </summary>
public static class MacroCheck
{
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
        => [.. own.Problems().Select(problem => FaultMessage(own, problem))];

    /// <summary>
    /// Everything the declaration editor has to say: the fatal faults above, plus warnings about
    /// results that no step writes and defaults that look wrong for their declared kind. A warning
    /// is worth showing while the macro is still being written, but it does not block saving.
    /// </summary>
    public static IReadOnlyList<string> DeclarationProblems(MacroCallable own)
        => [.. DeclarationFaults(own), .. DeclarationWarnings(own)];

    private static IReadOnlyList<string> DeclarationWarnings(MacroCallable own)
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
            if (!VariableText.IsName(name)
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
            if (parameter.Required || name.Length == 0 || !VariableText.IsName(name))
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
            MacroSignatureFault.ConflictingName =>
                Strings.Format("Editor.Problem.ConflictingDeclarationName", own.Name, problem.Name),
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
                if (step.Type is not ("control.runMacro" or "control.startMacro")
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
        foreach (var parameter in own.Parameters)
        {
            var name = parameter.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var supplied = step.Text(MacroCallFields.Argument(name)).Trim().Length > 0
                || string.Equals(step.Text(MacroCallFields.Given(name)).Trim(), "true",
                    StringComparison.OrdinalIgnoreCase);
            if (!supplied && parameter.Required)
            {
                return true;
            }
        }

        foreach (var parameter in step.Parameters)
        {
            if (MacroCallFields.IsGiven(parameter.Name))
            {
                var given = parameter.Name[MacroCallFields.ArgumentPrefix.Length..^MacroCallFields.GivenSuffix.Length];
                if (own.Parameter(given) is null)
                {
                    return true;
                }

                continue;
            }

            if (!parameter.Name.StartsWith(MacroCallFields.ArgumentPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (own.Parameter(parameter.Name[MacroCallFields.ArgumentPrefix.Length..]) is null)
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
    /// <param name="inputShared">
    /// True when another macro in the same project also sends keys or clicks. Two macros that send
    /// input at the same time write it through each other, and nothing about either macro looks
    /// wrong on its own — which is exactly why it has to be said here.
    /// </param>
    /// <param name="browserShared">
    /// True when another macro in the same project also uses the browser. There is one browser and
    /// one current page for the whole program, so two runs take turns there; a tab one changes is
    /// the page the other reads next.
    /// </param>
    public static IReadOnlyList<MacroProblem> Inspect(IEnumerable<MacroStep> steps,
        MacroCallable? own, IEnumerable<MacroCallable> macros, bool inputShared = false,
        bool browserShared = false)
    {
        IReadOnlyList<MacroStep> tree = steps as IReadOnlyList<MacroStep> ?? [.. steps];
        var problems = new List<MacroProblem>();

        var reachable = new Dictionary<string, MacroCallable?>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            reachable[macro.Name.Trim()] = macro;
        }

        var ownName = own?.Name.Trim();
        if (ownName is { Length: > 0 })
        {
            reachable[ownName] = own;
        }

        var defined = DefinedVariables(tree);
        var known = KnownTypes(tree, own, reachable);

        // A background run's results exist only once it has been waited for. These two say which
        // handle carries which names, and which of those names is still owed a wait: reading one
        // before then would read nothing.
        var started = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // What the macro declares is a name it reads like any other: the caller hands a value to it
        // under that name and the steps inside read $name. A macro lifted out of a stretch of steps
        // is exactly this shape, so without this it would be told that the names it was made for
        // are ones nothing defines. Declared results count too: the runtime clears them at the
        // start of a call, so reading one before a step writes it is reading the empty value this
        // attempt started with.
        if (own is not null)
        {
            defined.UnionWith(own.Parameters
                .Select(parameter => parameter.Name.Trim())
                .Where(name => name.Length > 0)
                .Where(VariableText.IsName));
            defined.UnionWith(own.Results
                .Select(result => result.Name.Trim())
                .Where(name => name.Length > 0)
                .Where(VariableText.IsName));
        }

        // A step that loads variables from a file, or runs a script with the macro in reach, can
        // bring in any name at all, so nothing can be said about which names the rest of the macro
        // is allowed to read.
        var namesAreOpen = WritesAnyName(tree);

        Visit(tree, 0, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), bounded: false,
            guarded: false, browserHeld: false);
        CheckSharedInput();
        CheckSharedBrowser();
        return problems;

        /// <summary>
        /// True when this step does the thing, or reaches it through a macro it calls. The names
        /// being walked are kept so that a macro calling itself — directly or around a circle — is
        /// answered rather than followed forever.
        /// </summary>
        bool Reaches(MacroStep step, Func<string, bool> direct, HashSet<string> walking)
        {
            if (direct(step.Type))
            {
                return true;
            }

            foreach (var list in step.StepLists)
            {
                if (list.Steps.Any(child => Reaches(child, direct, walking)))
                {
                    return true;
                }
            }

            if (step.Type is not ("control.runMacro" or "control.startMacro"))
            {
                return false;
            }

            var name = step.Text("macro").Trim();
            if (name.Length == 0 || !walking.Add(name)
                || reachable.GetValueOrDefault(name) is not { } called)
            {
                return false;
            }

            try
            {
                return called.OwnSteps.Any(child => Reaches(child, direct, walking));
            }
            finally
            {
                walking.Remove(name);
            }
        }

        void CheckSharedInput()
        {
            if (!inputShared)
            {
                return;
            }

            // Said once, on the first step that sends input: a mark on every one of them would be
            // the same sentence twenty times, and the answer — turn the switch on, or leave the two
            // macros not running together — is the same for all of them. A step that calls a macro
            // which sends input counts: the input is sent either way.
            var walking = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in Every(tree))
            {
                if (Reaches(step, MacroRunner.SendsInput, walking))
                {
                    Add(step, Strings.Get("Editor.Problem.SharedInput"));
                    return;
                }
            }
        }

        void CheckSharedBrowser()
        {
            if (!browserShared)
            {
                return;
            }

            // Said once, on the first browser step: the answer — let the two run apart, or know
            // that they take turns at one page — is the same for every browser step in the macro.
            // Through a call, the called macro's steps are the ones that take the page.
            var walking = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in Every(tree))
            {
                if (Reaches(step, MacroRunner.UsesBrowser, walking))
                {
                    Add(step, Strings.Get("Editor.Problem.SharedBrowser"));
                    return;
                }
            }
        }

        /// <summary>
        /// A background run started while this run is inside a browser step that holds the one page
        /// would wait for that page for good. The start step is marked instead.
        /// </summary>
        void CheckHeldBrowserRun(MacroStep step, bool browserHeld)
        {
            if (!browserHeld || step.Type != "control.startMacro")
            {
                return;
            }

            var name = step.Text("macro").Trim();
            if (reachable.GetValueOrDefault(name) is { } called
                && called.OwnSteps.Any(child => Reaches(child, MacroRunner.UsesBrowser,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase))))
            {
                Add(step, Strings.Get("Editor.Problem.BrowserRunWhileHeld"));
            }
        }

        void Visit(IReadOnlyList<MacroStep> list, int loops, int switches,
            IReadOnlySet<string> loopNames, bool bounded, bool guarded, bool browserHeld)
        {
            foreach (var step in list)
            {
                CheckLoopControl(step, loops, switches);
                CheckCondition(step);
                CheckLoopIndex(step, loops);
                CheckCall(step, guarded);
                TrackPendingResults(step);
                CheckNames(step, namesAreOpen);
                CheckPostedMove(step);
                CheckWait(step, bounded);
                CheckPlace(step);
                CheckValueTypes(step);
                CheckSwitch(step);
                CheckReservedNames(step);
                CheckScriptInput(step);
                CheckRunHandle(step);
                CheckHeldBrowserRun(step, browserHeld);
                CheckKnownStep(step);
                CheckMissingFields(step);
                CheckOptions(step);
                CheckRowColumns(step);
                CheckDuplicateResults(step);
                CheckExpressions(step);

                // A loop's own variables belong to the innermost loop that names them, so one
                // that borrows a name from a loop around it loses the outer value when it ends.
                var ownNames = LoopVariableNames(step);
                foreach (var name in ownNames)
                {
                    if (loopNames.Contains(name))
                    {
                        Add(step, Strings.Format("Editor.Problem.LoopVariableShadowed", name));
                    }
                }

                // Every list of child steps is walked from inside the loop it sits in, whichever
                // parameter holds it: a body, an if branch, a switch case and a try block all let
                // a break reach the loop around them.
                var insideLoops = LoopKeys.Contains(step.Type) ? loops + 1 : loops;
                var insideSwitches = step.Type == "control.switch" ? switches + 1 : switches;
                var insideNames = new HashSet<string>(loopNames, StringComparer.OrdinalIgnoreCase);
                insideNames.UnionWith(ownNames);
                var insideBounded = bounded || step.Meta.TimeoutMs > 0;
                var insideGuard = guarded || GuardKeys.Contains(step.Type);
                var insideHeld = browserHeld
                    || step.Type is "browser.dialog" or "control.holdBrowser";
                foreach (var parameter in step.Parameters)
                {
                    switch (parameter.Kind)
                    {
                        case ActionParameterKind.Steps:
                            Visit(parameter.Steps, insideLoops, insideSwitches, insideNames,
                                insideBounded, insideGuard, insideHeld);
                            break;
                        case ActionParameterKind.Condition when parameter.Condition is { } condition:
                            Visit([condition], insideLoops, insideSwitches, insideNames,
                                insideBounded, insideGuard, browserHeld);
                            break;
                    }
                }
            }
        }

        void CheckCondition(MacroStep step)
        {
            if (!ConditionKeys.Contains(step.Type))
            {
                return;
            }

            var chosen = step.Parameters
                .FirstOrDefault(parameter => parameter.Name == "condition")
                ?.Condition;
            if (chosen is null || chosen.Type.Trim().Length == 0)
            {
                Add(step, Strings.Get("Editor.Problem.MissingCondition"));
            }
        }

        void CheckScriptInput(MacroStep step)
        {
            // The declaration lives on the step rather than in a list of script action names, so a
            // new scripting language gets the same warning as soon as it carries the switch.
            var declared = step.Parameters.FirstOrDefault(parameter => parameter.Name == "sendsInput");
            if (declared is not null
                && string.Equals(declared.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                Add(step, Strings.Get("Editor.Problem.ScriptInput"));
            }
        }

        void CheckRunHandle(MacroStep step)
        {
            switch (step.Type)
            {
                case "control.startMacro":
                    var handle = step.Text("runHandleName").Trim();
                    if (handle.Length == 0)
                    {
                        Add(step, Strings.Get("Editor.Problem.MissingRunHandle"));
                        break;
                    }

                    // The names this call asked for are owed a wait: the results are not written
                    // until one takes them back.
                    var names = Wanted(step).Select(wanted => wanted.Name).ToList();
                    if (names.Count > 0)
                    {
                        started[handle] = names;
                        pending.UnionWith(names);
                    }

                    break;
                case "control.waitMacro":
                case "control.cancelMacro":
                case "control.runStatus":
                    if (step.Text("runHandle").Trim().Length == 0)
                    {
                        Add(step, Strings.Get("Editor.Problem.MissingRunHandle"));
                    }

                    break;
                case "control.waitAll":
                    if (step.Text("runHandles").Trim().Length == 0)
                    {
                        Add(step, Strings.Get("Editor.Problem.MissingRunHandles"));
                    }

                    break;
            }
        }

        /// <summary>
        /// Whether the step's action and every field it writes still exist in the catalogue. A macro
        /// written before an action or a field was renamed would otherwise run with values nothing
        /// reads, which looks like the step ignoring what its author wrote. The fields a call passes
        /// to another macro are named by that macro rather than by the catalogue, so they are not
        /// this question's.
        /// </summary>
        void CheckKnownStep(MacroStep step)
        {
            if (step.Definition is not { } definition)
            {
                Add(step, Strings.Get("Editor.Problem.UnknownAction"));
                return;
            }

            var calling = step.Type is "control.runMacro" or "control.startMacro";
            foreach (var parameter in step.Parameters)
            {
                if (calling && parameter.Name.StartsWith(MacroCallFields.ArgumentPrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!definition.Parameters.Any(known => known.Name == parameter.Name))
                {
                    Add(step, Strings.Format("Editor.Problem.UnknownParameter", parameter.Name));
                }
            }
        }

        /// <summary>
        /// A field the catalogue calls required that the step left empty is marked while it is still
        /// being written. The same rule the engine runs, so a macro that opens without a mark does
        /// not fail on one; a field that only applies under some settings is not asked.
        /// </summary>
        void CheckMissingFields(MacroStep step)
        {
            if (step.Definition is not { } definition)
            {
                return;
            }

            foreach (var parameter in definition.Parameters)
            {
                // A run handle already has its own check; saying it twice would teach the user to
                // ignore one of the two.
                if (parameter.Semantics.MissingValue(step.Text(parameter.Name))
                    && parameter.Semantics.MissingKey != RunKeys.MissingHandle)
                {
                    Add(step, Strings.Format("Editor.Problem.MissingField", parameter.LocalLabel));
                }
            }
        }

        /// <summary>
        /// A choice holding a word the catalogue does not offer. The engine refuses it unless the
        /// field says an unknown word falls back, so the step is marked here rather than at the run.
        /// </summary>
        void CheckOptions(MacroStep step)
        {
            if (step.Definition is not { } definition)
            {
                return;
            }

            foreach (var parameter in definition.Parameters)
            {
                if (parameter.Shape is not ActionValueShape.Choice
                    || parameter.Semantics.AllowsUnknownOption
                    || parameter.Semantics.Options.Count == 0)
                {
                    continue;
                }

                var written = step.Text(parameter.Name).Trim();
                if (written.Length > 0
                    && !parameter.Semantics.Options.Contains(written, StringComparer.Ordinal))
                {
                    Add(step, Strings.Format("Editor.Problem.BadOption",
                        parameter.LocalLabel, written));
                }
            }
        }

        /// <summary>True when a region row gives both a name and the four numbers.</summary>
        static bool RegionHasBoth(StepParameterRow row)
        {
            var named = !string.IsNullOrWhiteSpace(row.Text("text"));
            var numbered = new[] { "x", "y", "width", "height" }
                .Any(column => !string.IsNullOrWhiteSpace(row.Text(column)));
            return named && numbered;
        }
        /// <summary>
        /// A row list says its columns in the catalogue. A row that writes a column the action
        /// never reads, or leaves out one the list needs, is marked here instead of being skipped
        /// quietly at the run.
        /// </summary>
        void CheckRowColumns(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                if (step.ParameterDefinition(parameter) is not { } definition
                    || definition.Shape is not ActionValueShape.Rows
                    || definition.Semantics.Columns.Count == 0)
                {
                    continue;
                }

                foreach (var row in parameter.Rows)
                {
                    foreach (var column in row.Columns.Keys)
                    {
                        if (!definition.Semantics.Columns.Any(known =>
                                string.Equals(known.Name, column, StringComparison.OrdinalIgnoreCase)))
                        {
                            Add(step, Strings.Format("Editor.Problem.UnknownRowColumn",
                                definition.LocalLabel, column));
                        }
                    }

                    if (string.Equals(definition.Semantics.Name, "region", StringComparison.Ordinal)
                        && RegionHasBoth(row))
                    {
                        Add(step, Strings.Format("Editor.Problem.ExclusiveRowColumn",
                            definition.LocalLabel));
                    }
                    foreach (var column in definition.Semantics.Columns.Where(column => column.Required))
                    {
                        if (!row.Columns.TryGetValue(column.Name, out var value)
                            || string.IsNullOrWhiteSpace(value))
                        {
                            Add(step, Strings.Format("Editor.Problem.MissingRowColumn",
                                definition.LocalLabel, column.Name));
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Two ticks writing the same name: the second silently replaces the first, so the value the
        /// step reports is not the one the row was talking about.
        /// </summary>
        void CheckDuplicateResults(MacroStep step)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind is not ActionParameterKind.Returns)
                {
                    continue;
                }

                foreach (var (_, name) in step.ReturnedResults(parameter))
                {
                    if (!seen.Add(name))
                    {
                        Add(step, Strings.Format("Editor.Problem.DuplicateResultName", name));
                    }
                }
            }
        }

        /// <summary>
        /// A whole-cell value that starts with a dollar sign is read as an expression, so one that
        /// the language cannot parse would stop the run. Plain text and the escaped dollar are left
        /// alone: those are values, not formulae.
        /// </summary>
        void CheckExpressions(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.IsRowList
                    || step.ParameterDefinition(parameter) is not { } definition)
                {
                    continue;
                }

                if (definition.ReadMode is ActionReadMode.Text)
                {
                    foreach (var expression in VariableText.Expressions(parameter.Value))
                    {
                        if (!Expression.TryParse(expression, out _, out var fault))
                        {
                            Add(step, Strings.Format("Editor.Problem.BadExpression",
                                fault?.Detail ?? expression));
                        }
                    }

                    continue;
                }

                if (definition.ReadMode is not ActionReadMode.WholeValue)
                {
                    continue;
                }

                var written = parameter.Value.Trim();
                if (!written.StartsWith('$') || written.StartsWith("$$", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Expression.TryParse(written, out _, out var error))
                {
                    Add(step, Strings.Format("Editor.Problem.BadExpression",
                        error?.Detail ?? written));
                }
            }
        }

        /// <summary>
        /// A name a start step chose for a background run's result is not defined until a wait step
        /// takes the run's results back. The steps are walked in the order they are written, so a
        /// read that comes before that wait is said to be reading something that is not there yet.
        /// Cancelling is not a wait: it stops the run without taking any result back.
        /// </summary>
        void TrackPendingResults(MacroStep step)
        {
            switch (step.Type)
            {
                case "control.waitMacro":
                    ForgetPending(step.Text("runHandle"));
                    break;
                case "control.waitAll":
                    pending.Clear();
                    break;
            }

            if (namesAreOpen)
            {
                return;
            }

            foreach (var parameter in step.Parameters)
            {
                if (step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                foreach (var name in ReadsOf(step, parameter))
                {
                    if (pending.Contains(name))
                    {
                        Add(step, Strings.Format("Editor.Problem.ResultBeforeWait", name));
                        return;
                    }
                }
            }
        }

        /// <summary>Forgets the names a wait or cancel step has settled, by the handle it names.</summary>
        void ForgetPending(string written)
        {
            if (HandleVariable(written) is { } handle && started.Remove(handle, out var names))
            {
                pending.ExceptWith(names);
            }
        }

        void CheckLoopIndex(MacroStep step, int loops)
        {
            if (loops > 0)
            {
                return;
            }

            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind is ActionParameterKind.Steps or ActionParameterKind.Condition
                    || step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                if (ReadsOf(step, parameter).Contains(VariableStore.LoopIndexName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    Add(step, Strings.Get("Editor.Problem.LoopIndexOutsideLoop"));
                    return;
                }
            }
        }

        void CheckLoopControl(MacroStep step, int loops, int switches)
        {
            if (step.Type is not ("control.break" or "control.continue"))
            {
                return;
            }

            // A break inside a switch leaves that switch; a continue only ever means a loop.
            var reachable = step.Type == "control.break" ? loops > 0 || switches > 0 : loops > 0;
            if (reachable)
            {
                return;
            }

            Add(step, Strings.Get(step.Type == "control.break"
                ? "Editor.Problem.BreakOutsideLoop"
                : "Editor.Problem.ContinueOutsideLoop"));
        }

        void CheckCall(MacroStep step, bool guarded)
        {
            if (step.Type is not ("control.runMacro" or "control.startMacro"))
            {
                return;
            }

            var name = step.Text("macro").Trim();
            if (name.Length == 0)
            {
                Add(step, Strings.Get("Editor.Problem.NoMacro"));
                return;
            }

            if (!reachable.TryGetValue(name, out var called))
            {
                Add(step, Strings.Format("Editor.Problem.MacroNotFound", name));
                return;
            }

            // What a call passes and asks for is read against what the macro declares: a name the
            // macro never mentioned is a mistake in the call, and the run would report it far from
            // here if it were left to be found out then.
            if (called is null)
            {
                return;
            }

            if (ownName is { Length: > 0 }
                && string.Equals(called.Name.Trim(), ownName, StringComparison.OrdinalIgnoreCase)
                && !guarded)
            {
                Add(step, Strings.Get("Editor.Problem.UnguardedRecursion"));
            }

            foreach (var parameter in step.Parameters)
            {
                if (!parameter.Name.StartsWith(MacroCallFields.ArgumentPrefix, StringComparison.Ordinal)
                    || MacroCallFields.IsGiven(parameter.Name))
                {
                    continue;
                }

                var passed = parameter.Name[MacroCallFields.ArgumentPrefix.Length..];
                if (called.Parameter(passed) is null)
                {
                    Add(step, Strings.Format("Editor.Problem.UndeclaredArgument", passed, name));
                }
            }

            foreach (var (result, _) in Wanted(step))
            {
                if (!called.Offers(result))
                {
                    Add(step, Strings.Format("Editor.Problem.UndeclaredResult", result, name));
                }
            }
        }

        void CheckNames(MacroStep step, bool open)
        {
            if (open)
            {
                return;
            }

            foreach (var parameter in step.Parameters)
            {
                // Steps and conditions hold other steps rather than a value of their own, and text
                // written in another language spells its own variables with a $.
                if (parameter.Kind is ActionParameterKind.Steps or ActionParameterKind.Condition
                    || step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                foreach (var name in ReadsOf(step, parameter))
                {
                    if (!defined.Contains(name))
                    {
                        Add(step, Strings.Format("Editor.Problem.UndefinedVariable", name));
                    }
                }
            }
        }

        /// <summary>
        /// 相对移动与后台这一档合不来：后台方式是把消息投给窗口，屏幕上的指针从来没有动过，而
        /// "偏移多少"问的正是指针现在在哪儿。这是步骤自己的设置就能判定的事，所以写宏的时候就
        /// 说出来，不必等跑到这一步才发现位置是错的。
        /// </summary>
        void CheckPostedMove(MacroStep step)
        {
            if (step.Type == "input.mouseMoveRelative"
                && step.Text("inputMode").Trim().Equals("background", StringComparison.OrdinalIgnoreCase))
            {
                Add(step, Strings.Get("Editor.Problem.PostedMove"));
            }
        }

        /// <summary>
        /// A wait with no clock of its own is a legitimate "wait until it happens". It stops being
        /// one when the step retries or an outer block is on a clock: then the retry or the outer
        /// budget is what the user is relying on, and neither can make this wait end sooner.
        /// </summary>
        void CheckWait(MacroStep step, bool bounded)
        {
            if (step.Definition?.Waits != true || step.Meta.TimeoutMs > 0)
            {
                return;
            }

            if (step.Meta.RetryCount > 0 || bounded)
            {
                Add(step, Strings.Get("Editor.Problem.WaitWithoutTimeout"));
            }
        }

        /// <summary>
        /// A step that says where it works says it once: a record, or the numbers beside it. Both
        /// empty is a step that would work at nothing in particular, which the editor can see
        /// before the macro runs. A click and a drag fall back to the pointer, and a step that reads
        /// the screen falls back to the whole of it, so those say nothing is a real answer.
        /// </summary>
        void CheckPlace(MacroStep step)
        {
            if (step.Definition is not { } definition
                || definition.Parameters.Any(parameter => parameter.Name == "useCursor")
                || step.Type is "vision.capture" or "ocr.recognize")
            {
                return;
            }

            foreach (var (place, numbers) in PlaceGroups)
            {
                if (definition.Parameters.FirstOrDefault(parameter =>
                        parameter.Name == place) is not { } field)
                {
                    continue;
                }

                var written = step.Text(place).Trim();
                var number = numbers.Any(name => step.Text(name).Trim().Length > 0);
                if (written.Length == 0 && !number)
                {
                    Add(step, Strings.Format("Editor.Problem.NoPlace", field.LocalLabel));
                }
                else if (written.Length > 0 && number)
                {
                    // The record wins in the engine, so the numbers the author typed are not read
                    // at all. Saying so is better than letting the step look like it uses both.
                    Add(step, Strings.Format("Editor.Problem.PlaceAndNumbers", field.LocalLabel));
                }
            }
        }

        /// <summary>
        /// What a value is has to match what the field takes. Only names whose kind the steps
        /// themselves declare are judged — a hit is a record, a line of writing is text — because
        /// guessing at a name nobody declared would teach the user to ignore the marks.
        /// </summary>
        void CheckValueTypes(MacroStep step)
        {
            CheckCallArguments(step);

            foreach (var parameter in step.Parameters)
            {
                if (step.ParameterDefinition(parameter) is not { } definition)
                {
                    continue;
                }

                var value = parameter.Value.Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                if (WholeName(value) is { } name && known.TryGetValue(name, out var type))
                {
                    if (definition.Shape == ActionValueShape.Number
                        && type == ActionReturnType.Record)
                    {
                        Add(step, Strings.Format("Editor.Problem.RecordWhereNumber",
                            definition.LocalLabel));
                    }
                    else if (definition.Shape
                             is ActionValueShape.Point or ActionValueShape.Rect
                                 or ActionValueShape.Record
                             && type != ActionReturnType.Record
                             && type != ActionReturnType.List)
                    {
                        Add(step, Strings.Format("Editor.Problem.NotARecord",
                            definition.LocalLabel, name));
                    }
                }

                // A record named inside text becomes JSON where the user meant a title or a line:
                // a field read as one value keeps the record whole, and the window fields read it
                // as a window, so those are left alone.
                if (definition.ReadMode != ActionReadMode.Text
                    || definition.Shape is ActionValueShape.Point or ActionValueShape.Rect
                        or ActionValueShape.Record
                    || definition.Picker == ActionPickerKind.Window
                    || definition.Name is "window" or "anchorWindow" or "targetWindow")
                {
                    continue;
                }

                foreach (var referenced in VariableText.Referenced(value))
                {
                    if (known.TryGetValue(referenced, out var kind)
                        && kind == ActionReturnType.Record)
                    {
                        Add(step, Strings.Format("Editor.Problem.RecordInText", referenced,
                            definition.LocalLabel));
                    }
                }
            }
        }

        /// <summary>
        /// A call passes values the called macro asked for. A value whose kind is known and does not
        /// fit what was asked for is worth saying before the run: the call would read it as text or
        /// refuse it, and neither is visible from the call's own form.
        /// </summary>
        void CheckCallArguments(MacroStep step)
        {
            if (step.Type is not ("control.runMacro" or "control.startMacro")
                || reachable.GetValueOrDefault(step.Text("macro").Trim()) is not { } called)
            {
                return;
            }

            var faults = DeclarationFaults(called);
            foreach (var message in faults)
            {
                Add(step, message);
            }

            // A call whose declaration cannot be used will fail before the arguments matter; the
            // declaration messages are the ones worth showing.
            if (faults.Count > 0)
            {
                return;
            }

            foreach (var declared in called.Parameters)
            {
                var name = declared.Name.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                var field = step.Text(MacroCallFields.Argument(name)).Trim();
                var given = string.Equals(step.Text(MacroCallFields.Given(name)).Trim(), "true",
                    StringComparison.OrdinalIgnoreCase);
                var supplied = field.Length > 0 || given;

                if (given && field.Length > 0)
                {
                    Add(step, Strings.Format("Editor.Problem.GivenWithValue", name));
                }

                if (!supplied)
                {
                    if (declared.Required)
                    {
                        Add(step, Strings.Format("Editor.Problem.MissingArgument", name,
                            step.Text("macro").Trim()));
                    }

                    continue;
                }

                if (field.Length == 0)
                {
                    continue;
                }

                if (ConstantValue(field) is { } value
                    && !MacroValues.Fits(declared.Type, value))
                {
                    Add(step, Strings.Format("Editor.Problem.ArgumentType", name,
                        Strings.Get("ActionType." + declared.Type, declared.Type.ToString())));
                    continue;
                }

                if (WholeName(field) is not { } whole
                    || !known.TryGetValue(whole, out var type)
                    || !ObviouslyWrong(declared.Type, type))
                {
                    continue;
                }

                Add(step, Strings.Format("Editor.Problem.ArgumentType", name,
                    Strings.Get("ActionType." + declared.Type, declared.Type.ToString())));
            }

            foreach (var parameter in step.Parameters.Where(parameter => parameter.Name.StartsWith(
                         MacroCallFields.ArgumentPrefix, StringComparison.Ordinal)))
            {
                var passed = MacroCallFields.IsGiven(parameter.Name)
                    ? parameter.Name[MacroCallFields.ArgumentPrefix.Length..^MacroCallFields.GivenSuffix.Length]
                    : parameter.Name[MacroCallFields.ArgumentPrefix.Length..];
                if (called.Parameter(passed) is null)
                {
                    Add(step, Strings.Format("Editor.Problem.UndeclaredArgument", passed,
                        step.Text("macro").Trim()));
                }
            }
        }

        void CheckSwitch(MacroStep step)
        {
            if (step.Type != "control.switch")
            {
                return;
            }
            var cases = step.Parameters
                .FirstOrDefault(parameter => parameter.Name == "cases")?.Steps
                .Count(branch => branch.Type == "control.case") ?? 0;
            var otherwise = step.Parameters
                .FirstOrDefault(parameter => parameter.Name == "otherwise")?.Steps.Count ?? 0;
            if (cases == 0 && otherwise == 0)
            {
                Add(step, Strings.Get("Editor.Problem.EmptySwitch"));
            }
        }

        /// <summary>
        /// The names the run itself keeps are not the macro's to write. A step that defines one of
        /// them would replace a value the rest of the macro reads, so it is said here rather than
        /// found out when the wrong value turns up.
        /// </summary>
        void CheckReservedNames(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind == ActionParameterKind.Returns)
                {
                    foreach (var (_, name) in step.ReturnedResults(parameter))
                    {
                        Check(name);
                    }
                }
                else if (step.DefinesVariable(parameter))
                {
                    Check(parameter.Value);
                }
            }

            void Check(string written)
            {
                var name = written.Trim();
                if (name.Length > 0
                    && (name.StartsWith("sys.", StringComparison.OrdinalIgnoreCase)
                        || MacroRunner.IsRunStepName(name)))
                {
                    Add(step, Strings.Format("Editor.Problem.ReservedVariable", name));
                }
            }
        }

        void Add(MacroStep step, string message)
        {
            if (!problems.Any(problem => problem.Step == step && problem.Message == message))
            {
                problems.Add(new MacroProblem(step, message));
            }
        }
    }

    /// <summary>
    /// The variable names one parameter reads, taken from the catalogue's read mode rather than
    /// from its editor kind. A whole value may name several variables anywhere inside it; text
    /// names them where they stand but leaves <c>$$</c> alone; a bare-name field is one name.
    /// A field that writes a name reads nothing.
    /// </summary>
    public static IReadOnlyList<string> ReadsOf(MacroStep step, StepParameter parameter)
    {
        if (step.DefinesVariable(parameter))
        {
            return [];
        }

        // A list parameter keeps its values in rows, and every one of them may name a picture an
        // earlier step took or a value an expression works out.
        if (parameter.IsRowList)
        {
            return [.. parameter.RowValues
                .SelectMany(VariableText.Referenced)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        var definition = step.ParameterDefinition(parameter);
        if (definition is null)
        {
            // A macro call's arguments are not in the catalogue; the file reads them as one value.
            return Expression.ReferencedNames(parameter.Value);
        }

        return definition.ReadMode switch
        {
            ActionReadMode.VariableName => parameter.Value.Trim().TrimStart('$') is { Length: > 0 } name
                ? [name]
                : [],
            ActionReadMode.WholeValue => Expression.ReferencedNames(parameter.Value),
            _ => VariableText.Referenced(parameter.Value),
        };
    }

    /// <summary>A yes/no field the way the engine reads one: anything but "false" is on.</summary>
    private static bool On(MacroStep step, string name)
    {
        var text = step.Text(name).Trim();
        return text.Length > 0
            && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The names a loop writes for its own rounds: the item and round number of a "for each" and
    /// the counter of a counting loop. A repeat or a while counts the rounds in the runner's head,
    /// so there is no name of the macro's to keep.
    /// </summary>
    private static string[] LoopVariableNames(MacroStep step)
    {
        switch (step.Type)
        {
            case "control.forEach":
                var item = step.Text("itemVariable").Trim();
                var index = step.Text("indexVariable").Trim();
                return
                [
                    .. new[] { item.Length == 0 ? "item" : item, index }
                        .Where(name => name.Length > 0),
                ];
            case "control.for":
                var counter = step.Text("name").Trim();
                return [counter.Length == 0 ? "i" : counter];
            default:
                return [];
        }
    }
    /// <summary>
    /// The variable a run-handle field names when it holds one variable and nothing else, or null
    /// when it is an expression. Only that form lets the check say which run a wait settles.
    /// </summary>
    private static string? HandleVariable(string written)
    {
        var text = written.Trim();
        if (text.Length < 2 || text[0] != '$' || text.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        var name = text[1..];
        return name.All(letter => char.IsLetterOrDigit(letter) || letter is '.' or '_' or '-')
            ? name
            : null;
    }

    /// <summary>
    /// The names a macro may read: the ones WhaleGenie provides, the shared ones, the ones its own
    /// steps create, and the ones a call asks a called macro to bring back. A name the macro
    /// defines anywhere counts, because whether a step runs before the one that reads it is a
    /// question the steps alone cannot answer.
    /// </summary>
    private static HashSet<string> DefinedVariables(IReadOnlyList<MacroStep> steps)
    {
        var defined = new HashSet<string>(
            VariableCatalog.SystemVariables.Select(variable => variable.Name),
            StringComparer.OrdinalIgnoreCase);
        defined.UnionWith(VariableCatalog.Globals.Select(variable => variable.Name));

        // A catch block is handed the failure it took on under these two names. Whether a read
        // sits inside a catch is not something the walk below can settle yet, so the names count
        // as known rather than every correct catchWhen being marked as reading nothing.
        defined.Add(MacroRunner.CaughtKeyName);
        defined.Add(MacroRunner.CaughtDetailName);

        foreach (var step in steps)
        {
            step.CollectVariables(defined);
        }

        foreach (var step in Every(steps).Where(step => step.Type == "control.runMacro"))
        {
            foreach (var (_, name) in Wanted(step))
            {
                defined.Add(name);
            }
        }

        // Every step that can run leaves an ending and a failure key behind under its own id,
        // whether or not the editor asked for a named result.
        foreach (var step in Every(steps).Where(step => !step.IsCondition && step.Id.Length > 0))
        {
            defined.Add(MacroRunner.OutcomeName(step.Id));
            defined.Add(MacroRunner.ErrorName(step.Id));
        }

        return defined;
    }

    /// <summary>
    /// The results a call asks to have back, one row per result: what the called macro calls the
    /// value, and the name the caller wants it under. A row without both is not asking for anything.
    /// </summary>
    /// <summary>
    /// What kind of value each name the macro can read is known to hold. Only what the steps and
    /// declarations say: a result the catalogue calls a record is a record, a line of writing is
    /// text, and a name nothing declares is left unknown rather than guessed at.
    /// </summary>
    private static Dictionary<string, ActionReturnType> KnownTypes(IReadOnlyList<MacroStep> steps,
        MacroCallable? own, Dictionary<string, MacroCallable?> reachable)
    {
        var known = new Dictionary<string, ActionReturnType>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in VariableCatalog.SystemVariables)
        {
            known[variable.Name] = variable.Type switch
            {
                "number" => ActionReturnType.Number,
                "bool" => ActionReturnType.Flag,
                "list" => ActionReturnType.List,
                _ => ActionReturnType.Text,
            };
        }

        if (own is not null)
        {
            foreach (var parameter in own.Parameters)
            {
                if (parameter.Name.Trim() is { Length: > 0 } name && VariableText.IsName(name))
                {
                    known[name] = parameter.Type;
                }
            }

            // An in-out name keeps the parameter's kind: before it is written it holds the value
            // the caller passed, and that is what a read at the top of the macro sees.
            foreach (var result in own.Results)
            {
                if (result.Name.Trim() is { Length: > 0 } name
                    && VariableText.IsName(name)
                    && !known.ContainsKey(name))
                {
                    known[name] = result.Type;
                }
            }
        }

        foreach (var step in Every(steps))
        {
            if (step.Definition is { } definition
                && step.Parameters.FirstOrDefault(parameter =>
                    parameter.Kind is ActionParameterKind.Returns) is { } returns)
            {
                foreach (var (key, name) in step.ReturnedResults(returns))
                {
                    if (definition.Returns.FirstOrDefault(result =>
                            string.Equals(result.Key, key, StringComparison.OrdinalIgnoreCase))
                        is { } result)
                    {
                        known[name] = result.Type;
                    }
                }
            }

            if (step.Type is "control.runMacro" or "control.startMacro"
                && reachable.GetValueOrDefault(step.Text("macro").Trim()) is { } called)
            {
                foreach (var (result, name) in Wanted(step))
                {
                    if (called.Results.FirstOrDefault(candidate => string.Equals(candidate.Name,
                            result, StringComparison.OrdinalIgnoreCase)) is { } declared)
                    {
                        known[name] = declared.Type;
                    }
                }
            }
        }

        return known;
    }

    /// <summary>
    /// True when a value of one kind cannot stand for a value the called macro declared: a record
    /// is not a line of text, a line of text is not a record, and a list is not a number.
    /// </summary>
    private static bool ObviouslyWrong(ActionReturnType declared, ActionReturnType actual)
    {
        if (declared == actual)
        {
            return false;
        }

        var composite = actual
            is ActionReturnType.Record or ActionReturnType.List or ActionReturnType.Image;
        return declared switch
        {
            ActionReturnType.Record => !composite,
            ActionReturnType.Text => composite,
            ActionReturnType.Number or ActionReturnType.Flag => composite,
            _ => false,
        };
    }

    /// <summary>
    /// The name a cell holds when it is one variable and nothing else, or null when it is text, an
    /// expression or several things at once.
    /// </summary>
    private static string? WholeName(string value)
    {
        var text = value.Trim();
        if (text.Length < 2 || text[0] != '$')
        {
            return null;
        }

        var name = text[1..];
        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            return null;
        }

        foreach (var letter in name)
        {
            if (!char.IsLetterOrDigit(letter) && letter != '_' && letter != '.')
            {
                return null;
            }
        }

        return name;
    }

    private static IEnumerable<(string Result, string Name)> Wanted(MacroStep step)
    {
        foreach (var parameter in step.Parameters.Where(parameter => parameter.Name == "returns"))
        {
            foreach (var row in parameter.Rows)
            {
                var result = row.Text(FieldNames.Key).Trim();
                var name = row.Text(FieldNames.Name).Trim();
                if (result.Length > 0 && name.Length > 0)
                {
                    yield return (result, name);
                }
            }
        }
    }

    /// <summary>Every step of the macro, nested ones and conditions included.</summary>
    private static IEnumerable<MacroStep> Every(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;

            foreach (var parameter in step.Parameters)
            {
                foreach (var nested in Every(parameter.Steps))
                {
                    yield return nested;
                }

                if (parameter.Condition is { } condition)
                {
                    foreach (var nested in Every([condition]))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Every variable name the steps read, nested steps included. It is what says which values a
    /// stretch of steps takes from around it: the values a lift has to turn into arguments of the
    /// macro it makes, and what "nothing defines this" is decided against elsewhere.
    /// </summary>
    public static IReadOnlyList<string> Reads(IEnumerable<MacroStep> steps)
    {
        var names = new List<string>();
        foreach (var step in Every(steps))
        {
            foreach (var parameter in step.Parameters)
            {
                if (step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                names.AddRange(ReadsOf(step, parameter));
            }
        }

        return names;
    }
}
