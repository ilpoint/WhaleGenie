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

        private void CheckCall(MacroStep step, bool guarded)
        {
            if (!CallsMacro(step))
            {
                return;
            }

            var name = step.Text("macro").Trim();
            if (name.Length == 0)
            {
                Add(step, MacroProblemCode.NoMacro, Strings.Get("Editor.Problem.NoMacro"));
                return;
            }

            if (!reachable.TryGetValue(name, out var called))
            {
                Add(step, MacroProblemCode.MacroNotFound, Strings.Format("Editor.Problem.MacroNotFound", name));
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
                Add(step, MacroProblemCode.UnguardedRecursion, Strings.Get("Editor.Problem.UnguardedRecursion"));
            }

            foreach (var row in ArgumentRows(step))
            {
                var (passed, _, _) = Argument(row);
                if (passed.Length > 0 && called.Parameter(passed) is null)
                {
                    Add(step, MacroProblemCode.UndeclaredArgument, Strings.Format("Editor.Problem.UndeclaredArgument", passed, name));
                }
            }

            foreach (var (result, _) in Wanted(step))
            {
                if (!called.Offers(result))
                {
                    Add(step, MacroProblemCode.UndeclaredResult, Strings.Format("Editor.Problem.UndeclaredResult", result, name));
                }
            }
        }

        /// <summary>
        /// A call passes values the called macro asked for. A value whose kind is known and does not
        /// fit what was asked for is worth saying before the run: the call would read it as text or
        /// refuse it, and neither is visible from the call's own form.
        /// </summary>
        private void CheckCallArguments(MacroStep step)
        {
            if (!CallsMacro(step)
                || reachable.GetValueOrDefault(step.Text("macro").Trim()) is not { } called)
            {
                return;
            }

            var faults = DeclarationFaults(called);
            foreach (var fault in DeclarationFaultProblems(called))
            {
                Add(step, fault.Code, fault.Message);
            }

            // A call whose declaration cannot be used will fail before the arguments matter; the
            // declaration messages are the ones worth showing.
            if (faults.Count > 0)
            {
                return;
            }

            var filled = new Dictionary<string, (ExpressionNode Value, bool Given)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var row in ArgumentRows(step))
            {
                var (passed, value, given) = Argument(row);
                if (passed.Length == 0)
                {
                    continue;
                }

                if (called.Parameter(passed) is null)
                {
                    Add(step, MacroProblemCode.UndeclaredArgument, Strings.Format("Editor.Problem.UndeclaredArgument", passed,
                        step.Text("macro").Trim()));
                    continue;
                }

                if (!filled.TryAdd(passed, (value, given)))
                {
                    Add(step, MacroProblemCode.DuplicateArgument, Strings.Format("Editor.Problem.DuplicateArgument", passed));
                }
            }

            foreach (var declared in CallableSignature.Arguments(called))
            {
                var name = declared.Name;
                if (!filled.TryGetValue(name, out var input))
                {
                    if (declared.Required)
                    {
                        Add(step, MacroProblemCode.MissingArgument, Strings.Format("Editor.Problem.MissingArgument", name,
                            step.Text("macro").Trim()));
                    }

                    continue;
                }

                if (input.Given && !Formula.IsNothing(input.Value))
                {
                    Add(step, MacroProblemCode.GivenWithValue, Strings.Format("Editor.Problem.GivenWithValue", name));
                }

                if (Formula.IsNothing(input.Value))
                {
                    continue;
                }

                if (ConstantValue(input.Value) is { } value
                    && !ValueRules.Accepts(declared.Type, value))
                {
                    Add(step, MacroProblemCode.ArgumentType, Strings.Format("Editor.Problem.ArgumentType", name,
                        Strings.Get("ActionType." + declared.Type, declared.Type.ToString())));
                    continue;
                }

                if (WholeName(input.Value) is not { } whole
                    || !known.TryGetValue(whole, out var type)
                    || !ObviouslyWrong(declared.Type, type))
                {
                    continue;
                }

                Add(step, MacroProblemCode.ArgumentType, Strings.Format("Editor.Problem.ArgumentType", name,
                    Strings.Get("ActionType." + declared.Type, declared.Type.ToString())));
            }
        }

        /// <summary>
        /// Two ticks writing the same name: the second silently replaces the first, so the value the
        /// step reports is not the one the row was talking about.
        /// </summary>
        private void CheckDuplicateResults(MacroStep step)
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
                        Add(step, MacroProblemCode.DuplicateResultName, Strings.Format("Editor.Problem.DuplicateResultName", name));
                    }
                }
            }
        }

        /// <summary>
        /// A step may only ask for results its action declares, and each result key and destination
        /// name may appear once. Calls are checked against the called macro's signature elsewhere;
        /// this check covers the action catalogue's own returns and the destination names both kinds
        /// of step write.
        /// </summary>
        private void CheckActionResultRequests(MacroStep step)
        {
            if (step.Definition is not { } definition)
            {
                return;
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in step.Parameters.Where(parameter =>
                         parameter.Kind is ActionParameterKind.Returns))
            {
                foreach (var (key, name) in step.ReturnedResults(parameter))
                {
                    if (!VariableName.IsName(name))
                    {
                        Add(step, MacroProblemCode.BadResultName, Strings.Format("Editor.Problem.BadResultName", name));
                    }

                    if (key.Length > 0 && !keys.Add(key))
                    {
                        Add(step, MacroProblemCode.DuplicateResultKey, Strings.Format("Editor.Problem.DuplicateResultKey", key));
                    }

                    if (key.Length > 0
                        && definition.Semantics.SignatureSource is ActionSignatureSource.Own
                        && definition.Semantics.Return(key) is null)
                    {
                        Add(step, MacroProblemCode.UndeclaredActionResult, Strings.Format("Editor.Problem.UndeclaredActionResult", key,
                            definition.LocalName));
                    }
                }
            }
        }
    }
}
