namespace WhaleGenie.Contracts;

/// <summary>The theme choice the page paints with. The program derives it; the page only applies it.</summary>
[BridgeContract]
public sealed record ThemeState(
    string Mode,
    bool BaseLight,
    string? AccentHex,
    bool AccentUsable,
    string Fill,
    string Surface,
    string Text,
    string DefaultAccent);

/// <summary>The window's own state, as the title bar mirrors it.</summary>
[BridgeContract]
public sealed record WindowState(string State, bool Topmost);

/// <summary>One question the program asks the page, and the two answers it can give.</summary>
[BridgeContract]
public sealed record DialogRequest(
    int RequestId,
    string Title,
    string Message,
    string ConfirmLabel,
    string? CancelLabel);
