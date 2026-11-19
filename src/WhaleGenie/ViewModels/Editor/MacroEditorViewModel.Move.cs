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

// Moving the selection within a list and between lists.
public partial class MacroEditorViewModel
{
    /// <summary>
    /// The step that holds the list a step is written in, and the parameter carrying that list.
    /// </summary>
    private (MacroStep Owner, StepParameter Parameter)? OwnerOf(MacroStep step)
    {
        foreach (var owner in DocumentOrder())
        {
            foreach (var parameter in owner.StepLists)
            {
                if (parameter.Steps.Contains(step))
                {
                    return (owner, parameter);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Puts the picked steps inside a block, at the end of the first list it runs. A block that
    /// runs several lists leaves the choice to the row of the list meant — a drop there names the
    /// list itself rather than the block.
    /// </summary>
    public void MoveSelectionInto(MacroStep block)
    {
        if (block.StepLists.FirstOrDefault() is not { } parameter || _selection.Count == 0)
        {
            return;
        }

        block.IsExpanded = true;
        MoveSelectionToTarget(parameter.Steps, parameter.Steps.Count);
    }

    private void MoveSelection(int offset)
    {
        if (_selection.Count == 0)
        {
            return;
        }

        PushUndo();

        // Capture the steps first: moving them makes the list drop its selection,
        // which clears the selection the loop below needs to keep stable.
        var moved = Ordered(_selection);
        var moving = new HashSet<MacroStep>(moved);

        // Walk from the far end so a block of selected steps keeps its order.
        foreach (var step in offset < 0 ? moved : moved.AsEnumerable().Reverse())
        {
            if (ListOf(step) is not { } list)
            {
                continue;
            }

            var index = list.IndexOf(step);
            var target = index + offset;

            if (index < 0 || target < 0 || target >= list.Count || moving.Contains(list[target]))
            {
                continue;
            }

            list.RemoveAt(index);
            list.Insert(target, step);
        }

        RestoreSelection(moved);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Puts the selected steps where a drag dropped them: into <paramref name="list"/> in front of
    /// <paramref name="index"/>. The list is what says which block the steps land in, so one drag
    /// can carry a step out of a loop, into one, or past the line that closes a block.
    /// </summary>
    public void MoveSelectionTo(IList<MacroStep> list, int index)
        => MoveSelectionToTarget(list, index);

    /// <summary>
    /// Moves the picked steps into a list of steps at a position. A step is never moved into
    /// itself, and a move that would change nothing leaves no undo entry behind.
    /// </summary>
    private void MoveSelectionToTarget(IList<MacroStep> target, int index)
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0)
        {
            return;
        }

        if (selected.Any(step => IsInside(target, step)))
        {
            ReportPlacementRefusal(Strings.Get("Editor.PlaceIntoItself"));
            return;
        }

        if (PlacementRefusal(TargetForList(target, index), selected) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        var moving = new HashSet<MacroStep>(selected);

        // The step the block lands in front of. A step being dragged cannot be that anchor, or
        // the block would be measured against itself and could never move past it.
        MacroStep? anchor = null;
        for (var at = Math.Clamp(index, 0, target.Count); at < target.Count; at++)
        {
            if (!moving.Contains(target[at]))
            {
                anchor = target[at];
                break;
            }
        }

        var reordered = target.Where(step => !moving.Contains(step)).ToList();
        var insertAt = anchor is null ? reordered.Count : reordered.IndexOf(anchor);
        reordered.InsertRange(insertAt, selected);

        // A drag that ends where it started is not a change, so it leaves no undo entry.
        if (reordered.SequenceEqual(target))
        {
            return;
        }

        PushUndo();

        foreach (var step in selected)
        {
            ListOf(step)?.Remove(step);
        }

        target.Clear();
        foreach (var step in reordered)
        {
            target.Add(step);
        }

        ShowInside(target);
        RestoreSelection(selected);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Re-applies a selection after the step list dropped it, and asks the view to
    /// highlight the same rows again so repeated moves do not need another click.
    /// </summary>
    private void RestoreSelection(IEnumerable<MacroStep> steps)
    {
        _selection.Clear();
        _selection.AddRange(steps.Where(InTree));

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        RefreshPaletteState();
        RefreshRowDetails();
    }
}
