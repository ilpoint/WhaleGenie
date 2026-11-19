using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// Which ring and where a wheel opens from, and how many places a ring has.
/// <summary>Which of the three circles a wheel position belongs to.</summary>
public enum WheelRing
{
    Inner,
    Outer,
    Third,
    Extended,
}

/// <summary>
/// What the wheel shows while it is open. The source can be changed with the wheel; the scene and
/// the layout it produced stay fixed for the rest of that session, because a wheel someone is
/// aiming at must not change shape under the pointer when another window happens to come forward.
/// </summary>
public enum WheelSource
{
    Merged,
    Global,
    Program,
}

/// <summary>
/// One position on the wheel, named by its circle and its clockwise index from straight up.
/// </summary>
public readonly record struct WheelSlotRef(WheelRing Ring, int Index);

/// <summary>
/// The fixed capacities of the three circles. The outer circle keeps sixteen slots even when the
/// layout only shows eight: narrowing the layout hides positions, it does not throw their actions
/// away, so widening it again puts the same actions back in the same directions.
/// </summary>
public static class WheelShape
{
    public const int InnerCount = 8;
    public const int OuterCount = 8;
    public const int OuterWideCount = 16;
    public const int ThirdCount = 24;
    public const int ExtendedCount = 8;

    /// <summary>The capacities the third resident circle offers. Zero hides it.</summary>
    public static int[] ThirdCapacities => [0, 8, 16, 24];

    public static int CountOf(WheelRing ring, bool wideOuter, int thirdCapacity) => ring switch
    {
        WheelRing.Inner => InnerCount,
        WheelRing.Outer => wideOuter ? OuterWideCount : OuterCount,
        WheelRing.Third => ClampThird(thirdCapacity),
        _ => ExtendedCount,
    };

    /// <summary>The two-circle shape used by older callers and by the editor's default preview.</summary>
    public static int CountOf(WheelRing ring, bool wideOuter) => CountOf(ring, wideOuter, 0);

    public static int CapacityOf(WheelRing ring) => ring switch
    {
        WheelRing.Inner => InnerCount,
        WheelRing.Outer => OuterWideCount,
        WheelRing.Third => ThirdCount,
        _ => ExtendedCount,
    };

    public static int ClampThird(int capacity)
        => ThirdCapacities.Contains(capacity) ? capacity : 0;
}
