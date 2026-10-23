using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Mcp;

/// <summary>A deep copy of the project at the moment an MCP draft was opened.</summary>
public sealed record McpProjectSnapshot(
    string? Path, int Revision, IReadOnlyList<MacroItem> Macros,
    IReadOnlyList<VariableDefinition> Globals, ProjectTriggers Triggers);

/// <summary>What the MCP client can see about the project without opening a draft.</summary>
public sealed record McpProjectStatus(string? Path, int Revision, bool Dirty, int MacroCount);

/// <summary>
/// The only bridge between the HTTP tools and the interface's project. Every read and write is
/// marshalled to the interface thread: the project is an Avalonia view model and must not be
/// touched from a Kestrel thread.
/// </summary>
public sealed class McpLiveProject(MainViewModel project)
{
    public async Task<McpProjectStatus> StatusAsync()
        => await Dispatcher.UIThread.InvokeAsync(() =>
            new McpProjectStatus(project.CurrentPath, project.Revision, project.IsDirty,
                project.Macros.Count));

    public async Task<McpProjectSnapshot> SnapshotAsync()
        => await Dispatcher.UIThread.InvokeAsync(Snapshot);

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
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (project.Revision != basis.Revision)
            {
                throw new McpException(
                    "The project changed in WhaleGenie while this draft was open. Open the project again.");
            }

            project.ApplyDraft(macros, triggers, globals);
            return project.Revision;
        });

    /// <summary>Writes the project's current package, keeping a copy of the old file.</summary>
    public async Task<string> SaveAsync()
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var path = project.CurrentPath;
            if (path is null || path.Length == 0)
            {
                throw new McpException(
                    "The project has no package path yet. Save it once in WhaleGenie first.");
            }

            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
            }

            project.SavePackage(path);
            return path;
        });

    private McpProjectSnapshot Snapshot()
    {
        var macros = project.Macros.Select(macro => MacroItem.FromJson(macro.ToJson())).ToList();
        var triggers = project.Triggers.Copy();
        triggers.Attach(macros);
        return new(
            project.CurrentPath,
            project.Revision,
            macros,
            [.. VariableCatalog.Globals.Select(Copy)],
            triggers);
    }

    private static VariableDefinition Copy(VariableDefinition variable)
        => new()
        {
            Name = variable.Name,
            Scope = VariableScope.Global,
            Type = variable.Type,
            Description = variable.Description,
            DefaultValue = variable.DefaultValue,
        };
}
