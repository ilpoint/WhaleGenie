namespace WhaleGenie.Contracts;

/// <summary>
/// One edit of a condition tree. Kind is rowAdd/groupAdd/rowRemove/row/join; Path addresses the
/// node, or Index addresses a top-level row.
/// </summary>
[BridgeContract]
[BridgeRequest]
public sealed record EditorConditionCommand(
    string Kind,
    string StepId,
    string Name,
    int[]? Path = null,
    int? Index = null,
    string? Field = null,
    string? Value = null,
    string? Join = null);
