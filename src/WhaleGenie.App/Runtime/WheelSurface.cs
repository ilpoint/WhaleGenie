using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Projects a resolved wheel into the shape the page draws. The scene resolution and the geometry
/// stay where they are; this only names the rings and strips icons to the wire's spelling.
/// </summary>
internal sealed class WheelSurface
{
    public const double Diameter = 420;

    public static WheelMetrics MetricsOf(WheelRuntimeLayout layout)
        => WheelGeometry.For(Diameter, layout.WideOuter, layout.ThirdCapacity,
            layout.ThirdCapacity > 0 ? 3 : layout.HideEmptyOuter ? 1 : 2);

    /// <summary>The wheel the project has right now, for the debug host and the first frame.</summary>
    public SurfaceWheelSnapshot Snapshot(SurfaceWheelAim? aim = null)
        => Of(Resolve(), aim);

    public WheelRuntimeLayout Resolve()
        => Resolve(Workspace);

    public static WheelRuntimeLayout Resolve(ProjectWorkspace workspace)
        => workspace.Triggers.Wheel.Resolve(null, WheelSource.Global, wideOuter: false,
            [.. workspace.Macros], thirdCapacity: 0, hideEmptyOuter: false);

    /// <summary>One frame of a layout the caller already resolved, with the aim it was drawn at.</summary>
    public static SurfaceWheelSnapshot Of(WheelRuntimeLayout layout, SurfaceWheelAim? aim = null)
    {
        var rings = layout.ThirdCapacity > 0 ? 3 : layout.HideEmptyOuter ? 1 : 2;
        return new SurfaceWheelSnapshot(Diameter, layout.WideOuter, rings, layout.ThirdCapacity,
            layout.SceneName,
            [.. layout.Slots.Where(slot => slot.Label.Length > 0).Select(Slot)], aim);
    }

    private static SurfaceWheelSlot Slot(WheelRuntimeSlot slot)
    {
        var icon = slot.Icon;
        var kind = icon is { IsEmpty: false } ? icon.Kind.ToString().ToLowerInvariant() : null;
        return new SurfaceWheelSlot(slot.Reference.Ring.ToString().ToLowerInvariant(),
            slot.Reference.Index, slot.Label, kind, icon?.Value);
    }

    private ProjectWorkspace Workspace { get; }

    public WheelSurface(ProjectWorkspace workspace) => Workspace = workspace;
}