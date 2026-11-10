using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// Walking the tree: identifiers, order, cloning and where an insertion would sit.
public partial class MacroEditorViewModel
{
    /// <summary>
    /// A name for a step that is not in the list yet. The name is handed to the dialog while the
    /// step is being written, so the variables it creates can carry it — and it is drawn from the
    /// names this macro already uses, so the name the dialog shows is the name the step will keep.
    /// </summary>
    public string NewStepId() => StepIds.Next(AllIds());

    /// <summary>
    /// An id no step of this macro is using, counting names another form has already handed out but
    /// not yet put in the list. A form that adds two branches in one visit needs that: neither is
    /// in the macro until it is saved, so the first would be drawn again for the second.
    /// </summary>
    public string NewStepId(ISet<string> reserved)
    {
        var taken = AllIds();
        taken.UnionWith(reserved);
        return StepIds.Next(taken);
    }

    /// <summary>Every name in use anywhere in this macro, nested steps and conditions included.</summary>
    private HashSet<string> AllIds()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps)
        {
            Collect(step);
        }

        return taken;

        void Collect(MacroStep step)
        {
            if (step.Id.Length > 0)
            {
                taken.Add(step.Id);
            }

            foreach (var parameter in step.Parameters)
            {
                foreach (var child in parameter.Steps)
                {
                    Collect(child);
                }

            }
        }
    }

    /// <summary>
    /// The steps of the macro being written, which is what a step that has to point at another one
    /// picks from.
    /// </summary>
    public IReadOnlyList<ActionParameterOption> StepChoices()
    {
        var choices = new List<ActionParameterOption>();
        foreach (var step in Steps)
        {
            Collect(choices, step);
        }

        return choices;

        static void Collect(List<ActionParameterOption> choices, MacroStep step)
        {
            if (step.Id.Length > 0)
            {
                choices.Add(new ActionParameterOption(step.Id, step.PickerLabel));
            }

            foreach (var parameter in step.Parameters)
            {
                foreach (var child in parameter.Steps)
                {
                    Collect(choices, child);
                }

            }
        }
    }

    private static MacroStep Clone(MacroStep step) => new()
    {
        Type = step.Type,
        Id = step.Id,
        Parameters = step.Parameters.Select(Clone).ToList(),
        Meta = step.Meta,
        IsExpanded = step.IsExpanded,
    };

    /// <summary>
    /// Copies a parameter together with the steps nested inside it,
    /// so duplicating a loop or an if keeps its body.
    /// </summary>
    private static StepParameter Clone(StepParameter parameter) => new()
    {
        Name = parameter.Name,
        Kind = parameter.Kind,
        Value = parameter.Value,
        Jitter = parameter.Jitter,
        Steps = parameter.Steps.Select(Clone).ToList(),
        Rows = parameter.Rows.Select(row => row.Copy()).ToList(),
    };

    /// <summary>
    /// The given steps in the order they are written, nested ones included, so a run of them
    /// keeps its order whichever block each came from.
    /// </summary>
    private List<MacroStep> Ordered(IReadOnlyList<MacroStep> steps)
    {
        var order = DocumentOrder();
        return [.. steps.Where(order.Contains).OrderBy(order.IndexOf)];
    }

    /// <summary>Every step of the macro in the order it is written, deepest steps included.</summary>
    private List<MacroStep> DocumentOrder()
    {
        var ordered = new List<MacroStep>();
        Walk(Steps);
        return ordered;

        void Walk(IEnumerable<MacroStep> steps)
        {
            foreach (var step in steps)
            {
                ordered.Add(step);
                foreach (var list in step.StepLists)
                {
                    Walk(list.Steps);
                }
            }
        }
    }

    /// <summary>Every list of steps in the macro: the top level first, then the ones in blocks.</summary>
    private IEnumerable<IList<MacroStep>> AllLists()
    {
        yield return Steps;

        foreach (var list in Descend(Steps))
        {
            yield return list;
        }
    }

    private static IEnumerable<IList<MacroStep>> Descend(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            foreach (var list in step.StepLists)
            {
                yield return list.Steps;

                foreach (var nested in Descend(list.Steps))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>The list of steps a step sits in, or null when the macro no longer holds it.</summary>
    private IList<MacroStep>? ListOf(MacroStep step)
        => AllLists().FirstOrDefault(list => list.Contains(step));

    private bool InTree(MacroStep step) => ListOf(step) is not null;

    /// <summary>
    /// True when a list of steps lives inside the given step, so moving steps into it would put
    /// the step inside itself.
    /// </summary>
    private static bool IsInside(IList<MacroStep> list, MacroStep step)
        => step.StepLists.Any(own => ReferenceEquals(own.Steps, list)
            || own.Steps.Any(child => IsInside(list, child)));

    /// <summary>
    /// Opens every block on the way to a list of steps, so a step put in there is not put in
    /// a block the user cannot see.
    /// </summary>
    private void ShowInside(IList<MacroStep> list) => Open(list, Steps);

    private static bool Open(IList<MacroStep> list, IEnumerable<MacroStep> from)
    {
        foreach (var step in from)
        {
            foreach (var own in step.StepLists)
            {
                if (ReferenceEquals(own.Steps, list) || Open(list, own.Steps))
                {
                    step.IsExpanded = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Locks the place a dropped action will occupy. The form opens after this, so the tree cannot
    /// move the target under the person while they fill the fields.
    /// </summary>
    public void PrepareInsertionAt(IList<MacroStep> list, int index)
    {
        _insertAt = TargetForList(list, index);

        // Opening the way to the list is what makes the step visible once it is added.
        ShowInside(list);
    }

    /// <summary>The target for a known list, including the owner's declaration that restricts it.</summary>
    private StepInsertionTarget TargetForList(IList<MacroStep> list, int index)
        => OwnerOfList(list) is { } owner
            ? new StepInsertionTarget(list, index, owner.Owner, owner.Parameter)
            : new StepInsertionTarget(list, index, null, null);

    /// <summary>The block and parameter that own a child list, or null for the macro's root list.</summary>
    private (MacroStep Owner, StepParameter Parameter)? OwnerOfList(IList<MacroStep> list)
    {
        foreach (var owner in DocumentOrder())
        {
            foreach (var parameter in owner.StepLists)
            {
                if (ReferenceEquals(parameter.Steps, list))
                {
                    return (owner, parameter);
                }
            }
        }

        return null;
    }

    /// <summary>True when the place currently accepting a drop allows this action.</summary>
    public bool CanPlace(string actionKey)
        => InsertChoices is not { } allowed
           || allowed.Any(definition => definition.Key == actionKey);

    public void ClearPendingInsertion()
        => _insertAt = null;

    /// <summary>Where a step asked for now should go: below the picked one, in its own block.</summary>
    private StepInsertionTarget? InsertionPoint()
    {
        if (Ordered(_selection).LastOrDefault() is not { } anchor)
        {
            return null;
        }

        // Picking a block means "add inside it": it runs one list, so there is only one place that
        // could mean, and it is where a person who just picked the loop wants the step to land.
        if (anchor.StepLists.Count() == 1 && anchor.StepLists.First() is { } body)
        {
            return new StepInsertionTarget(body.Steps, body.Steps.Count, anchor, body);
        }

        // A switch keeps its branches in one list, and picking the switch then adding a step is how
        // a new branch is made: the palette already narrows that list to the branch step, so what
        // arrives is a branch and not a stray action inside the table.
        if (anchor.StepLists.FirstOrDefault(list => list.Name == "cases") is { } branches)
        {
            return new StepInsertionTarget(branches.Steps, branches.Steps.Count, anchor, branches);
        }

        if (OwnerOf(anchor) is not { } at)
        {
            return null;
        }

        return new StepInsertionTarget(at.Parameter.Steps,
            at.Parameter.Steps.IndexOf(anchor) + 1, at.Owner, at.Parameter);
    }
}
