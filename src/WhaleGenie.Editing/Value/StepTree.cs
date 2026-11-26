using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>
/// Walking, copying and serialising a step tree. The editor's commands still own selection, undo and
/// the visible rows; these functions only answer what the tree holds, and they do not read a view.
/// </summary>
internal static class StepTree
{
    /// <summary>The list and index a step id names, or null when the tree has no such id.</summary>
    public static (IList<MacroStep> List, int Index)? Find(IList<MacroStep> roots, string stepId)
    {
        foreach (var (list, index) in Walk(roots))
        {
            if (string.Equals(list[index].Id, stepId, StringComparison.Ordinal))
            {
                return (list, index);
            }
        }

        return null;
    }

    public static MacroStep? FindStep(IList<MacroStep> roots, string stepId)
        => Find(roots, stepId) is { } found ? found.List[found.Index] : null;

    /// <summary>The distinct steps named by ids, in the order the ids were given.</summary>
    public static List<MacroStep> FindSteps(IList<MacroStep> roots, IReadOnlyList<string> stepIds)
    {
        var steps = new List<MacroStep>();
        foreach (var id in stepIds)
        {
            if (FindStep(roots, id) is { } step && !steps.Contains(step))
            {
                steps.Add(step);
            }
        }

        return steps;
    }

    /// <summary>A deep copy of one step and its nested steps; changing either side cannot reach the other.</summary>
    public static MacroStep Clone(MacroStep step) => new()
    {
        Type = step.Type,
        Id = step.Id,
        Parameters = step.Parameters.Select(Clone).ToList(),
        Meta = step.Meta,
        IsExpanded = step.IsExpanded,
    };

    /// <summary>Writes steps as the JSON array copy and cut put on the clipboard.</summary>
    public static string Serialize(IEnumerable<MacroStep> steps)
    {
        var array = new JsonArray();
        foreach (var step in steps)
        {
            array.Add(step.ToJson());
        }

        return array.ToJsonString();
    }

    /// <summary>Rebuilds steps from clipboard text, or null when it is not a step array.</summary>
    public static List<MacroStep>? Deserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonArray array)
            {
                return null;
            }

            var steps = new List<MacroStep>();
            foreach (var node in array)
            {
                if (node is JsonObject obj)
                {
                    steps.Add(MacroStep.FromJson(obj));
                }
            }

            return steps.Count > 0 ? steps : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Moves one step inside its list; false when the target is the place it already is.</summary>
    public static bool Move(IList<MacroStep> list, int from, int to)
    {
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || to == from)
        {
            return false;
        }

        var step = list[from];
        list.RemoveAt(from);
        list.Insert(to, step);
        return true;
    }

    /// <summary>Every step and the list it sits in, parents before their children.</summary>
    public static IEnumerable<(IList<MacroStep> List, int Index)> Walk(IList<MacroStep> steps)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            yield return (steps, index);
            foreach (var children in steps[index].Parameters
                         .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
                         .SelectMany(parameter => Walk(parameter.Steps)))
            {
                yield return children;
            }
        }
    }

    private static StepParameter Clone(StepParameter parameter) => new()
    {
        Name = parameter.Name,
        Kind = parameter.Kind,
        Written = parameter.Written,
        Jitter = parameter.Jitter,
        Steps = parameter.Steps.Select(Clone).ToList(),
        Rows = parameter.Rows.Select(row => row.Copy()).ToList(),
    };
}
