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

        private void CheckNames(MacroStep step, bool open)
        {
            if (open)
            {
                return;
            }

            foreach (var parameter in step.Parameters)
            {
                // Step lists hold other steps rather than a value of their own, and text written
                // in another language spells its own variables with a $.
                if (parameter.Kind is ActionParameterKind.Steps
                    || step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                foreach (var name in ReadsOf(step, parameter))
                {
                    if (!defined.Contains(name))
                    {
                        Add(step, MacroProblemCode.UndefinedVariable, Strings.Format("Editor.Problem.UndefinedVariable", name));
                    }
                }
            }
        }

        /// <summary>
        /// A whole-cell value that starts with a dollar sign is read as an expression, so one that
        /// the language cannot parse would stop the run. Plain text and the escaped dollar are left
        /// alone: those are values, not formulae.
        /// </summary>
        private void CheckExpressions(MacroStep step)
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
                    foreach (var expression in FormulaText.Formulas(parameter.Value))
                    {
                        if (!Expression.TryParse(expression, out _, out var fault))
                        {
                            Add(step, MacroProblemCode.BadExpression, Strings.Format("Editor.Problem.BadExpression",
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
                    Add(step, MacroProblemCode.BadExpression, Strings.Format("Editor.Problem.BadExpression",
                        error?.Detail ?? written));
                }
            }
        }

        /// <summary>
        /// A field read of a record whose declaration names its fields. <c>field($point, "x")</c>
        /// where the record declares `x` and `y` is a mistake the editor can see now, rather than one
        /// the run reports after it has already sent the input before it. A record whose declaration
        /// says nothing about its fields is left alone: checking against names nobody wrote down
        /// would be a guess, and a guessed mark teaches the user to ignore the real ones.
        /// </summary>
        private void CheckFieldNames(MacroStep step)
        {
            foreach (var value in Values(step))
            {
                foreach (var tree in Trees(value))
                {
                    foreach (var call in Calls(tree))
                    {
                        if (!string.Equals(call.Name, FieldFunction,
                                StringComparison.OrdinalIgnoreCase)
                            || call.Arguments.Count != 2
                            || call.Arguments[0] is not VariableNode variable
                            || call.Arguments[1] is not TextNode name)
                        {
                            continue;
                        }

                        if (!recordFields.TryGetValue(variable.Name, out var declared)
                            || declared.Any(field => string.Equals(field, name.Text,
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        Add(step, MacroProblemCode.UnknownField, Strings.Format("Editor.Problem.UnknownField",
                            name.Text, variable.Name, string.Join(", ", declared)));
                    }
                }
            }
        }

        /// <summary>
        /// The formulas a value holds: the whole of it when it is one, and each braced part of it
        /// when it is text. A field read is written either way — <c>field($p, "x")</c> on its own,
        /// or inside a line that says what it is for.
        /// </summary>
        private static IEnumerable<ExpressionNode> Trees(string value)
        {
            if (Expression.TryParse(value, out var whole, out _) && whole is not null)
            {
                yield return whole;
            }

            foreach (var part in FormulaText.Formulas(value))
            {
                if (Expression.TryParse(part, out var tree, out _) && tree is not null)
                {
                    yield return tree;
                }
            }
        }

        /// <summary>Every call in a formula, including the ones inside another call's arguments.</summary>
        private static IEnumerable<CallNode> Calls(ExpressionNode node) => node switch
        {
            CallNode call => [call, .. call.Arguments.SelectMany(Calls)],
            UnaryNode unary => Calls(unary.Operand),
            BinaryNode binary => [.. Calls(binary.Left), .. Calls(binary.Right)],
            ListNode list => list.Items.SelectMany(Calls),
            _ => [],
        };

        /// <summary>
        /// Every value the step holds, whether a box of its own or a cell of a row: a field read in
        /// the fifth row of a call's arguments is as wrong as one in a plain field.
        /// </summary>
        private static IEnumerable<string> Values(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Value.Trim().Length > 0)
                {
                    yield return parameter.Value;
                }

                foreach (var cell in parameter.Rows.SelectMany(row => row.Columns.Values))
                {
                    if (cell.Trim().Length > 0)
                    {
                        yield return cell;
                    }
                }
            }
        }

        /// <summary>
        /// What a value is has to match what the field takes. Only names whose kind the steps
        /// themselves declare are judged — a hit is a record, a line of writing is text — because
        /// guessing at a name nobody declared would teach the user to ignore the marks.
        /// </summary>
        private void CheckValueTypes(MacroStep step)
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
                        Add(step, MacroProblemCode.RecordWhereNumber, Strings.Format("Editor.Problem.RecordWhereNumber",
                            definition.LocalLabel));
                    }
                    else if (definition.Shape
                             is ActionValueShape.Point or ActionValueShape.Rect
                                 or ActionValueShape.Record
                             && type != ActionReturnType.Record
                             && type != ActionReturnType.List)
                    {
                        Add(step, MacroProblemCode.NotARecord, Strings.Format("Editor.Problem.NotARecord",
                            definition.LocalLabel, name));
                    }
                }

                // A record named inside text becomes JSON where the user meant a title or a line:
                // a field read as one value keeps the record whole, and the window fields read it
                // as a window, so those are left alone.
                if (definition.ReadMode != ActionReadMode.Text
                    || definition.Shape is ActionValueShape.Point or ActionValueShape.Rect
                        or ActionValueShape.Record
                    || definition.Picker == ActionPickerKind.Window)
                {
                    continue;
                }

                foreach (var referenced in FormulaText.Names(value, ActionReadMode.Text))
                {
                    if (known.TryGetValue(referenced, out var kind)
                        && kind == ActionReturnType.Record)
                    {
                        Add(step, MacroProblemCode.RecordInText, Strings.Format("Editor.Problem.RecordInText", referenced,
                            definition.LocalLabel));
                    }
                }
            }
        }
    }
}
