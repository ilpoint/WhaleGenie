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

// Removing a row from the list a step carries.
internal static partial class McpPatches
{
    /// <summary>
    /// Takes one row out of a table parameter, named the way setRow names it. A table with one
    /// answer for a name has no way to say which of two identical rows to delete.
    /// </summary>
    private static Refusal? RemoveRow(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var refusal = Table(macro, operation, out var step, out var stored, out var declared);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Columns(operation["match"], declared, out var match) is { } badMatch)
        {
            return badMatch;
        }

        if (match.Count == 0)
        {
            return Refused(McpErrors.BadArgument, "removeRow needs match.",
                "Say which row to remove by one or more of its columns, for example "
                + "{\"name\":\"次数\"}.",
                true, new { step = step!.Type, param = declared.Name });
        }

        var (rows, refusal2) = RowsOf(step!, stored, declared);
        if (refusal2 is not null)
        {
            return refusal2;
        }

        var found = Matching(rows, match);
        if (found.Count != 1)
        {
            return Refused(McpErrors.BadPatch,
                found.Count == 0 ? "No row of this table matches." : $"{found.Count} rows match.",
                found.Count == 0
                    ? "Read the rows with whalegenie_get_macro."
                    : "Match on more columns so exactly one row is named.",
                true, new { step = step!.Type, param = declared!.Name, match, rows = found.Count });
        }

        rows.Remove(found[0]);
        touches.Add(new Touch("removeRow", step!));
        return null;
    }
}
