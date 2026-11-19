using System.Collections.Generic;
using System.Threading.Tasks;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>
/// What the MCP tools and resources need from the project: read it as it stands, take a draft from
/// a revision, apply that draft only if the revision has not moved, and write the package. The
/// implementation lives beside the window because every call has to reach the document on the
/// interface thread; a client sees only this surface.
/// </summary>
/// <remarks>
/// The interface is here rather than beside the implementation so the MCP side can move to a
/// project of its own without taking the window with it: the window supplies an implementation,
/// and a check supplies one that answers without a dispatcher.
/// </remarks>
public interface IProjectGateway
{
    /// <summary>What can be said about the project without opening a draft.</summary>
    Task<McpProjectStatus> StatusAsync();

    /// <summary>A copy of the project at the revision the draft will be based on.</summary>
    Task<McpProjectSnapshot> SnapshotAsync();

    /// <summary>The snapshot and whether the project has unsaved changes, read in one turn.</summary>
    Task<(McpProjectSnapshot Snapshot, bool Dirty)> ReadAsync();

    /// <summary>
    /// Writes a draft back into the project only if the project has not moved since the snapshot,
    /// answering with the new revision.
    /// </summary>
    Task<int> ApplyAsync(McpProjectSnapshot basis, IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals);

    /// <summary>The same write, with the trigger entries the draft carries.</summary>
    Task<int> ApplyAsync(McpProjectSnapshot basis, IReadOnlyList<MacroItem> macros,
        ProjectTriggers triggers, IReadOnlyList<VariableDefinition> globals);

    /// <summary>Writes the project's current package and answers with the path it went to.</summary>
    Task<string> SaveAsync();
}
