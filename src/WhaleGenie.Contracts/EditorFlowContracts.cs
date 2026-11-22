using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>The answer of the value editor: whether it closed, and whether the field was written.</summary>
[BridgeContract]
public sealed record EditorValueOutcome(
    bool Closed, bool Applied, EditorValueSnapshot Snapshot);

/// <summary>One box a trial found on the frame.</summary>
[BridgeContract]
public sealed record EditorTrialBox(
    int X, int Y, int Width, int Height, double Score, string Role, string? Label);

/// <summary>What one trial run of the draft step came to.</summary>
[BridgeContract]
public sealed record EditorTrialResult(
    bool Success, string Status, string Key, string Detail, string? Image,
    IReadOnlyList<EditorTrialBox> Boxes, int ChosenIndex, string Looking,
    int OriginX, int OriginY, int FrameWidth, int FrameHeight);

/// <summary>The page's answer to a trial: run it, or ask before an irreversible step.</summary>
[BridgeContract]
public sealed record EditorTrialReply(
    bool NeedsConfirm, string? Title, string? Question, string? Refused,
    EditorTrialResult? Result);

/// <summary>The action catalogue and the rail's order.</summary>
[BridgeContract]
public sealed record EditorCatalogue(
    int Count,
    IReadOnlyList<EditorActionSnapshot> Actions,
    IReadOnlyList<EditorActionCategorySnapshot> Categories,
    IReadOnlyList<EditorPublicMacroSnapshot> PublicMacros);
