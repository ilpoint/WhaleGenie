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
    private sealed partial class Inspection
    {

        private void CheckStepPlacement(MacroStep step, ActionParameter? destination)
        {
            if (StepPlacement.DirectIssue(destination, step) is not { } issue)
            {
                return;
            }

            var (code, message) = issue.Problem switch
            {
                StepPlacementProblem.WrongKind when issue.Destination is { } restricted =>
                    ("WrongStepKind",
                        Strings.Format("Editor.Problem.WrongStepKind",
                            StepPlacement.AllowedSummary(restricted))),
                StepPlacementProblem.HiddenStep when step.Type == "control.case" =>
                    (MacroProblemCode.CaseOutsideSwitch, Strings.Get("Editor.Problem.CaseOutsideSwitch")),
                StepPlacementProblem.HiddenStep =>
                    (MacroProblemCode.HiddenStep, Strings.Format("Editor.Problem.HiddenStep", step.DisplayName)),
                _ => (string.Empty, string.Empty),
            };
            if (message.Length > 0)
            {
                Add(step, code, message);
            }
        }

        private void CheckCondition(MacroStep step)
        {
            if (!ConditionKeys.Contains(step.Type))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(step.Text("condition")))
            {
                Add(step, MacroProblemCode.MissingCondition, Strings.Get("Editor.Problem.MissingCondition"));
            }
        }

        private void CheckLoopControl(MacroStep step, int loops, int switches)
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

            var (code, key) = step.Type == "control.break"
                ? (MacroProblemCode.BreakOutsideLoop, "Editor.Problem.BreakOutsideLoop")
                : (MacroProblemCode.ContinueOutsideLoop, "Editor.Problem.ContinueOutsideLoop");
            Add(step, code, Strings.Get(key));
        }

        private void CheckSwitch(MacroStep step)
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
                Add(step, MacroProblemCode.EmptySwitch, Strings.Get("Editor.Problem.EmptySwitch"));
            }
        }

        /// <summary>
        /// A name a start step chose for a background run's result is not defined until a wait step
        /// takes the run's results back. The steps are walked in the order they are written, so a
        /// read that comes before that wait is said to be reading something that is not there yet.
        /// Cancelling is not a wait: it stops the run without taking any result back.
        /// </summary>
        /// <summary>
        /// Two steps side by side that would claim the same name. Only siblings are compared: two
        /// branches of one switch never both run, so the same handle name in both is a way of not
        /// inventing two names, not a mistake. A sibling pair is different — the second one runs
        /// after the first, and whatever the first left is what gets overwritten.
        /// </summary>
        private void CheckSiblings(IReadOnlyList<MacroStep> siblings)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var step in siblings)
            {
                // Only a call's results are compared: an ordinary step writing a name twice in a row
                // is a plain overwrite the author can see in the macro itself.
                if (!CallsMacro(step))
                {
                    continue;
                }

                foreach (var (_, name) in Wanted(step))
                {
                    if (!results.Add(name))
                    {
                        Add(step, MacroProblemCode.DuplicateResultName,
                            Strings.Format("Editor.Problem.DuplicateResultName", name));
                    }
                }
            }
        }

        /// <summary>
        /// 相对移动与后台这一档合不来：后台方式是把消息投给窗口，屏幕上的指针从来没有动过，而
        /// "偏移多少"问的正是指针现在在哪儿。这是步骤自己的设置就能判定的事，所以写宏的时候就
        /// 说出来，不必等跑到这一步才发现位置是错的。
        /// </summary>
        /// <summary>
        /// A wait with no clock of its own is a legitimate "wait until it happens". It stops being
        /// one when the step retries or an outer block is on a clock: then the retry or the outer
        /// budget is what the user is relying on, and neither can make this wait end sooner.
        /// </summary>
        private void CheckWait(MacroStep step, bool bounded)
        {
            if (step.Definition?.Waits != true || step.Meta.TimeoutMs > 0)
            {
                return;
            }

            if (step.Meta.RetryCount > 0 || bounded)
            {
                Add(step, MacroProblemCode.WaitWithoutTimeout, Strings.Get("Editor.Problem.WaitWithoutTimeout"));
            }
        }

        /// <summary>
        /// The names the run itself keeps are not the macro's to write. A step that defines one of
        /// them would replace a value the rest of the macro reads, so it is said here rather than
        /// found out when the wrong value turns up.
        /// </summary>
        private void CheckReservedNames(MacroStep step)
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
                    && (VariableName.IsSystem(name) || MacroRunner.IsRunStepName(name)))
                {
                    Add(step, MacroProblemCode.ReservedVariable, Strings.Format("Editor.Problem.ReservedVariable", name));
                }
            }
        }

        private void CheckGlobalTarget(MacroStep step)
        {
            string? name = step.Type switch
            {
                "control.setVariable" when string.Equals(step.Text("scope").Trim(), "global",
                    StringComparison.OrdinalIgnoreCase) => step.Text("name").Trim(),
                "control.incrementGlobal" => step.Text("name").Trim(),
                _ => null,
            };
            if (name is { Length: > 0 } && !globals.Any(variable =>
                    string.Equals(variable.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            {
                Add(step, MacroProblemCode.GlobalMustExist, Strings.Format("Editor.Problem.GlobalMustExist", name));
            }
        }

        private void CheckUserFailureKey(MacroStep step)
        {
            if (step.Type != "control.fail")
            {
                return;
            }

            var key = step.Text("failureKey").Trim();
            if (key.Length > 0 && !UserFailureKeys.IsValid(key))
            {
                Add(step, MacroProblemCode.BadUserFailureKey, Strings.Format("Editor.Problem.BadUserFailureKey", key));
            }
        }

        private void CheckScriptInput(MacroStep step)
        {
            // The declaration lives on the step rather than in a list of script action names, so a
            // new scripting language gets the same warning as soon as it carries the switch.
            var declared = step.Parameters.FirstOrDefault(parameter => parameter.Name == "sendsInput");
            if (declared is not null
                && string.Equals(declared.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                Add(step, MacroProblemCode.ScriptInput, Strings.Get("Editor.Problem.ScriptInput"));
            }
        }
    }
}
