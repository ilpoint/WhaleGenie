using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <paramref name="macroNames"/> is every macro a call can reach — the project list — and
    /// <paramref name="ownName"/> is the macro being edited, which may call itself.
    /// </summary>
    public static IReadOnlyList<MacroProblem> Inspect(IEnumerable<MacroStep> steps,
        IEnumerable<string> macroNames, string ownName)
    {
        IReadOnlyList<MacroStep> tree = steps as IReadOnlyList<MacroStep> ?? [.. steps];
        var problems = new List<MacroProblem>();

        var reachable = new HashSet<string>(macroNames, StringComparer.OrdinalIgnoreCase);
        if (ownName.Trim().Length > 0)
        {
            reachable.Add(ownName.Trim());
        }

        var defined = DefinedVariables(tree);

        // A step that loads variables from a file can bring in any name at all, so nothing can be
        // said about which names the rest of the macro is allowed to read.
        var namesAreOpen = Every(tree).Any(step => step.Type == "file.loadVariables");

        Visit(tree, 0);
        return problems;

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

            var name = Text(step, "macro").Trim();
            if (name.Length == 0)
            {
                Add(step, Strings.Get("Editor.Problem.NoMacro"));
            }
            else if (!reachable.Contains(name))
            {
                Add(step, Strings.Format("Editor.Problem.MacroNotFound", name));
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
            foreach (var name in Text(step, "returns").Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                defined.Add(name);
            }
        }

        return defined;
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

    /// <summary>The text of a named parameter, or empty when the step has no such parameter.</summary>
    private static string Text(MacroStep step, string name)
        => step.Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Value ?? string.Empty;
}
