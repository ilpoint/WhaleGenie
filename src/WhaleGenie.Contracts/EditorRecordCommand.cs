namespace WhaleGenie.Contracts;

/// <summary>
/// One recording command. Kind is start/stop/state/option/delayScale; option needs Field and
/// Value, delayScale needs Factor.
/// </summary>
[BridgeContract]
[BridgeRequest]
public sealed record EditorRecordCommand(
    string Kind,
    string? Field = null,
    string? Value = null,
    double? Factor = null);
