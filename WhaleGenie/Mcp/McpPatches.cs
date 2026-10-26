using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>
/// Step-level edits: change one parameter, insert a step, remove one, move one. It exists because
/// resending a whole macro to change a single number is how a long macro gets truncated on the way
/// in or edited in the wrong place — the client ends up rewriting what it did not mean to touch.
///
/// A patch is one write or none. Every operation is applied to a copy, each insertion is held to the
/// same child-list rules the editor uses, and a refusal leaves the draft exactly as it was, so a
/// wrong third operation cannot leave the first two behind.
/// </summary>
internal static class McpPatches
{
    private const string TopLevel = "";

    /// <summary>One step a patch touched, named after the ids are settled so the answer is usable.</summary>
    internal sealed record Touch(string Op, MacroStep Step);

    /// <summary>Why a patch was refused. Nothing has been written when this comes back.</summary>
    internal sealed record Refusal(
        string Code, string Message, string Hint, bool Retryable, object? Details);

    internal sealed record Outcome(MacroItem? Macro, IReadOnlyList<Touch> Touches, Refusal? Refused);

    /// <summary>
    /// Applies every operation or returns the first refusal. The macro handed in is never changed:
    /// the caller commits <see cref="Outcome.Macro"/> itself.
    /// </summary>
    public static Outcome Apply(MacroItem macro, JsonArray operations)
    {
        MacroItem copy;
        try
        {
            // The same deep copy the draft snapshot uses: a patch that is going to be refused must
            // not have half of itself sitting in the draft already.
            copy = MacroItem.FromJson(macro.ToJson());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return new Outcome(null, [],
                Refused(McpErrors.BadPatch, $"The macro could not be copied: {error.Message}",
                    "Use whalegenie_get_macro and whalegenie_put_macro for this macro.", true, null));
        }

        var touches = new List<Touch>();
        var index = 0;
        foreach (var node in operations)
        {
            index++;
            if (node is not JsonObject operation)
            {
                return new Outcome(null, touches, Refused(McpErrors.BadPatch,
                    $"Operation {index} is not an object.",
                    "Each operation is {op, ...} with op one of set, insert, remove or move.", true,
                    new { index }));
            }

            var refusal = Run(copy, operation, touches);
            if (refusal is not null)
            {
                return new Outcome(null, touches, refusal with { Details = refusal.Details ?? new { index } });
            }
        }

        // New steps came in without ids, and a patch that inserted one before another step existed
        // could have reused a name. Settling here is what makes every later answer able to point at
        // a step, and what the run reports as step.<id>.
        Settle(copy);
        return new Outcome(copy, touches, null);
    }

    /// <summary>
    /// Gives every step of a macro and its submacros an id of its own. A step written by a client
    /// arrives without one, and a run reports such a step as <c>step..outcome</c>, so the ids are
    /// issued here rather than waiting for someone to open the macro in the editor.
    /// </summary>
    public static void Settle(MacroItem macro) => StepIds.SettleAcross(Documents(macro));

    private static Refusal? Run(MacroItem macro, JsonObject operation, List<Touch> touches)
        => Text(operation["op"]).Trim().ToLowerInvariant() switch
        {
            "set" => Set(macro, operation, touches),
            // The operation name is read without regard to case, so the labels a person sees
            // (setRow, addRow, removeRow) and the spelling a client sends both land here.
            "setrow" => SetRow(macro, operation, touches),
            "addrow" => AddRow(macro, operation, touches),
            "removerow" => RemoveRow(macro, operation, touches),
            "insert" => Insert(macro, operation, touches),
            "remove" => Remove(macro, operation, touches),
            "move" => Move(macro, operation, touches),
            "" => Refused(McpErrors.BadPatch, "An operation has no op.",
                "op is set, setRow, addRow, removeRow, insert, remove or move.", true, null),
            var other => Refused(McpErrors.BadPatch, $"There is no operation \"{other}\".",
                "op is set, setRow, addRow, removeRow, insert, remove or move.", true,
                new { op = other }),
        };

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
            var names = step.Definition?.Parameters.Select(parameter => parameter.Name).ToList() ?? [];
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
            found[0].Columns[column] = value;
        }

        touches.Add(new Touch("setRow", step!));
        return null;
    }

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
            Columns = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase),
        });

        touches.Add(new Touch("addRow", step!));
        return null;
    }

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
            or ActionParameterKind.Arguments or ActionParameterKind.Narrow;

    /// <summary>Inserts one step into a named list, held to the child-list rules of that list.</summary>
    private static Refusal? Insert(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        if (operation["step"] is not JsonObject body)
        {
            return Refused(McpErrors.BadArgument, "insert needs step.",
                "The step is one action object, the same shape a macro holds in its steps array.",
                true, null);
        }

        MacroStep step;
        try
        {
            step = MacroStep.FromJson(body);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return Refused(McpErrors.BadJson, $"That step could not be read: {error.Message}",
                "A step is {type, params} with type an action key from the catalogue.", true, null);
        }

        if (step.Type.Trim().Length == 0)
        {
            return Refused(McpErrors.BadArgument, "The step has no type.",
                "Every step names its action key, for example control.delay.", true, null);
        }

        var parent = Text(operation["parent"]);
        if (!Resolve(macro, parent, out var list, out var destination, out var pathError))
        {
            return Refused(McpErrors.BadArgument, pathError!,
                "A parent is a path such as 0.then or 0.then.2.else; leave it empty for the top level.",
                true, new { parent });
        }

        if (Fits(destination, step, parent) is { } refused)
        {
            return refused;
        }

        var at = Position(operation["index"], list.Count);
        list.Insert(at, step);
        touches.Add(new Touch("insert", step));
        return null;
    }

    private static Refusal? Remove(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var id = Text(operation["stepId"]).Trim();
        if (!Locate(macro.Steps, id, out var list, out var at, out var step))
        {
            return NoStep(operation);
        }

        list.RemoveAt(at);
        touches.Add(new Touch("remove", step));
        return null;
    }

    private static Refusal? Move(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var id = Text(operation["stepId"]).Trim();
        if (!Locate(macro.Steps, id, out var from, out var at, out var step))
        {
            return NoStep(operation);
        }

        var parent = Text(operation["parent"]);
        if (!Resolve(macro, parent, out var to, out var destination, out var pathError))
        {
            return Refused(McpErrors.BadArgument, pathError!,
                "A parent is a path such as 0.then; leave it empty for the top level.", true, new { parent });
        }

        if (Fits(destination, step, parent) is { } refused)
        {
            return refused;
        }

        from.RemoveAt(at);
        to.Insert(Position(operation["index"], to.Count), step);
        touches.Add(new Touch("move", step));
        return null;
    }

    /// <summary>
    /// The same question the editor asks before it accepts a drop. A list that names its child kinds
    /// takes only those; an ordinary list takes visible actions.
    /// </summary>
    private static Refusal? Fits(ActionParameter? destination, MacroStep step, string parent)
    {
        if (StepPlacement.FirstIssue(destination, [step]) is not { } issue)
        {
            return null;
        }

        // A key that is not in the catalogue is a different mistake from a step in the wrong list:
        // one is a typo to look up, the other is a placement to change.
        if (issue.Problem == StepPlacementProblem.UnknownStep)
        {
            return Refused(McpErrors.UnknownAction,
                $"\"{step.Type}\" is not an action key in the catalogue.",
                "Action keys come from the catalogue; search it with whalegenie_list_actions.",
                true, new { step = step.Type, parent });
        }

        var allowed = destination is { ChildKeys.Count: > 0 } ? destination.ChildKeys.ToList() : [];
        var where = parent.Length == 0 ? "the top level" : $"\"{parent}\"";
        var message = issue.Problem == StepPlacementProblem.HiddenStep
            ? $"\"{step.Type}\" is a hidden action and cannot stand in {where}."
            : $"\"{step.Type}\" does not belong in {where}.";
        var hint = allowed.Count > 0
            ? $"That list only accepts: {string.Join(", ", allowed)}."
            : "An ordinary list takes visible actions, plus the grouping container the editor makes.";

        return Refused(McpErrors.BadPlacement, message, hint, true,
            new { step = step.Type, parent, allowed });
    }

    /// <summary>
    /// The list a parent path names, and the parameter that declares it. An empty path is the
    /// macro's own list; anything else is step indexes and list names in turn: <c>0.then.2.else</c>.
    /// </summary>
    private static bool Resolve(MacroItem macro, string path, out List<MacroStep> list,
        out ActionParameter? destination, out string? error)
    {
        list = macro.Steps;
        destination = null;
        error = null;

        var trimmed = (path ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed == TopLevel)
        {
            return true;
        }

        var tokens = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length % 2 != 0)
        {
            error = $"\"{trimmed}\" has to end with the name of a list, such as 0.then.";
            return false;
        }

        var current = macro.Steps;
        for (var index = 0; index < tokens.Length; index += 2)
        {
            if (!int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var at) || at < 0 || at >= current.Count)
            {
                error = $"\"{tokens[index]}\" is not a position in the list before it.";
                return false;
            }

            var holder = current[at];
            var name = tokens[index + 1];
            var parameter = holder.StepLists.FirstOrDefault(candidate => candidate.Name == name);
            if (parameter is null)
            {
                var names = holder.StepLists.Select(candidate => candidate.Name).ToList();
                error = names.Count == 0
                    ? $"\"{holder.Type}\" holds no lists, so it cannot be a parent."
                    : $"\"{name}\" is not a list of \"{holder.Type}\"; its lists are "
                        + $"{string.Join(", ", names)}.";
                return false;
            }

            current = parameter.Steps;
            destination = holder.ParameterDefinition(parameter);
        }

        list = current;
        return true;
    }

    private static MacroStep? Located(MacroItem macro, JsonObject operation)
    {
        var id = Text(operation["stepId"]).Trim();
        return Locate(macro.Steps, id, out _, out _, out var step) ? step : null;
    }

    /// <summary>Where one step sits: the list holding it and its place in that list.</summary>
    private static bool Locate(List<MacroStep> steps, string id, out List<MacroStep> list,
        out int index, out MacroStep step)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            if (id.Length > 0 && string.Equals(steps[at].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                list = steps;
                index = at;
                step = steps[at];
                return true;
            }

            foreach (var parameter in steps[at].StepLists)
            {
                if (Locate(parameter.Steps, id, out list, out index, out step))
                {
                    return true;
                }
            }
        }

        list = steps;
        index = -1;
        step = null!;
        return false;
    }

    /// <summary>Every step list in the macro and in the submacros it carries, for one set of ids.</summary>
    private static IEnumerable<IEnumerable<MacroStep>> Documents(MacroItem macro)
    {
        yield return macro.Steps;
        foreach (var sub in macro.SubMacros)
        {
            foreach (var document in Documents(sub))
            {
                yield return document;
            }
        }
    }

    private static int Position(JsonNode? node, int count)
        => node is JsonValue value && value.TryGetValue<int>(out var at)
            ? Math.Clamp(at, 0, count)
            : count;

    /// <summary>The value as the field holds it: text, or the number the text says.</summary>
    private static string Written(JsonNode? value) => value switch
    {
        null => string.Empty,
        JsonValue number when number.TryGetValue<decimal>(out var written)
            => written.ToString(CultureInfo.InvariantCulture),
        JsonValue flag when flag.TryGetValue<bool>(out var answer) => answer ? "true" : "false",
        JsonValue text when text.TryGetValue<string>(out var written) => written,
        _ => value.ToJsonString(),
    };

    private static Refusal NoStep(JsonObject operation)
    {
        var id = Text(operation["stepId"]).Trim();
        return Refused(McpErrors.BadArgument, $"There is no step with the id \"{id}\".",
            "Step ids come from whalegenie_get_macro; a step written without one gets a new one on the next write.",
            true, new { stepId = id });
    }

    private static Refusal Refused(string code, string message, string hint, bool retryable,
        object? details) => new(code, message, hint, retryable, details);

    private static string Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    /// <summary>
    /// One cell of a row, as the file writes it. A number and a truth value are written the way the
    /// editor writes them rather than refused, because a client sending <c>3</c> for a count means
    /// the same thing as one sending <c>"3"</c>.
    /// </summary>
    private static string Cell(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return string.Empty;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return value.TryGetValue<bool>(out var flag) && flag ? "true" : string.Empty;
    }
}
