using System;
using System.Collections.Generic;
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

/// <summary>The document's lifecycle outside its steps: the public library and save.</summary>

internal sealed partial class HeadlessEditorSession
{
    public EditorSnapshot AddPublicMacro(string id)
    {
        var entry = _publicLibrary.Load()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the public library has no macro " + id);
        var macro = _publicLibrary.CopyToProject(entry, ImageAssets.FolderFor(_workspace.CurrentPath));
        macro.Name = SubMacroEditor.UniqueName(TakenMacroNames(), macro.Name);
        _model.SubMacros.Add(macro);
        _model.Steps.Add(SubMacroEditor.CallStep(macro));
        _model.SettleStepIds();
        _model.IsDirty = true;
        return Snapshot();
    }

    public DocumentSaveOutcome Save()
    {
        if (!CanSave)
        {
            return new DocumentSaveOutcome(EditorSaveOutcomes.Blocked,
                SaveProblem is { Length: > 0 } problem
                    ? problem
                    : Strings.Get("Editor.Problem.CannotSave"));
        }

        var draft = _model.Draft();
        if (_owner is not null)
        {
            if (!_owner.ApplyChildDraft(_submacroOriginal, draft.Macro))
            {
                return new DocumentSaveOutcome(EditorSaveOutcomes.Conflict,
                    Strings.Format("Editor.Save.TargetGone", Name));
            }

            _existing = draft.Macro;
            _submacroOriginal = draft.Macro.Name;
            _originalName = draft.Macro.Name;
            _model.IsDirty = false;
            return new DocumentSaveOutcome(EditorSaveOutcomes.Saved, null);
        }

        var commit = new ProjectMacroCommit(_workspace, _existing, MacroView.All,
            _ => { }, () => { }, () => { });
        var result = commit.Apply(draft, []);
        if (result.Outcome == MacroCommitOutcome.TargetGone)
        {
            return new DocumentSaveOutcome(EditorSaveOutcomes.Conflict,
                Strings.Format("Editor.Save.TargetGone", Name));
        }

        CommitGlobals();
        _existing = result.Target;
        if (_existing is { } target
            && !string.Equals(_model.Name.Trim(), target.Name.Trim(), StringComparison.Ordinal))
        {
            // The project makes a new macro's name unique. The page has to show the name the
            // project actually took, not the draft name it sent.
            _model.Name = target.Name;
        }

        _originalName = _existing?.Name.Trim() ?? draft.Macro.Name.Trim();
        _model.IsDirty = false;
        return new DocumentSaveOutcome(
            result.Outcome == MacroCommitOutcome.Unchanged
                ? EditorSaveOutcomes.Unchanged
                : EditorSaveOutcomes.Saved,
            null);
    }

    /// <summary>
    /// Puts a saved child draft into this macro's model. The child owns the edit until the macro
    /// that owns it is saved; touching the project's own macro here would make the parent's later
    /// save look unchanged and lose the edit.
    /// </summary>
    internal bool ApplyChildDraft(string originalName, MacroItem updated)
    {
        if (originalName.Trim().Length == 0)
        {
            // A new child was opened as a document before it belonged to the macro. Saving that
            // document is the moment it enters the list; an abandoned tab never did.
            updated.Name = SubMacroEditor.UniqueName(TakenMacroNames(), updated.Name);
            _model.SubMacros.Add(updated);
            _model.IsDirty = true;
            RefreshCallables();
            return true;
        }

        var index = _model.SubMacros.FindIndex(macro =>
            string.Equals(macro.Name, originalName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        _model.SubMacros[index] = updated;
        _model.IsDirty = true;
        RefreshCallables();
        return true;
    }

}
