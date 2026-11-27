using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>
/// One edit of the local or global variable table. Family is "local" or "global"; kind is
/// add/patch/remove.
/// </summary>
[BridgeContract]
[BridgeRequest]
public sealed record EditorVariableCommand(
    string Family,
    string Kind,
    string? Name = null,
    string? OriginalName = null,
    string? Field = null,
    string? Value = null,
    IReadOnlyList<EditorValuePartSnapshot>? Parts = null);
