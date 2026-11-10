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

// The columns and rows a table operation has to agree with.
internal static partial class McpPatches
{
    /// <summary>
    /// The table a row operation works on. Everything that could be wrong with naming it — no step,
    /// no such parameter, a parameter that is not a table — is answered here, so the operations
    /// above only deal with rows.
    /// </summary>
    private static Refusal? Table(MacroItem macro, JsonObject operation, out MacroStep? step,
        out StepParameter? stored, out ActionParameter declared)
    {
        step = Located(macro, operation);
        stored = null;
        declared = null!;

        if (step is null)
        {
            return NoStep(operation);
        }

        var name = Text(operation["param"]).Trim();
        if (name.Length == 0)
        {
            return Refused(McpErrors.BadArgument, "A row operation needs param.",
                "The parameter name is the one whalegenie_get_action lists.", true, null);
        }

        stored = step.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        var found = (stored is null ? null : step.ParameterDefinition(stored))
            ?? step.Definition?.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        if (found is null)
        {
            var names = step.Definition?.Parameters.Select(parameter => parameter.Name).ToList() ?? [];
            return Refused(McpErrors.BadArgument,
                $"\"{step.Type}\" has no parameter named \"{name}\".",
                names.Count == 0
                    ? "That action key is not in the catalogue; check it with whalegenie_get_action."
                    : $"Its parameters are: {string.Join(", ", names)}.",
                true, new { step = step.Type, param = name, parameters = names });
        }

        if (!HoldsRows(found.Kind))
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" does not hold rows.",
                "setRow, addRow and removeRow are for a table: a call's arguments, a step's results, "
                + "the places a search looks. A single value is changed with set.",
                true, new { step = step.Type, param = name });
        }

        if (found.Semantics.Columns.Count == 0)
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" declares no columns.",
                "The catalogue has no column list for this table, so a row cannot be written into it.",
                false, new { step = step.Type, param = name });
        }

        declared = found;
        return null;
    }

    /// <summary>
    /// The rows of a table parameter, created empty when the step has never written one. A column
    /// the catalogue does not declare is refused rather than written: nothing would read it, and a
    /// value nobody reads looks like the step ignoring what it was told.
    /// </summary>
    private static (List<StepParameterRow> Rows, Refusal? Refusal) RowsOf(
        MacroStep step, StepParameter? stored, ActionParameter declared)
    {
        if (stored is null)
        {
            return ([], Refused(McpErrors.BadPatch, "This step has no rows of that kind yet.",
                "Add the first one with addRow.", true,
                new { step = step.Type, param = declared.Name }));
        }

        return (stored.Rows, null);
    }

    /// <summary>One row's worth of columns, refusing names the catalogue does not declare.</summary>
    private static Refusal? Columns(JsonNode? node, ActionParameter declared,
        out Dictionary<string, string> columns)
    {
        columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node is not JsonObject written)
        {
            return Refused(McpErrors.BadArgument, "This operation needs an object of columns.",
                $"A row of \"{declared.Name}\" has these columns: "
                + $"{string.Join(", ", declared.Semantics.Columns.Select(column => column.Name))}.",
                true, new { param = declared.Name });
        }

        var known = declared.Semantics.Columns.Select(column => column.Name).ToList();
        var unknown = written.Select(pair => pair.Key)
            .Where(column => !known.Contains(column, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (unknown.Count > 0)
        {
            return Refused(McpErrors.BadArgument,
                $"\"{declared.Name}\" has no column named {string.Join(", ", unknown)}.",
                $"Its columns are: {string.Join(", ", known)}.",
                true, new { param = declared.Name, unknown, columns = known });
        }

        foreach (var (column, value) in written)
        {
            columns[column] = Cell(value);
        }

        return null;
    }

    /// <summary>The rows that say exactly what the given columns say, in the order they are written.</summary>
    private static List<StepParameterRow> Matching(List<StepParameterRow> rows,
        Dictionary<string, string> match)
        => [.. rows.Where(row => match.All(column =>
            string.Equals(row.Text(column.Key).Trim(), column.Value.Trim(),
                StringComparison.OrdinalIgnoreCase)))];

    /// <summary>True for the parameters that hold a table of rows rather than one value.</summary>
    private static bool HoldsRows(ActionParameterKind kind)
        => kind is ActionParameterKind.Region or ActionParameterKind.KeySequence
            or ActionParameterKind.Images or ActionParameterKind.Returns
            or ActionParameterKind.Arguments or ActionParameterKind.Narrow
            or ActionParameterKind.ColourPoints;
}
