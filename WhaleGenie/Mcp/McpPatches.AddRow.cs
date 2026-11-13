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

// Adding a row to the list a step carries.
internal static partial class McpPatches
{
    /// <summary>
    /// Adds one row to a table parameter. Every column the catalogue calls required has to be
    /// filled in: a row missing one is a row the action would skip in silence, which reads as the
    /// step ignoring what was written.
    /// </summary>
    private static Refusal? AddRow(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var refusal = Table(macro, operation, out var step, out var stored, out var declared);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Columns(operation["values"], declared, out var values) is { } badValues)
        {
            return badValues;
        }

        var missing = declared!.Semantics.Columns
            .Where(column => column.Required && !values.ContainsKey(column.Name))
            .Select(column => column.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return Refused(McpErrors.BadArgument,
                $"A row of \"{declared.Name}\" has to fill in {string.Join(", ", missing)}.",
                "Every column the catalogue calls required is part of what a row is.",
                true, new { step = step!.Type, param = declared.Name, missing });
        }

        // The first row of a table that has never had one creates the table. Telling a client to
        // "add the first one with addRow" and then refusing that very call was a loop with no exit.
        if (stored is null)
        {
            stored = new StepParameter { Name = declared.Name, Kind = declared.Kind };
            step!.Parameters.Add(stored);
        }

        var (rows, refusal2) = RowsOf(step!, stored, declared);
        if (refusal2 is not null)
        {
            return refusal2;
        }

        // Two rows that say the same thing in every required column name the same thing twice, and
        // which of them applies would be a guess — the same guess the run refuses to make.
        if (Matching(rows, values).Count > 0)
        {
            return Refused(McpErrors.BadPatch, "A row like this is already in the table.",
                "Change that row with setRow instead of adding a second one.",
                true, new { step = step!.Type, param = declared!.Name, values });
        }

        rows.Add(new StepParameterRow
        {
            Cells = new Dictionary<string, ExpressionNode>(values, StringComparer.OrdinalIgnoreCase),
        });

        touches.Add(new Touch("addRow", step!));
        return null;
    }
}
