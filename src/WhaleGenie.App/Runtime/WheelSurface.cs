using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Projects the project's wheel into the shape the page draws. The scene resolution and the
/// geometry stay where they are; this only names the rings and strips icons to the wire's spelling.
/// </summary>
internal sealed class WheelSurface(ProjectWorkspace workspace)
{
    public const double Diameter = 420;

    public SurfaceWheelSnapshot Snapshot(SurfaceWheelAim? aim = null)
    {
        var wheel = workspace.Triggers.Wheel;
        var layout = wheel.Resolve(null, WheelSource.Global, wideOuter: false,
            [.. workspace.Macros], thirdCapacity: 0, hideEmptyOuter: false);
        var rings = layout.ThirdCapacity > 0 ? 3 : layout.HideEmptyOuter ? 1 : 2;

        return new SurfaceWheelSnapshot(Diameter, layout.WideOuter, rings, layout.ThirdCapacity,
            layout.SceneName, [.. layout.Slots.Where(slot => slot.Label.Length > 0).Select(Slot)], aim);
    }

    private static SurfaceWheelSlot Slot(WheelRuntimeSlot slot)
    {
        var icon = slot.Icon;
        var kind = icon is { IsEmpty: false } ? icon.Kind.ToString().ToLowerInvariant() : null;
        return new SurfaceWheelSlot(slot.Reference.Ring.ToString().ToLowerInvariant(),
            slot.Reference.Index, slot.Label, kind, icon?.Value);
    }
}
