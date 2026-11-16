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

// Putting a step where the palette points: refusals, adds, replacements, paste and the text form.
public partial class MacroEditorViewModel
{
    /// <summary>
    /// Puts a step the add-action window handed over where the editor is pointing now. That window
    /// stays open while the list is worked on, so where the step goes is read again here rather
    /// than when the window was opened. Returns null once the step is in the list, or what to say
    /// when the place picked does not take that kind of step.
    /// </summary>
    public string? AddStepHere(MacroStep step)
    {
        // A place asked for by name — the "add" button on a block's line — stands until the
        // selection moves: moving it is the user saying "somewhere else".
        _insertAt ??= InsertionPoint();

        if (PlacementRefusalForPending(step) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return refusal;
        }

        AddStep(step);
        return null;
    }

    /// <summary>Checks the step a form is about to add at the target the editor locked for it.</summary>
    internal string? PlacementRefusalForPending(MacroStep step)
        => PlacementRefusal(
            _insertAt ?? InsertionPoint() ?? TargetForList(Steps, Steps.Count),
            [step]);

    /// <summary>Checks an edited step against the list its original occupies.</summary>
    internal string? PlacementRefusalForReplacement(MacroStep original, MacroStep replacement)
        => ListOf(original) is { } list
            ? PlacementRefusal(TargetForList(list, list.IndexOf(original)), [replacement])
            : null;

    /// <summary>Returns the one explanation every insertion and move uses for a rejected step.</summary>
    private static string? PlacementRefusal(
        StepInsertionTarget? target, IEnumerable<MacroStep> steps)
    {
        var destination = target is { Owner: { } owner, Parameter: { } parameter }
            ? owner.ParameterDefinition(parameter)
            : null;
        if (StepPlacement.FirstIssue(destination, steps) is not { } issue)
        {
            return null;
        }

        if (issue.Destination is { ChildKeys.Count: > 0 } restricted)
        {
            return Strings.Format("Editor.PlaceTakesOnly",
                StepPlacement.AllowedSummary(restricted));
        }

        return issue.Step.Type == "control.case"
            ? Strings.Get("Editor.PlaceCaseOnly")
            : Strings.Format("Editor.PlaceUnavailable", issue.Step.DisplayName);
    }

    /// <summary>
    /// Puts a step built by the "Add Action" dialog where the editor said it was to go, which is
    /// the end of the macro unless a step or a block was picked first.
    /// </summary>
    public void AddStep(MacroStep step)
    {
        step.EnsureDeclaredLists();
        var target = _insertAt ?? TargetForList(Steps, Steps.Count);
        if (PlacementRefusal(target, [step]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        if (step.StepLists.Any())
        {
            step.IsExpanded = true;
        }

        var (list, index) = (target.List, target.Index);
        _insertAt = null;
        list.Insert(Math.Clamp(index, 0, list.Count), step);

        SettleStepIds();
        ShowInside(list);
        RestoreSelection([step]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Appends the steps a recording produced. They arrive together, so one undo puts the
    /// list back the way it was before the recording started.
    /// </summary>
    public void AppendSteps(IReadOnlyList<MacroStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        if (PlacementRefusal(TargetForList(Steps, Steps.Count), steps) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }

        SettleStepIds();
        NotifyStepsChanged();
        MarkDirty();
    }

    /// <summary>Swaps a step for the edited version returned by the dialog.</summary>
    public void ReplaceStep(MacroStep original, MacroStep replacement)
    {
        replacement.EnsureDeclaredLists();
        if (ListOf(original) is not { } list)
        {
            return;
        }

        var index = list.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        if (PlacementRefusal(TargetForList(list, index), [replacement]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();

        // A block the user had folded open stays folded open while it is being edited.
        replacement.IsExpanded = original.IsExpanded;
        list[index] = replacement;
        RestoreSelection([replacement]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Drops the given steps in below the selection, which is where a paste is expected
    /// to land. With nothing selected the steps go to the end of the list.
    /// </summary>
    public void PasteSteps(IReadOnlyList<MacroStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var anchor = Ordered(_selection).LastOrDefault();
        var list = anchor is null ? Steps : ListOf(anchor) ?? Steps;
        var index = anchor is null ? list.Count : list.IndexOf(anchor) + 1;
        var copies = steps.Select(Clone).ToList();
        foreach (var copy in copies)
        {
            copy.EnsureDeclaredLists();
        }

        if (PlacementRefusal(TargetForList(list, index), copies) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        for (var offset = 0; offset < copies.Count; offset++)
        {
            list.Insert(Math.Clamp(index + offset, 0, list.Count), copies[offset]);
        }

        // A paste is a copy, and two steps sharing a name share the variable it fills in and the
        // row a run lights up; a name that is free in this macro is kept, so pasting from another
        // macro leaves the steps as they were.
        SettleStepIds();
        ShowInside(list);
        RestoreSelection(copies);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Writes the given steps as the JSON array that copy and cut place on the clipboard.
    /// </summary>
    public static string SerializeSteps(IEnumerable<MacroStep> steps)
    {
        var array = new JsonArray();
        foreach (var step in steps)
        {
            array.Add(step.ToJson());
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// Rebuilds steps from clipboard text, or returns <c>null</c> when the text is not a
    /// step array, so pasting an unrelated phrase leaves the list untouched.
    /// </summary>
    public static List<MacroStep>? DeserializeSteps(string? text)
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

    /// <summary>Records what the action list has highlighted, so the palette can react.</summary>
    public void SetSelection(IEnumerable<MacroStep> steps)
    {
        // A step asked for before this one is no longer the step being pointed at.
        _insertAt = null;
        Notice = string.Empty;
        _selection.Clear();
        _selection.AddRange(steps);

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        RefreshPaletteState();
        RefreshRowDetails();
    }
}
