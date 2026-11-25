using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Storage;

namespace WhaleGenie.Models;

/// <summary>
/// What a save needs, copied off the live project so the file can be written away from the thread
/// that owns the window. The copy is made on that thread; everything here is plain data.
/// </summary>
public sealed record ProjectSavePlan(
    string Path,
    string? PreviousPath,
    IReadOnlyList<MacroItem> Macros,
    ProjectTriggers Triggers,
    IReadOnlyList<VariableDefinition> Globals,
    string AppVersion,
    string? AssetFolder,
    int Revision);

public sealed partial class ProjectWorkspace
{
    /// <summary>
    /// Copies the project for a save, or answers null when it has no file yet. The caller writes
    /// <see cref="ProjectSavePlan"/> on a worker and then calls <see cref="MarkSaved"/> on the
    /// interface thread.
    /// </summary>
    public ProjectSavePlan? PrepareSave() => CurrentPath is { Length: > 0 } path
        ? PrepareSave(path)
        : null;

    /// <summary>
    /// Copies the project for a save to the given file. The pictures still come from the folder of
    /// the project as it stands now, which is what a save-as has to carry along.
    /// </summary>
    public ProjectSavePlan PrepareSave(string path)
        => new(
                path,
                CurrentPath,
                [.. Macros.Select(macro => MacroItem.FromJson(macro.ToJson()))],
                Triggers.Copy(),
                [.. Variables.Globals.Select(Clone)],
                _appVersion,
                ImageAssets.FolderFor(CurrentPath),
                Revision);

    /// <summary>
    /// Records that the file now holds the project as it stood when <paramref name="plan"/> was
    /// copied. A change made while the file was being written is newer than the file, so the
    /// document stays unsaved and the next save carries it; clearing the flag blindly would lose
    /// that change from every prompt and every recovery draft.
    /// </summary>
    /// <remarks>
    /// A save has two legitimate shapes: the plan was copied from the path the project still
    /// points at (an ordinary save, or the first write of a save-as), or the plan's target is
    /// already the current path (another save of the same file finished first). Any other
    /// current path means the project was replaced or moved to a different document while this
    /// plan was being written; that write belongs to the old document and must not pull the
    /// project back to it.
    /// </remarks>
    public void MarkSaved(ProjectSavePlan plan)
    {
        var sameDocument = string.IsNullOrEmpty(CurrentPath)
            || string.Equals(CurrentPath, plan.PreviousPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(CurrentPath, plan.Path, StringComparison.OrdinalIgnoreCase);
        if (!sameDocument)
        {
            return;
        }

        CurrentPath = plan.Path;
        if (Revision == plan.Revision)
        {
            IsDirty = false;
        }
    }

    /// <summary>Records that the file now holds this project's contents.</summary>
    public void MarkSaved(string path)
    {
        CurrentPath = path;
        IsDirty = false;
    }

    private static VariableDefinition Clone(VariableDefinition variable) => new()
    {
        Name = variable.Name,
        Scope = variable.Scope,
        Type = variable.Type,
        Description = variable.Description,
        Default = variable.Default,
        Owner = variable.Owner,
        Seeded = variable.Seeded,
        PersistValue = variable.PersistValue,
    };
}
