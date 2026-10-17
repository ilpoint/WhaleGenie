using System;
using System.Collections.Generic;
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
    /// The switches that say a place comes from a record, and the field that holds the record.
    /// The two are read together because a checked switch with nothing under it is a step that
    /// will fail before it does anything.
    /// </summary>
    private static readonly (string Flag, string Record)[] RecordPairs =
    [
        ("useRecord", "record"),
        ("startFromRecord", "startRecord"),
        ("endFromRecord", "endRecord"),
    ];

    /// <summary>
    /// Everything wrong with the macro, in the order the steps are written.
    /// <paramref name="macros"/> is every macro a call can reach — the project list, each with what
    /// it declares — and <paramref name="ownName"/> is the macro being edited, which may call itself
    /// and so is judged by <paramref name="own"/>.
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

        // What the macro declares is a name it reads like any other: the caller hands a value to it
        // under that name and the steps inside read $name. A macro lifted out of a stretch of steps
        // is exactly this shape, so without this it would be told that the names it was made for
        // are ones nothing defines.
        if (own is not null)
        {
            defined.UnionWith(own.Parameters
                .Select(parameter => parameter.Name.Trim())
                .Where(name => name.Length > 0));
        }

        // A step that loads variables from a file can bring in any name at all, so nothing can be
        // said about which names the rest of the macro is allowed to read.
        var namesAreOpen = Every(tree).Any(step => step.Type == "file.loadVariables");

        Visit(tree, 0, bounded: false, guarded: false);
        CheckSharedInput();
        CheckSharedBrowser();
        return problems;

        void CheckSharedInput()
        {
            if (!inputShared)
            {
                return;
            }

            // Said once, on the first step that sends input: a mark on every one of them would be
            // the same sentence twenty times, and the answer — turn the switch on, or leave the two
            // macros not running together — is the same for all of them.
            foreach (var step in Every(tree))
            {
                if (MacroRunner.SendsInput(step.Type))
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
            foreach (var step in Every(tree))
            {
                if (MacroRunner.UsesBrowser(step.Type))
                {
                    Add(step, Strings.Get("Editor.Problem.SharedBrowser"));
                    return;
                }
            }
        }

        void Visit(IReadOnlyList<MacroStep> list, int loops, bool bounded, bool guarded)
        {
            foreach (var step in list)
            {
                CheckLoopControl(step, loops);
                CheckCondition(step);
                CheckLoopIndex(step, loops);
                CheckCall(step, guarded);
                CheckNames(step, namesAreOpen);
                CheckPostedMove(step);
                CheckWait(step, bounded);
                CheckRecord(step);
                CheckSwitch(step);
                CheckReservedNames(step);
                CheckScriptInput(step);
                CheckRunHandle(step);

                // Every list of child steps is walked from inside the loop it sits in, whichever
                // parameter holds it: a body, an if branch, a switch case and a try block all let
                // a break reach the loop around them.
                var insideLoops = LoopKeys.Contains(step.Type) ? loops + 1 : loops;
                var insideBounded = bounded || step.Meta.TimeoutMs > 0;
                var insideGuard = guarded || GuardKeys.Contains(step.Type);
                foreach (var parameter in step.Parameters)
                {
                    switch (parameter.Kind)
                    {
                        case ActionParameterKind.Steps:
                            Visit(parameter.Steps, insideLoops, insideBounded, insideGuard);
                            break;
                        case ActionParameterKind.Condition when parameter.Condition is { } condition:
                            Visit([condition], insideLoops, insideBounded, insideGuard);
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
                case "control.runMacro":
                    var waiting = !string.Equals(step.Text("wait").Trim(), "false",
                        StringComparison.OrdinalIgnoreCase);
                    if (!waiting && step.Text("runHandleName").Trim().Length == 0)
                    {
                        Add(step, Strings.Get("Editor.Problem.MissingRunHandle"));
                    }

                    break;
                case "control.waitMacro":
                    if (step.Text("runHandle").Trim().Length == 0)
                    {
                        Add(step, Strings.Get("Editor.Problem.MissingRunHandle"));
                    }

                    break;
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

        void CheckLoopControl(MacroStep step, int loops)
        {
            if (loops > 0)
            {
                return;
            }

            switch (step.Type)
            {
                case "control.break":
                    Add(step, Strings.Get("Editor.Problem.BreakOutsideLoop"));
                    break;
                case "control.continue":
                    Add(step, Strings.Get("Editor.Problem.ContinueOutsideLoop"));
                    break;
            }
        }

        void CheckCall(MacroStep step, bool guarded)
        {
            if (step.Type != "control.runMacro")
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
                if (!parameter.Name.StartsWith(MacroCallFields.ArgumentPrefix, StringComparison.Ordinal))
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
        /// A place taken from a record needs the record. The switch and the field are one answer
        /// written in two places, and the editor can see when only half of it is there.
        /// </summary>
        void CheckRecord(MacroStep step)
        {
            foreach (var (flag, record) in RecordPairs)
            {
                if (!On(step, flag) || step.Text(record).Trim().Length > 0)
                {
                    continue;
                }

                var label = step.Definition?.Parameters
                    .FirstOrDefault(parameter => parameter.Name == record)?.LocalLabel ?? record;
                Add(step, Strings.Format("Editor.Problem.EmptyRecord", label));
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
