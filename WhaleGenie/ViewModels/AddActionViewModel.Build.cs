using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// What is still missing, and the step the form builds when it is saved.
public partial class AddActionViewModel
{
    private List<string> MissingParameters()
    {
        var missing = Parameters.Where(parameter => parameter.IsMissing)
            .Select(parameter => parameter.Definition.LocalLabel)
            .ToList();

        // A call's arguments are its own list rather than fields of the form, so a required one
        // with no value and no "pass an empty value" tick is missing the same way an ordinary
        // required field is — and has to be counted here or the dialog would let it be saved.
        missing.AddRange(CallArgumentRows
            .Where(row => row.IsDeclared && row.IsRequired && row.IsEmpty && !row.Given)
            .Select(row => row.Name));
        return missing;
    }

    /// <summary>
    /// Rules that span several parameters. The variables WhaleGenie provides stay read-only.
    /// </summary>
    private string? ScopeError()
    {
        if (SelectedDefinition is null)
        {
            return null;
        }

        // The name a step writes may not be one WhaleGenie provides: those are read-only, and a
        // macro holding its own copy of the date or the clipboard under that name reads it back
        // everywhere else as if it were the real one. A counter, a list and an item of a loop all
        // name a variable the same way, so the question is asked of every field that has the step
        // create one rather than of a list of actions kept here. What a field reads is not one of
        // these: reading sys.date is what those values are for.
        foreach (var parameter in Parameters.Where(parameter => parameter.IsIncluded))
        {
            var written = parameter.CurrentText.Trim();
            if (written.Length > 0 && VariableCatalog.IsSystem(written)
                && MacroStep.DefinesVariable(SelectedDefinition.Key, parameter.Definition.Name))
            {
                return Strings.Format("Add.SystemReadOnly", written);
            }
        }

        // Most actions that write a shared value require its declaration. Setting a variable is
        // the one exception: saving that step creates a missing global as text.
        if (SelectedDefinition.Parameters.All(parameter => parameter.Name != "name")
            || SelectedDefinition.Parameters.All(parameter => parameter.Name != "scope"))
        {
            return null;
        }

        var name = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "name")
            ?.CurrentText.Trim() ?? string.Empty;

        var scope = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "scope")
            ?.CurrentText;

        return SelectedDefinition.Key != "control.setVariable"
            && name.Length > 0 && scope == "global" && !VariableCatalog.IsGlobal(name)
            ? Strings.Format("Add.GlobalMustExist", name)
            : null;
    }

    /// <summary>
    /// The step as it would be saved, which is what the dialog's "test" button tries out: what is
    /// tried has to be what the step says, settings and all, rather than the fields read again
    /// somewhere else.
    /// </summary>
    internal MacroStep BuildStep()
    {
        var step = new MacroStep
        {
            Type = SelectedDefinition!.Key,
            Id = _editingId,
            Parameters = Parameters
                .Where(parameter => parameter.IsIncluded
                    || parameter.Definition.Kind == ActionParameterKind.Steps)
                .Select(parameter => parameter.ToStepParameter())
                .ToList(),
            Meta = new StepMeta
            {
                Comment = MetaComment.Trim(),
                IsEnabled = MetaEnabled,
                TimeoutMs = MetaTimeout.Milliseconds,
                RetryCount = Whole(MetaRetryCount),
                RetryDelayMs = MetaRetryDelay.Milliseconds,
                RetryBackoff = StepMeta.Backoff(MetaRetryBackoff?.Value ?? "fixed"),
                DelayBeforeMs = MetaDelayBefore.Milliseconds,
                DelayAfterMs = MetaDelayAfter.Milliseconds,
                OnError = StepMeta.Action(MetaOnError.Value),
            },
        };
        // A call's arguments are written as one list rather than as a field per argument: the names
        // belong to the macro being called, so a file that says which name goes with which value can
        // be read without the reader having to know that macro first.
        var arguments = CallArgumentRows
            .Select(row => row.ToRow())
            .OfType<StepParameterRow>()
            .ToList();
        if (arguments.Count > 0)
        {
            step.Parameters.RemoveAll(parameter => parameter.Name == FieldNames.Arguments);
            step.Parameters.Add(new StepParameter
            {
                Name = FieldNames.Arguments,
                Kind = ActionParameterKind.Arguments,
                Rows = arguments,
            });
        }

        step.EnsureDeclaredLists();
        return step;
    }

    /// <summary>Reads a whole-number box that may be left empty, counting an empty one as zero.</summary>
    private static int Whole(decimal? value) => value is null ? 0 : Math.Max(0, (int)value.Value);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        CommitGlobalVariable();
        CloseRequested?.Invoke(BuildStep());
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    private void OnGlobalDraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasGlobalVariableProblem));
        OnPropertyChanged(nameof(GlobalVariableProblem));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRun));
    }

    private void SyncGlobalVariable()
    {
        if (SelectedDefinition?.Key != "control.setVariable"
            || !string.Equals(Parameter("scope")?.CurrentText, "global",
                StringComparison.OrdinalIgnoreCase))
        {
            GlobalVariableDraft = null;
            return;
        }

        var name = Parameter("name")?.CurrentText.Trim() ?? string.Empty;
        if (GlobalVariableDraft is { } current
            && string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existing = VariableCatalog.FindGlobal(name);
        GlobalVariableDraft = existing is not null
            ? new VariableDefinitionDraft(existing, persisted: Persisted)
            : new VariableDefinitionDraft(persisted: Persisted)
            {
                Name = name,
                Type = "text",
            };
        GlobalVariableDraft.Resolver = VariableValues.DesignContext();
    }

    private void CommitGlobalVariable()
    {
        var name = GlobalVariableDraft?.Name.Trim() ?? string.Empty;
        if (GlobalVariableDraft is not { } draft
            || ShowsGlobalPersistence is false
            || !VariableName.IsName(name))
        {
            return;
        }

        var existing = VariableCatalog.FindGlobal(draft.OriginalName);
        if (existing is null)
        {
            existing = new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = VariableTypes.KindOf(draft.Type),
                Default = draft.Default,
                Description = draft.Description.Trim(),
                PersistValue = draft.PersistValue,
            };
            VariableCatalog.Globals.Add(existing);
        }
        else
        {
            existing.Type = VariableTypes.KindOf(draft.Type);
            existing.Default = draft.Default;
            existing.Description = draft.Description.Trim();
            existing.PersistValue = draft.PersistValue;
        }

        // A value kept between runs is written when the box is ticked here, not when the step is
        // saved: the person asked for it at this moment.
        Persisted.PersistChanged(existing, draft.PersistValue);

        existing.PersistValue = draft.PersistValue;
        VariableCatalog.NotifyGlobalsChanged();
    }
}
