namespace WhaleGenie.Contracts;

/// <summary>
/// One multi-step edit. Kind is enabled/remove/duplicate/group/move/moveBy/lift/copy/cut/paste;
/// paste is the one kind that does not require Steps. A move either names a Target and Before it,
/// or a ParentId whose list named by List (the first when omitted) takes the steps at Index.
/// </summary>
[BridgeContract]
[BridgeRequest]
public sealed record EditorStepsCommand(
    string Kind,
    string[]? Steps = null,
    bool? Enabled = null,
    string? Target = null,
    bool? Before = null,
    int? Delta = null,
    string? Text = null,
    string? After = null,
    string? ParentId = null,
    string? List = null,
    int? Index = null);
