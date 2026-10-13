using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
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
    public static IReadOnlyList<MacroProblem> Inspect(IEnumerable<MacroStep> steps,
        MacroCallable? own, IEnumerable<MacroCallable> macros, bool inputShared = false)
    {
        IReadOnlyList<MacroStep> tree = steps as IReadOnlyList<MacroStep> ?? [.. steps];
        var problems = new List<MacroProblem>();

        var reachable = new Dictionary<string, MacroCallable?>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            reachable[macro.Name.Trim()] = macro;
        }

        if (own?.Name.Trim() is { Length: > 0 } ownName)
        {
            reachable[ownName] = own;
        }

        var defined = DefinedVariables(tree);

        // A step that loads variables from a file can bring in any name at all, so nothing can be
        // said about which names the rest of the macro is allowed to read.
        var namesAreOpen = Every(tree).Any(step => step.Type == "file.loadVariables");

        Visit(tree, 0);
        CheckSharedInput();
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

        void Visit(IReadOnlyList<MacroStep> list, int loops)
        {
            foreach (var step in list)
            {
                CheckLoopControl(step, loops);
                CheckCall(step);
                CheckNames(step, namesAreOpen);

                // Every list of child steps is walked from inside the loop it sits in, whichever
                // parameter holds it: a body, an if branch, a switch case and a try block all let
                // a break reach the loop around them.
                var inside = LoopKeys.Contains(step.Type) ? loops + 1 : loops;
                foreach (var parameter in step.Parameters)
                {
                    switch (parameter.Kind)
                    {
                        case ActionParameterKind.Steps:
                            Visit(parameter.Steps, inside);
                            break;
                        case ActionParameterKind.Condition when parameter.Condition is { } condition:
                            Visit([condition], inside);
                            break;
                    }
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

        void CheckCall(MacroStep step)
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

                foreach (var name in Referenced(parameter))
                {
                    if (!defined.Contains(name))
                    {
                        Add(step, Strings.Format("Editor.Problem.UndefinedVariable", name));
                    }
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
    /// The variable names one parameter reads, read the same way the Variable Center reads them:
    /// a value written as an expression may name several variables anywhere inside it, while a
    /// plain field only names one when the whole value starts with <c>$</c>. A <c>$name</c> in
    /// the middle of a sentence is left alone — it is also how a literal dollar is written into
    /// text, and the steps alone cannot say which was meant.
    /// </summary>
    private static IReadOnlyList<string> Referenced(StepParameter parameter)
    {
        if (parameter.Kind is ActionParameterKind.Expression)
        {
            return Expression.ReferencedNames(parameter.Value);
        }

        // A list parameter keeps its values in rows, and every one of them is read like a plain
        // field: a row naming a picture a Capture took is a name the macro has to have defined.
        if (parameter.IsRowList)
        {
            return [.. parameter.RowValues
                .Select(value => value.Trim())
                .Where(value => value.StartsWith('$'))
                .SelectMany(Expression.ReferencedNames)];
        }

        var text = parameter.Value.Trim();
        return text.StartsWith('$') ? Expression.ReferencedNames(text) : [];
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
                var result = row.Text("key").Trim();
                var name = row.Text("name").Trim();
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

                names.AddRange(Referenced(parameter));
            }
        }

        return names;
    }
}
