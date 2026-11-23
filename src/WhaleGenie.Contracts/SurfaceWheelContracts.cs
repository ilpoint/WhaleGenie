using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One position on the wheel, as the page draws it.</summary>
[BridgeContract]
public sealed record SurfaceWheelSlot(
    string Ring,
    int Index,
    string Label,
    string? IconKind,
    string? IconValue);

/// <summary>
/// The wheel as one frame: its size, which rings are drawn, and the positions that carry something.
/// Geometry is not repeated here — the page computes the same sectors from the same parameters, and
/// the hit test stays in C# so a pointer landing on a pixel never depends on the page's layout.
/// </summary>
/// <summary>Where the pointer is aiming, or nothing when it is over the centre.</summary>
[BridgeContract]
public sealed record SurfaceWheelAim(string Ring, int Index);

[BridgeContract]
public sealed record SurfaceWheelSnapshot(
    double Diameter,
    bool WideOuter,
    int Rings,
    int ThirdCapacity,
    string SceneName,
    IReadOnlyList<SurfaceWheelSlot> Slots,
    SurfaceWheelAim? Aim = null);
