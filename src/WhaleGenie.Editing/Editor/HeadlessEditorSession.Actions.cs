using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
namespace WhaleGenie.Editing;

/// <summary>The action draft: opening, confirming and cancelling one step's action.</summary>

internal sealed partial class HeadlessEditorSession
{
    /// <summary>
    /// Opens the restored token picker on one scalar parameter of a step. The field is rebuilt from
    /// the catalogue definition and the stored parameter, exactly as the old action editor did, and
    /// the confirmed shape replaces that one parameter on the step.
    /// </summary>
    public EditorActionFormSnapshot OpenActionDraft(string type, string parentId = "", string listName = "",
        string targetStepId = "", bool before = false)
    {
        var definition = ActionCatalog.Find(type)
            ?? throw new InvalidOperationException("unknown action " + type);
        CloseValueEditor();
        _actionTarget = new ActionTarget(parentId, listName, targetStepId, before);
        PrepareTarget();
        _actionSession = CreateActionSession(definition, null);
        return _actionSession.Snapshot();
    }

    public EditorActionFormSnapshot OpenStepDraft(string stepId)
    {
        var found = FindTreeStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        CloseValueEditor();
        _actionTarget = null;
        var definition = ActionCatalog.Find(found.Type)
            ?? throw new InvalidOperationException("unknown action " + found.Type);
        _actionSession = CreateActionSession(definition, found);
        return _actionSession.Snapshot();
    }

    public EditorActionFormSnapshot ActionFormState()
        => _actionSession?.Snapshot() ?? EditorActionSnapshotFactory.Closed();

    public HeadlessActionSession ActionForm
        => _actionSession ?? throw new InvalidOperationException("no action is being edited");

    public EditorSnapshot ConfirmActionDraft()
    {
        var session = _actionSession
            ?? throw new InvalidOperationException("no action is being edited");
        if (!session.Form.CanSave)
        {
            return Snapshot();
        }

        var step = session.BuildStepForSave();
        if (session.IsEditing)
        {
            var original = FindTreeStep(session.Original!.Id)
                ?? throw new InvalidOperationException("the edited step is no longer in the macro");
            _model.ReplaceStep(original, step);
        }
        else
        {
            PrepareTarget();
            _model.AddStep(step);
        }

        _actionSession = null;
        _actionTarget = null;
        return Snapshot();
    }

    public EditorSnapshot CancelActionDraft()
    {
        _actionSession = null;
        _actionTarget = null;
        _valueSession = null;
        return Snapshot();
    }

    private HeadlessActionSession CreateActionSession(ActionDefinition definition, MacroStep? original)
    {
        var actions = ActionCatalog.PlaceableActions;
        if (actions.All(action => action.Key != definition.Key))
        {
            actions = [.. actions, definition];
        }

        var macros = new List<MacroCallable>(_workspace.Macros.Select(macro => macro.Callable));
        macros.AddRange(_model.SubMacros.Select(macro => macro.Callable));
        var above = WindowRuleChoices.NearestAbove(_model.Steps, _actionTarget?.TargetStepId);
        var form = new AddActionViewModel(actions, _model.CollectVariables(), macros,
            _model.StepChoices(), original is null ? StepIds.Next(CollectStepIds()) : null,
            WindowRuleChoices.Of(_model.Steps), above.Length > 0, above,
            _model.LoopNamesInScope())
        {
            Variables = _workspace.Variables,
            Persisted = _workspace.Persisted,
            Windows = _devices?.Windows,
            Ui = _devices?.Ui,
            Clipboard = _devices?.Clipboard,
            AssetFolder = _workspace.CurrentPath is { Length: > 0 } current
                ? MacroPackage.AssetFolderFor(current)
                : ImageAssets.DefaultFolder(),
            NextStepId = StepIds.Next,
        };

        if (original is null)
        {
            form.SelectAction(definition.Key);
        }
        else
        {
            form.LoadFrom(original);
        }

        return new HeadlessActionSession(form, original, new TrialHost());
    }

    private void PrepareTarget()
    {
        if (_actionTarget is not { } target)
        {
            _model.PrepareInsertionAt(_model.Steps, _model.Steps.Count);
            return;
        }

        if (target.TargetStepId is { Length: > 0 } targetId)
        {
            if (Find(targetId) is { } found)
            {
                _model.PrepareInsertionAt(found.List, found.Index + (target.Before ? 0 : 1));
                return;
            }

            throw new InvalidOperationException("the insertion target is no longer in the macro");
        }

        if (target.ParentId is { Length: > 0 } parentId)
        {
            var parent = FindTreeStep(parentId)
                ?? throw new InvalidOperationException("the parent step is no longer in the macro");
            var list = parent.Parameters.FirstOrDefault(parameter =>
                    parameter.Kind == ActionParameterKind.Steps
                    && (target.ListName is not { Length: > 0 }
                        || string.Equals(parameter.Name, target.ListName, StringComparison.Ordinal)))?.Steps
                ?? throw new InvalidOperationException("the parent has no child list " + target.ListName);
            _model.PrepareInsertionAt(list, list.Count);
            return;
        }

        _model.PrepareInsertionAt(_model.Steps, _model.Steps.Count);
    }

    private HashSet<string> CollectStepIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (list, index) in Walk(_model.Steps))
        {
            ids.Add(list[index].Id);
        }

        return ids;
    }

}
