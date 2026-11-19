using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>A deep copy of the project at the moment an MCP draft was opened.</summary>
public sealed record McpProjectSnapshot(
    string? Path, int Revision, IReadOnlyList<MacroItem> Macros,
    IReadOnlyList<VariableDefinition> Globals, ProjectTriggers Triggers);

/// <summary>What the MCP client can see about the project without opening a draft.</summary>
public sealed record McpProjectStatus(string? Path, int Revision, bool Dirty, int MacroCount);
