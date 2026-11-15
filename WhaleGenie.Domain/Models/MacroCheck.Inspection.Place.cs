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

        private void CheckKnownStep(MacroStep step)
        {
            if (step.Definition is not { } definition)
            {
                Add(step, MacroProblemCode.UnknownAction, Strings.Get("Editor.Problem.UnknownAction"));
                return;
            }

            foreach (var parameter in step.Parameters)
            {
                if (!definition.Parameters.Any(known => known.Name == parameter.Name))
                {
                    Add(step, MacroProblemCode.UnknownParameter, Strings.Format("Editor.Problem.UnknownParameter", parameter.Name));
                }
            }
        }

        /// <summary>
        /// A field the catalogue calls required that the step left empty is marked while it is still
        /// being written. The same rule the engine runs, so a macro that opens without a mark does
        /// not fail on one; a field that only applies under some settings is not asked.
        /// </summary>
        private void CheckMissingFields(MacroStep step)
        {
            if (step.Definition is not { } definition)
            {
                return;
            }

            foreach (var parameter in definition.Parameters)
            {
                // A condition and the macro a call names each have a check of their own; saying it
                // twice would teach the user to ignore one of the two.
                if (parameter.Semantics.MissingValue(step.Text(parameter.Name))
                    && parameter.Semantics.MissingKey is not RunKeys.MissingCondition
                    && !(CallsMacro(step) && parameter.Name == FieldNames.Macro))
                {
                    Add(step, MacroProblemCode.MissingField, Strings.Format("Editor.Problem.MissingField", parameter.LocalLabel));
                }
            }
        }

        /// <summary>
        /// A choice holding a word the catalogue does not offer. The engine refuses it unless the
        /// field says an unknown word falls back, so the step is marked here rather than at the run.
        /// </summary>
        private void CheckOptions(MacroStep step)
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
                    Add(step, MacroProblemCode.BadOption, Strings.Format("Editor.Problem.BadOption",
                        parameter.LocalLabel, written));
                }
            }
        }

        /// <summary>True when a region row gives both a name and the four numbers.</summary>
        private static bool RegionHasBoth(StepParameterRow row)
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
        private void CheckRowColumns(MacroStep step)
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
                    foreach (var column in row.Cells.Keys)
                    {
                        if (!definition.Semantics.Columns.Any(known =>
                                string.Equals(known.Name, column, StringComparison.OrdinalIgnoreCase)))
                        {
                            Add(step, MacroProblemCode.UnknownRowColumn, Strings.Format("Editor.Problem.UnknownRowColumn",
                                definition.LocalLabel, column));
                        }
                    }

                    if (string.Equals(definition.Semantics.Name, "region", StringComparison.Ordinal)
                        && RegionHasBoth(row))
                    {
                        Add(step, MacroProblemCode.ExclusiveRowColumn, Strings.Format("Editor.Problem.ExclusiveRowColumn",
                            definition.LocalLabel));
                    }
                    foreach (var column in definition.Semantics.Columns.Where(column => column.Required))
                    {
                        if (Formula.IsNothing(row.Cell(column.Name)))
                        {
                            Add(step, MacroProblemCode.MissingRowColumn, Strings.Format("Editor.Problem.MissingRowColumn",
                                definition.LocalLabel, column.Name));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A step that says where it works says it once: a record, or the numbers beside it. Both
        /// empty is a step that would work at nothing in particular, which the editor can see
        /// before the macro runs. A click and a drag fall back to the pointer, and a step that reads
        /// the screen falls back to the whole of it, so those say nothing is a real answer.
        /// </summary>
        private void CheckPlace(MacroStep step)
        {
            if (step.Definition is not { } definition
                || step.Type == "window.move"
                || definition.Parameters.Any(parameter => parameter.Name == "useCursor")
                || step.Type is "vision.capture" or "ocr.recognize")
            {
                return;
            }

            // The two colour searches treat the region — and the whole screen — as valid answers:
            // only the place-and-numbers conflict is a mistake then. Reading a pattern at one pixel
            // is the same question as reading one colour there, so both are let through; a step that
            // has a region for another reason (a drag's direction, say) is not.
            if (step.Type is "vision.findColor" or "vision.findColors")
            {
                if (definition.Parameters.FirstOrDefault(parameter => parameter.Name == "place")
                        is { } placeField
                    && step.Text("place").Trim().Length > 0
                    && (step.Text("x").Trim().Length > 0 || step.Text("y").Trim().Length > 0))
                {
                    Add(step, MacroProblemCode.PlaceAndNumbers,
                        Strings.Format("Editor.Problem.PlaceAndNumbers", placeField.LocalLabel));
                }

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
                    Add(step, MacroProblemCode.NoPlace, Strings.Format("Editor.Problem.NoPlace", field.LocalLabel));
                }
                else if (written.Length > 0 && number)
                {
                    // The record wins in the engine, so the numbers the author typed are not read
                    // at all. Saying so is better than letting the step look like it uses both.
                    Add(step, MacroProblemCode.PlaceAndNumbers, Strings.Format("Editor.Problem.PlaceAndNumbers", field.LocalLabel));
                }
            }
        }

        /// <summary>
        /// A mover that says nothing about any side would do nothing. One side is enough: the other
        /// three are deliberately "keep what is there", so only the all-empty case is a mistake.
        /// </summary>
        private void CheckWindowBounds(MacroStep step)
        {
            if (step.Type != "window.move")
            {
                return;
            }

            var hasRect = step.Text("rect").Trim().Length > 0;
            var hasSides = new[] { "x", "y", "width", "height" }
                .Any(name => step.Text(name).Trim().Length > 0);
            if (hasRect && hasSides)
            {
                Add(step, MacroProblemCode.PlaceAndNumbers, Strings.Format("Editor.Problem.PlaceAndNumbers",
                    ActionCatalog.Find(step.Type)?.Parameters
                        .FirstOrDefault(parameter => parameter.Name == "rect")?.LocalLabel
                    ?? "rect"));
            }
            else if (!hasRect && !hasSides)
            {
                Add(step, MacroProblemCode.WindowBounds, Strings.Get("Editor.Problem.WindowBounds"));
            }
        }
    }
}
