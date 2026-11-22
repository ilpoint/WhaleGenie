using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// Reading a saved step back into the form, and the values it may name.
public partial class AddActionViewModel
{
    /// <summary>
    /// Variable names the pickers offer: the ones the editor already knows about plus
    /// any created by nested steps added in this dialog.
    /// </summary>
    public IReadOnlyList<VariableChoice> CollectVariables()
    {
        // Steps added to a block inside this very dialog count the same way the ones already in the
        // macro do: they are steps the user has just written, and a field beside them may name what
        // they leave behind.
        var nested = Parameters
            .SelectMany(parameter => parameter.List?.Steps ?? [])
            .ToList();

        return nested.Count == 0
            ? _variables
            : VariableChoices.Merge(_variables, VariableChoices.For(nested, Variables.Globals));
    }

    /// <summary>
    /// The steps a field of this action may point at. The step being edited is left out: a step
    /// that asks about its own ending would always be asking before it has one, and the answer
    /// would be "not reached" every time.
    /// </summary>
    /// <remarks>
    /// Steps added to a block from inside this dialog count too, the same way the variables a step
    /// creates for itself do: they are steps of the macro the user has just written, and a
    /// condition written beside them is entitled to name one.
    /// </remarks>
    private IReadOnlyList<ActionParameterOption> StepsOfferedTo(ActionDefinition definition)
    {
        if (definition.Parameters.All(parameter => parameter.Kind is not ActionParameterKind.Step))
        {
            return [];
        }

        var choices = new List<ActionParameterOption>(StepChoices);
        foreach (var parameter in Parameters)
        {
            foreach (var step in parameter.List?.Steps ?? [])
            {
                Gather(choices, step);
            }
        }

        // Two blocks holding a step of the same name would offer it twice; the list is built from
        // names here and there is only one step per name in a macro.
        return
        [
            .. choices
                .GroupBy(choice => choice.Value, StringComparer.Ordinal)
                .Select(group => group.First())
                .Where(choice => choice.Value != _editingId),
        ];
    }

    /// <summary>Adds one step and everything written inside it to the list being built.</summary>
    private static void Gather(List<ActionParameterOption> choices, MacroStep step)
    {
        if (step.Id.Length > 0)
        {
            choices.Add(new ActionParameterOption(step.Id, step.PickerLabel));
        }

        foreach (var child in step.Parameters.SelectMany(parameter => parameter.Steps))
        {
            Gather(choices, child);
        }

    }

    /// <summary>Fills the dialog from an existing step so it can be edited.</summary>
    public void LoadFrom(MacroStep step)
    {
        IsEditing = true;
        _isNewStep = false;
        _editingId = step.Id;
        OnPropertyChanged(nameof(StepId));
        OnPropertyChanged(nameof(HasStepId));
        SelectedDefinition = ActionCatalog.Find(step.Type);

        // A call's arguments and results are the called macro's own declarations, so the name goes
        // into the form first: the fields the file is about to fill in only exist because of it.
        if (CallsMacro(SelectedDefinition) && Parameter("macro") is { } called)
        {
            called.WriteValue(Formula.Text(step.Text("macro")));
        }

        // The argument rows are read after the name they belong to, because which rows exist is the
        // called macro's answer rather than the step's.
        LoadCallArguments(step);

        foreach (var editor in Parameters)
        {
            var stored = step.Parameters.FirstOrDefault(parameter => parameter.Name == editor.Definition.Name);
            if (stored is not null)
            {
                editor.ApplyValue(stored);
            }
            else if (editor.IsReturns)
            {
                // The file said nothing about results, so this step hands none over: the defaults
                // are what a step being written starts from, not what a step that exists becomes.
                editor.ClearReturns();
            }
        }

        MetaComment = step.Meta.Comment;
        MetaEnabled = step.Meta.IsEnabled;
        MetaTimeout.Load(step.Meta.TimeoutMs);
        MetaRetryCount = step.Meta.RetryCount;
        MetaRetryDelay.Load(step.Meta.RetryDelayMs);
        MetaDelayBefore.Load(step.Meta.DelayBeforeMs);
        MetaDelayAfter.Load(step.Meta.DelayAfterMs);
        MetaOnError = ErrorChoices.FirstOrDefault(choice =>
            choice.Value == StepMeta.Name(step.Meta.OnError)) ?? ErrorChoices[0];
        MetaRetryBackoff = BackoffChoices.FirstOrDefault(choice =>
            choice.Value == StepMeta.Name(step.Meta.RetryBackoff)) ?? BackoffChoices[0];

        // The step opens on the page that was changed, so the reason it behaves unusually is the
        // first thing on screen rather than something to go looking for.
        OpenFirstPage();

        // Its own results are not "another variable": they are the ones this dialog is naming, and
        // they were being counted as taken because the macro's list of variables includes the step
        // being edited.
        _ownNames.Clear();
        if (ReturnsField is { } returns)
        {
            foreach (var row in returns.ReturnsRows.Where(row => row.ToRow() is not null))
            {
                _ownNames.Add(row.Name.Trim());
            }

            SuggestReturnNames();
        }
    }
}
