using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One drawn circle of the wheel: its radii and how many positions it has.</summary>
/// <remarks>
/// The radii are DIPs from the wheel's centre, not from the corner of the square window; the page
/// adds the square's half size. Sending them keeps the painter and the hit test on one geometry.
/// </remarks>
[BridgeContract]
public sealed record SurfaceWheelRing(string Ring, double From, double To, int Count);

/// <summary>One filled position on the wheel, as the page draws it.</summary>
[BridgeContract]
public sealed record SurfaceWheelSlot(
    string Ring,
    int Index,
    string Label,
    string? IconKind,
    string? IconValue,
    string Tone);

/// <summary>Where the pointer is aiming: the position it lands in, and where it is on the square.</summary>
/// <remarks>
/// <paramref name="X"/> and <paramref name="Y"/> are DIPs from the wheel's centre and may run past
/// the visible circles; the page clips the guide line to the square window.
/// </remarks>
[BridgeContract]
public sealed record SurfaceWheelAim(string Ring, int Index, double X, double Y);

/// <summary>
/// The wheel as one frame: its size, the circles it draws, and the positions that carry something.
/// Geometry is not repeated in the page — the same ring descriptions drive both drawing and aim.
/// </summary>
[BridgeContract]
public sealed record SurfaceWheelSnapshot(
    double Diameter,
    double CanvasSize,
    IReadOnlyList<SurfaceWheelRing> Rings,
    string SceneName,
    string CenterText,
    IReadOnlyList<SurfaceWheelSlot> Slots,
    SurfaceWheelAim? Aim = null, bool Gloss = true, bool HoverBorder = true,
    bool ShowGuide = true, bool HideLabelWithIcon = false, bool FollowTheme = true,
    double FontSize = 12, double CenterX = -1, double CenterY = -1);
