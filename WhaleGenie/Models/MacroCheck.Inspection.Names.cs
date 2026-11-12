using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
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

                foreach (var id in IdsOf(parameter))
                {
                    if (!definedIds.Contains(id))
                    {
                        Add(step, MacroProblemCode.UnknownVariableReference,
                            Strings.Get("Editor.Problem.UnknownVariableReference"));
                    }
                }
            }
        }

        /// <summary>The identities written in one field, including the cells of a row list.</summary>
        private static IEnumerable<string> IdsOf(StepParameter parameter)
        {
            foreach (var id in Expression.ReferenceIds(parameter.Written))
            {
                yield return id;
            }

            foreach (var row in parameter.Rows)
            {
                foreach (var cell in row.Cells.Values)
                {
                    foreach (var id in Expression.ReferenceIds(cell))
                    {
                        yield return id;
                    }
                }
            }
        }

        /// <summary>
        /// A value the reader could not read. A value is a shape, and a shape that could not be
        /// read is the one node that says so, so the check looks for that node rather than for a
        /// sign at the start of a line: the reading has already happened by the time a step is
        /// inspected, and this asks it how it went.
        /// </summary>
        private void CheckExpressions(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.IsRowList
                    || step.ParameterDefinition(parameter) is null)
                {
                    continue;
                }

                if (Fault(parameter.Written) is { } fault)
                {
                    Add(step, MacroProblemCode.BadExpression, Strings.Format("Editor.Problem.BadExpression",
                        fault.Message));
                }
            }
        }

        /// <summary>The first part of a value that did not read, or null when all of it did.</summary>
        private static FaultNode? Fault(ExpressionNode node) => node switch
        {
            FaultNode fault => fault,
            ConcatNode run => run.Parts.Select(Fault).OfType<FaultNode>().FirstOrDefault(),
            _ => null,
        };

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
                foreach (var call in Calls(value))
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

        /// <summary>Every call in a value, including the ones inside another call's arguments.</summary>
        private static IEnumerable<CallNode> Calls(ExpressionNode node) => node switch
        {
            CallNode call => [call, .. call.Arguments.SelectMany(Calls)],
            UnaryNode unary => Calls(unary.Operand),
            BinaryNode binary => [.. Calls(binary.Left), .. Calls(binary.Right)],
            ListNode list => list.Items.SelectMany(Calls),
            ConcatNode run => run.Parts.SelectMany(Calls),
            _ => [],
        };

        /// <summary>
        /// Every value the step holds, whether a box of its own or a cell of a row: a field read in
        /// the fifth row of a call's arguments is as wrong as one in a plain field.
        /// </summary>
        private static IEnumerable<ExpressionNode> Values(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                if (!Formula.IsNothing(parameter.Written))
                {
                    yield return parameter.Written;
                }

                foreach (var cell in parameter.Rows.SelectMany(row => row.Cells.Values))
                {
                    if (!Formula.IsNothing(cell))
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
                if (step.ParameterDefinition(parameter) is not { } definition
                    || Formula.IsNothing(parameter.Written))
                {
                    continue;
                }

                if (parameter.Written is VariableNode whole && known.TryGetValue(whole.Name, out var type))
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
                            definition.LocalLabel, whole.Name));
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

                foreach (var referenced in Expression.References(parameter.Written))
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
