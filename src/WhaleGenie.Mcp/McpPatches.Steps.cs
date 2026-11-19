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

// Inserting, removing and moving a step, and whether it fits where it lands.
internal static partial class McpPatches
{
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
        if (!Resolve(Scope(macro, operation).Steps, parent, out var list, out var destination,
                out var pathError))
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
        var scope = Scope(macro, operation);
        if (!Locate(scope.Steps, id, out var from, out var at, out var step))
        {
            return NoStep(operation);
        }

        var parent = Text(operation["parent"]);
        if (!Resolve(scope.Steps, parent, out var to, out var destination, out var pathError))
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
    private static bool Resolve(List<MacroStep> root, string path, out List<MacroStep> list,
        out ActionParameter? destination, out string? error)
    {
        list = root;
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

        var current = root;
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
}
