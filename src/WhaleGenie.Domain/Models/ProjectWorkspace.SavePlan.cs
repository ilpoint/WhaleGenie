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
    IReadOnlyList<MacroItem> Macros,
    ProjectTriggers Triggers,
    IReadOnlyList<VariableDefinition> Globals,
    string AppVersion,
    string? AssetFolder);

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
                [.. Macros.Select(macro => MacroItem.FromJson(macro.ToJson()))],
                Triggers.Copy(),
                [.. Variables.Globals.Select(Clone)],
                _appVersion,
                ImageAssets.FolderFor(CurrentPath));

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
