using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;


namespace WhaleGenie.Mcp;

// Changing one parameter of a step, whatever kind of field it is.
internal static partial class McpPatches
{
    /// <summary>Changes one parameter's value, which is the edit that used to cost a whole macro.</summary>
    private static Refusal? Set(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var step = Located(macro, operation);
        if (step is null)
        {
            return NoStep(operation);
        }

        var name = Text(operation["param"]).Trim();
        if (name.Length == 0)
        {
            return Refused(McpErrors.BadArgument, "set needs param.",
                "The parameter name is the one whalegenie_get_action lists.", true, null);
        }

        if (!operation.TryGetPropertyValue("value", out var value))
        {
            return Refused(McpErrors.BadArgument, "set needs value.",
                "Send the new value; null clears the field.", true, null);
        }

        var existing = step.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        var declared = (existing is null ? null : step.ParameterDefinition(existing))
            ?? step.Definition?.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        if (declared is null)
        {
            // An action key that is not in the catalogue is a different mistake from a parameter
            // this action does not have, and a client branches on the code to tell them apart.
            if (step.Definition is null)
            {
                return Refused(McpErrors.UnknownAction,
                    $"There is no action \"{step.Type}\" in the catalogue.",
                    "Action keys come from the catalogue; check it with whalegenie_get_action.",
                    true, new { step = step.Type });
            }

            var names = step.Definition.Parameters.Select(parameter => parameter.Name).ToList();
            return Refused(McpErrors.BadArgument,
                $"\"{step.Type}\" has no parameter named \"{name}\".",
                names.Count == 0
                    ? "That action key is not in the catalogue; check it with whalegenie_get_action."
                    : $"Its parameters are: {string.Join(", ", names)}.",
                true, new { step = step.Type, param = name, parameters = names });
        }

        if (declared.Kind == ActionParameterKind.Steps)
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" holds steps, not one value.",
                "Use insert or remove with parent set to the path of that list, such as 0." + name + ".",
                true, new { step = step.Type, param = name });
        }

        if (HoldsRows(declared.Kind))
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" holds a table of rows.",
                "A table is changed one row at a time: setRow changes the columns of a row, addRow "
                + "adds one, removeRow takes one out.",
                true, new { step = step.Type, param = name });
        }

        // A window rule is the one record written as an object in the file, so it is the one that can
        // be sent whole. A point or a rectangle is written as one value instead — a variable holding
        // the record, or a field of it — and an object there is a shape the run reads as something
        // else, which is why it is refused with the way it is written rather than half-accepted.
        var recordFields = ActionKnowledge.FieldsOf(declared.Shape);
        if (recordFields.Count > 0 && declared.Shape is not ActionValueShape.WindowRule)
        {
            if (value is JsonObject)
            {
                return Refused(McpErrors.BadPatch,
                    $"\"{name}\" is a {declared.Shape} written as one value, not as an object.",
                    "Send one value: a variable holding the record, or field($record, \"name\") for one "
                    + $"field of it. Its fields are {string.Join(", ", recordFields)}.",
                    true, new { step = step.Type, param = name, fields = recordFields });
            }
        }

        if (declared.Shape is ActionValueShape.WindowRule && value is JsonObject window)
        {
            var unknown = window.Select(field => field.Key)
                .Where(field => !recordFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (unknown.Count > 0)
            {
                return Refused(McpErrors.BadArgument,
                    $"\"{name}\" has no field {string.Join(", ", unknown)}.",
                    $"A window rule holds: {string.Join(", ", recordFields)}.",
                    true, new { step = step.Type, param = name, unknown, fields = recordFields });
            }
        }

        var text = Written(value);
        if (existing is null)
        {
            step.Parameters.Add(new StepParameter
            {
                Name = name,
                Kind = declared.Kind,
                Value = text,
            });
        }
        else
        {
            existing.Value = text;
        }

        touches.Add(new Touch("set", step));
        return null;
    }
}
