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
    public EditorSnapshot AddPublicMacro(string id, string source = "local", int version = 0)
    {
        var entry = source == "project"
            ? _workspace.Library.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal)
                && (version <= 0 || candidate.Version == version))
            : _publicLibrary.Load().FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal)
                && (version <= 0 || candidate.Version == version));
        var found = entry ?? throw new InvalidOperationException(
            "the public library has no macro " + id + " version " + version);
        var sourceFolder = source == "project"
            ? ImageAssets.FolderFor(_workspace.CurrentPath)
            : PublicMacroLibrary.AssetFolderFor(_publicLibrary.Path);
        var targetFolder = ImageAssets.FolderFor(_workspace.CurrentPath);
        var macro = source == "project"
            ? PublicMacroLibrary.CopyToProject(found, targetFolder, sourceFolder)
            : _publicLibrary.CopyToProject(found, targetFolder, sourceFolder);
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
            var applied = _owner.ApplyChildDraft(_submacroOriginal, draft.Macro);
            if (applied is null)
            {
                return new DocumentSaveOutcome(EditorSaveOutcomes.Conflict,
                    Strings.Format("Editor.Save.TargetGone", Name));
            }

            _existing = applied;
            _submacroOriginal = applied.Name;
            _originalName = applied.Name;
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
    internal MacroItem? ApplyChildDraft(string originalName, MacroItem updated)
    {
        var (children, applied, found) = SubMacroEditor.WithChild(
            _model.SubMacros, originalName, updated, TakenMacroNames());
        if (!found)
        {
            return null;
        }

        _model.SubMacros.Clear();
        _model.SubMacros.AddRange(children);
        _model.IsDirty = true;
        RefreshCallables();
        return applied;
    }

}
