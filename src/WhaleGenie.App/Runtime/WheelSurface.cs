using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Projects a resolved wheel into the frame the page draws. The scene resolution, the circles that
/// are actually drawn and the hit test stay where they are; this only names the rings, strips icons
/// to the wire's spelling, and says what the centre circle reads.
/// </summary>
internal sealed class WheelSurface
{
    /// <summary>The size a project uses when its options are not in hand (the debug host).</summary>
    public const double DefaultDiameter = 420;

    /// <summary>
    /// How many resident circles are drawn: the native rule — an empty outer circle is hidden only
    /// when it really has no macro, and the third circle needs a capacity and an action.
    /// </summary>
    public static int EffectiveRings(WheelRuntimeLayout layout, WheelOptions options)
    {
        var capacity = layout.ThirdCapacity;
        if (!options.HideEmptyOuter)
        {
            return capacity > 0 ? 3 : 2;
        }

        if (capacity > 0 && layout.Slots.Any(slot =>
                slot.Reference.Ring == WheelRing.Third && slot.Macro is not null))
        {
            return 3;
        }

        return layout.Slots.Any(slot =>
            slot.Reference.Ring == WheelRing.Outer && slot.Macro is not null) ? 2 : 1;
    }

    public static WheelMetrics MetricsOf(WheelRuntimeLayout layout, WheelOptions options)
        => WheelGeometry.For(options.Diameter, layout.WideOuter, layout.ThirdCapacity,
            EffectiveRings(layout, options));

    /// <summary>The wheel the project has right now, for the debug host.</summary>
    public SurfaceWheelSnapshot Snapshot(SurfaceWheelAim? aim = null)
        => Of(Resolve(), new WheelOptions { Diameter = (int)DefaultDiameter }, aim);

    public WheelRuntimeLayout Resolve()
        => Resolve(Workspace);

    public static WheelRuntimeLayout Resolve(ProjectWorkspace workspace)
        => workspace.Triggers.Wheel.Resolve(null, WheelSource.Global, wideOuter: false,
            [.. workspace.Macros], thirdCapacity: 0, hideEmptyOuter: false);

    /// <summary>One frame of a layout the caller already resolved.</summary>
    /// <param name="dx">Pointer offset from the centre, in DIPs. Ignored when there is no aim.</param>
    public static SurfaceWheelSnapshot Of(WheelRuntimeLayout layout, WheelOptions options,
        SurfaceWheelAim? aim = null, double dx = 0, double dy = 0)
    {
        var rings = EffectiveRings(layout, options);
        var metrics = MetricsOf(layout, options);
        var drawn = Rings(metrics, rings);
        var names = drawn.Select(ring => ring.Ring).ToHashSet(StringComparer.Ordinal);
        var slots = layout.Slots
            .Where(slot => slot.Name.Trim().Length > 0)
            .Where(slot => names.Contains(Spelling(slot.Reference.Ring)))
            .Select(Slot)
            .ToList();
        var pointed = aim is null ? null : aim with { X = aim.X + dx, Y = aim.Y + dy };
        return new SurfaceWheelSnapshot(
            options.Diameter,
            options.Diameter * 2,
            drawn,
            layout.SceneName,
            CenterText(layout, options, aim),
            slots,
            pointed,
            options.Gloss,
            options.HoverBorder,
            options.ShowGuide,
            options.HideLabelWithIcon,
            options.FollowTheme,
            options.FontSize,
            options.CenterName);
    }

    private static IReadOnlyList<SurfaceWheelRing> Rings(WheelMetrics metrics, int rings)
    {
        var list = new List<SurfaceWheelRing>
        {
            new("inner", metrics.Center, metrics.Inner, WheelShape.InnerCount),
        };
        if (rings >= 2 && metrics.HasOuter)
        {
            list.Add(new("outer", metrics.Inner, metrics.Outer, metrics.OuterCount));
        }

        if (rings >= 3 && metrics.HasThird)
        {
            list.Add(new("third", metrics.Outer, metrics.Third, Math.Max(1, metrics.ThirdCount)));
        }

        return list;
    }

    private static string CenterText(WheelRuntimeLayout layout, WheelOptions options,
        SurfaceWheelAim? aim)
    {
        if (aim is null)
        {
            return Strings.Get("Wheel.Close");
        }

        var ring = RingOf(aim.Ring);
        var slot = ring is { } value ? layout.At(new WheelSlotRef(value, aim.Index)) : null;
        var name = slot?.Label ?? string.Empty;
        if (ring == WheelRing.Extended && options.ExtendedHint && name.Length > 0)
        {
            return name;
        }

        return options.CenterName && slot?.Macro is not null && name.Length > 0
            ? name
            : layout.SceneName;
    }

    private static SurfaceWheelSlot Slot(WheelRuntimeSlot slot)
    {
        var icon = slot.Icon;
        var kind = icon is { IsEmpty: false } ? icon.Kind.ToString().ToLowerInvariant() : null;
        return new SurfaceWheelSlot(Spelling(slot.Reference.Ring), slot.Reference.Index,
            slot.Label, kind, icon?.Value, ToneOf(slot));
    }

    private static string ToneOf(WheelRuntimeSlot slot)
        => slot.Reference.Ring == WheelRing.Extended ? "extended"
            : slot.Macro is null ? "empty"
            : slot.FromProgram ? "overridden" : "global";

    private static string Spelling(WheelRing ring) => ring.ToString().ToLowerInvariant();

    private static WheelRing? RingOf(string spelling)
        => Enum.TryParse<WheelRing>(spelling, ignoreCase: true, out var ring) ? ring : null;

    private ProjectWorkspace Workspace { get; }

    public WheelSurface(ProjectWorkspace workspace) => Workspace = workspace;
}
