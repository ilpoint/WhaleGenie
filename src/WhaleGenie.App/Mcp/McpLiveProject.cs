using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;


/// <summary>
/// The only bridge between the protocol tools and the window's project. Every read and write goes
/// through the interface thread the window owns; a run with no window passes a dispatcher that
/// answers on the calling thread.
/// </summary>
public sealed class McpLiveProject(ProjectWorkspace project, IUiDispatcher ui) : IProjectGateway
{
    public async Task<McpProjectStatus> StatusAsync()
        => await ui.InvokeAsync(() =>
            new McpProjectStatus(project.CurrentPath, project.Revision, project.IsDirty,
                project.Macros.Count));

    public async Task<McpProjectSnapshot> SnapshotAsync()
        => await ui.InvokeAsync(Snapshot);

    /// <summary>
    /// The snapshot and whether the project has unsaved changes, read in one turn of the interface
    /// thread so the two belong to the same revision. A page that said "revision 7, unsaved" from two
    /// separate reads could name a revision the dirty flag was never about.
    /// </summary>
    public async Task<(McpProjectSnapshot Snapshot, bool Dirty)> ReadAsync()
        => await ui.InvokeAsync(() => (Snapshot(), project.IsDirty));

    /// <summary>
    /// Writes a draft back into the project, but only if the project has not moved since the
    /// snapshot. Returns the new revision.
    /// </summary>
    public async Task<int> ApplyAsync(McpProjectSnapshot basis,
        IReadOnlyList<MacroItem> macros, IReadOnlyList<VariableDefinition> globals)
        => await ApplyAsync(basis, macros, basis.Triggers, globals);

    public async Task<int> ApplyAsync(McpProjectSnapshot basis,
        IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals)
        => await ui.InvokeAsync(() =>
        {
            if (project.Revision != basis.Revision)
            {
                throw new McpFailure(McpErrors.RevisionConflict,
                    "The project changed in WhaleGenie while this draft was open.",
                    "The person edited the project in the window; open the project again and re-apply the change." +
                    " Nothing was written.",
                    retryable: true, details: new { basis = basis.Revision, now = project.Revision });
            }

            project.ApplyDraft(macros, triggers, globals);
            McpAudit.Note("project.apply", $"{macros.Count} macros, revision {project.Revision}");
            return project.Revision;
        });

    /// <summary>
    /// Writes the project's current package. The old file is kept as .bak by the package writer
    /// itself, so this path and the window's own Save behave the same way.
    /// </summary>
    public async Task<string> SaveAsync()
        => await ui.InvokeAsync(() =>
        {
            var path = project.CurrentPath;
            if (path is null || path.Length == 0)
            {
                throw new McpFailure(McpErrors.NoPackagePath,
                    "The project has no package path yet, so there is nothing to save to.",
                    "Ask the person to save the project once in WhaleGenie, or to choose a location when MCP starts.");
            }

            project.SavePackage(path);
            McpAudit.Note("project.save", path);
            return path;
        });

    private McpProjectSnapshot Snapshot()
    {
        var macros = project.Macros.Select(macro => MacroItem.FromJson(macro.ToJson())).ToList();
        var triggers = project.Triggers.Copy();
        return new(
            project.CurrentPath,
            project.Revision,
            macros,
            [.. project.Variables.Globals.Select(Copy)],
            triggers);
    }

    private static VariableDefinition Copy(VariableDefinition variable)
        => new()
        {
            Name = variable.Name,
            Scope = VariableScope.Global,
            Type = variable.Type,
            Description = variable.Description,
            Default = variable.Default,
        };
}
