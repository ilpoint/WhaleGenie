using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One macro on an interface panel, as the page draws it.</summary>
[BridgeContract]
public sealed record SurfacePanelItem(
    string Label,
    string State,
    string? IconKind,
    string? IconValue);

/// <summary>
/// One interface group's panel: its title, orientation, and the members that can be shown. Which
/// window it anchors to, where it sits and when it appears stay in C#; the page only lays out what
/// it is given.
/// </summary>
[BridgeContract]
public sealed record SurfacePanelSnapshot(
    string GroupKey,
    string Title,
    bool Horizontal,
    bool Collapsed,
    IReadOnlyList<SurfacePanelItem> Items);
