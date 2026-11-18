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

// Changing one row of a table the step carries.
internal static partial class McpPatches
{
    /// <summary>
    /// Changes some columns of one row of a table parameter: a call's arguments, a step's results,
    /// the places a search looks. The row is named by what it already says rather than by its place,
    /// so inserting a row above it does not make this edit land on a different one.
    /// </summary>
    private static Refusal? SetRow(MacroItem macro, JsonObject operation, List<Touch> touches)
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

        if (Columns(operation["match"], declared, out var match) is { } badMatch)
        {
            return badMatch;
        }

        if (match.Count == 0)
        {
            return Refused(McpErrors.BadArgument, "setRow needs match.",
                "Say which row to change by one or more of its columns, for example "
                + "{\"name\":\"次数\"}. A row can also be added with addRow.",
                true, new { step = step!.Type, param = declared.Name });
        }

        var (rows, refusal2) = RowsOf(step!, stored, declared);
        if (refusal2 is not null)
        {
            return refusal2;
        }

        var found = Matching(rows, match);
        if (found.Count == 0)
        {
            return Refused(McpErrors.BadPatch, "No row of this table matches.",
                "Read the rows with whalegenie_get_macro, or add one with addRow.",
                true, new { step = step!.Type, param = declared!.Name, match });
        }

        if (found.Count > 1)
        {
            return Refused(McpErrors.BadPatch, $"{found.Count} rows match, so which one to change is not decided.",
                "Match on more columns so exactly one row is named.",
                true, new { step = step!.Type, param = declared!.Name, match, rows = found.Count });
        }

        foreach (var (column, value) in values)
        {
            found[0].Set(column, value);
        }

        touches.Add(new Touch("setRow", step!));
        return null;
    }
}
