using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>Which list rejected a step, and why.</summary>
internal enum StepPlacementProblem
{
    WrongKind,
    HiddenStep,
    UnknownStep,
}

/// <summary>The first step in a proposed insertion that its destination does not accept.</summary>
internal sealed record StepPlacementIssue(
    StepPlacementProblem Problem,
    MacroStep Step,
    ActionParameter? Destination);

/// <summary>
/// Applies the child-list rules the action catalogue declares. The editor uses this before every
/// insertion or move so adding, dropping, pasting, grouping and lifting cannot disagree.
/// </summary>
internal static class StepPlacement
{
    /// <summary>The generated grouping container is the one hidden action a normal list can hold.</summary>
    public const string SequenceKey = "control.sequence";

    /// <summary>True when every step and everything nested under it belongs at this destination.</summary>
    public static bool Accepts(ActionParameter? destination, IEnumerable<MacroStep> steps)
        => FirstIssue(destination, steps) is null;

    /// <summary>
    /// Finds the first step the destination does not accept. A list with named child kinds is
    /// exclusive; an ordinary list takes visible actions, plus the grouping container the editor
    /// creates itself. Nested lists are held to their own declarations as well.
    /// </summary>
    public static StepPlacementIssue? FirstIssue(
        ActionParameter? destination, IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            if (DirectIssue(destination, step) is { } issue)
            {
                return issue;
            }

            foreach (var parameter in step.StepLists)
            {
                var nested = step.ParameterDefinition(parameter);
                if (FirstIssue(nested, parameter.Steps) is { } nestedIssue)
                {
                    return nestedIssue;
                }
            }
        }

        return null;
    }

    /// <summary>Checks one step against its own list, without looking inside it.</summary>
    public static StepPlacementIssue? DirectIssue(ActionParameter? destination, MacroStep step)
    {
        var definition = step.Definition;
        if (definition is null)
        {
            return new StepPlacementIssue(StepPlacementProblem.UnknownStep, step, destination);
        }

        if (destination is { ChildKeys.Count: > 0 })
        {
            return destination.ChildKeys.Contains(definition.Key)
                ? null
                : new StepPlacementIssue(StepPlacementProblem.WrongKind, step, destination);
        }

        return definition.Hidden && definition.Key != SequenceKey
            ? new StepPlacementIssue(StepPlacementProblem.HiddenStep, step, destination)
            : null;
    }

    /// <summary>The actions a picker may offer for one destination.</summary>
    public static IReadOnlyList<ActionDefinition> Allowed(ActionParameter? destination)
        => destination is { ChildKeys.Count: > 0 }
            ? ActionCatalog.ForKeys(destination.ChildKeys)
            : ActionCatalog.PlaceableActions;

    /// <summary>The names a refusal should use for a destination that only takes named child kinds.</summary>
    public static string AllowedSummary(ActionParameter destination)
    {
        var allowed = Allowed(destination);
        var names = allowed.Select(definition => definition.LocalName).Distinct().ToList();
        return names.Count <= 3
            ? string.Join(" / ", names)
            : Strings.Get($"Add.Category.{allowed[0].Category}");
    }
}
