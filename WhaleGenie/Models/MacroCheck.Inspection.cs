using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

public static partial class MacroCheck
{
    /// <summary>
    /// One walk of a macro. The names the steps have defined so far, the kind each name is known
    /// to hold, and the problems found along the way all live here rather than in the walk itself:
    /// each check is then a method that can be read on its own instead of a closure reaching back
    /// over a dozen locals declared above it.
    /// </summary>
    private sealed partial class Inspection
    {
        private readonly bool browserShared;
        private readonly HashSet<string> defined;
        private readonly HashSet<string> definedIds;
        private readonly Dictionary<string, ValueKind> known;
        private readonly bool namesAreOpen;
        private readonly string? ownName;
        private readonly List<MacroProblem> problems = [];
        private readonly Dictionary<string, IReadOnlyList<string>> recordFields;
        private readonly Dictionary<string, MacroCallable?> reachable;
        private readonly IReadOnlyList<MacroStep> tree;

        public Inspection(IEnumerable<MacroStep> steps, MacroCallable? own,
            IEnumerable<MacroCallable> macros, bool browserShared)
        {
            tree = steps as IReadOnlyList<MacroStep> ?? [.. steps];
            this.browserShared = browserShared;

            reachable = new Dictionary<string, MacroCallable?>(StringComparer.OrdinalIgnoreCase);
            foreach (var macro in macros)
            {
                reachable[macro.Name.Trim()] = macro;
            }

            ownName = own?.Name.Trim();
            if (ownName is { Length: > 0 })
            {
                reachable[ownName] = own;
            }

            defined = DefinedVariables(tree);
            definedIds = DefinedIds(tree, own);
            known = KnownTypes(tree, own, reachable);
            recordFields = KnownFields(tree, own, reachable);

            // A background run's results exist only once it has been waited for. These two say which
            // handle carries which names, and which of those names is still owed a wait: reading one
            // before then would read nothing.

            // What the macro declares is a name it reads like any other: the caller hands a value to it
            // under that name and the steps inside read $name. A macro lifted out of a stretch of steps
            // is exactly this shape, so without this it would be told that the names it was made for
            // are ones nothing defines. Declared results count too: the runtime clears them at the
            // start of a call, so reading one before a step writes it is reading the empty value this
            // attempt started with. A declared local is written before the first step, so it is a name
            // the macro already has rather than one a step has yet to make.
            if (own is not null)
            {
                defined.UnionWith(own.Parameters
                    .Select(parameter => parameter.Name.Trim())
                    .Where(name => name.Length > 0)
                    .Where(VariableName.IsName));
                defined.UnionWith(own.Results
                    .Select(result => result.Name.Trim())
                    .Where(name => name.Length > 0)
                    .Where(VariableName.IsName));
                defined.UnionWith(own.OwnLocals
                    .Select(local => local.Name.Trim())
                    .Where(name => name.Length > 0)
                    .Where(VariableName.IsName));
            }

            // A step that loads variables from a file, or runs a script with the macro in reach, can
            // bring in any name at all, so nothing can be said about which names the rest of the macro
            // is allowed to read.
            namesAreOpen = WritesAnyName(tree);
        }

        public IReadOnlyList<MacroProblem> Run()
        {
            Visit(tree, null, 0, 0,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), bounded: false,
                guarded: false, browserHeld: false);
            CheckSharedBrowser();
            return problems;
        }

        /// <summary>
        /// True when this step does the thing, or reaches it through a macro it calls. The names
        /// being walked are kept so that a macro calling itself — directly or around a circle — is
        /// answered rather than followed forever.
        /// </summary>
        private bool Reaches(MacroStep step, Func<string, bool> direct, HashSet<string> walking)
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

            if (!CallsMacro(step))
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

        private void CheckSharedBrowser()
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
                    Add(step, MacroProblemCode.SharedBrowser, Strings.Get("Editor.Problem.SharedBrowser"));
                    return;
                }
            }
        }

        private void Visit(IReadOnlyList<MacroStep> list, ActionParameter? destination,
            int loops, int switches,
            IReadOnlySet<string> loopNames, bool bounded, bool guarded, bool browserHeld)
        {
            CheckSiblings(list);

            foreach (var step in list)
            {
                CheckStepPlacement(step, destination);
                CheckLoopControl(step, loops, switches);
                CheckCondition(step);
                CheckUserFailureKey(step);
                CheckGlobalTarget(step);
                CheckCall(step, guarded);
                CheckNames(step, namesAreOpen);
                CheckWait(step, bounded);
                CheckPlace(step);
                CheckWindowBounds(step);
                CheckValueTypes(step);
                CheckFieldNames(step);
                CheckSwitch(step);
                CheckReservedNames(step);
                CheckScriptInput(step);
                CheckKnownStep(step);
                CheckMissingFields(step);
                CheckOptions(step);
                CheckRowColumns(step);
                CheckActionResultRequests(step);
                CheckDuplicateResults(step);
                CheckExpressions(step);

                // A loop's own variables belong to the innermost loop that names them, so one
                // that borrows a name from a loop around it loses the outer value when it ends.
                var ownNames = LoopVariableNames(step);
                foreach (var name in ownNames)
                {
                    if (loopNames.Contains(name))
                    {
                        Add(step, MacroProblemCode.LoopVariableShadowed, Strings.Format("Editor.Problem.LoopVariableShadowed", name));
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
                    if (parameter.Kind is ActionParameterKind.Steps)
                    {
                        Visit(parameter.Steps, step.ParameterDefinition(parameter),
                            insideLoops, insideSwitches, insideNames,
                            insideBounded, insideGuard, insideHeld);
                    }
                }
            }
        }

        private void Add(MacroStep step, string code, string message)
        {
            // Two checks can find the same thing about one step; the code is what makes them the
            // same thing, and the message is only how it is said.
            if (!problems.Any(problem => problem.Step == step && problem.Code == code
                && problem.Message == message))
            {
                problems.Add(new MacroProblem(step, code, message,
                    Blocking.Contains(code) ? MacroProblemSeverity.Blocking : MacroProblemSeverity.Hint));
            }
        }
    }
}
