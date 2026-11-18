using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The size of a wheel, and where a pointer lands on it.
/// <summary>The radii of the resident circles, in window coordinates.</summary>
public sealed record WheelMetrics(
    double Center,
    double Inner,
    double Outer,
    int OuterCount,
    double Third = 0,
    int ThirdCount = 0)
{
    /// <summary>Where the visible wheel ends; past it lies the invisible extended circle.</summary>
    public double Edge => ThirdCount > 0 ? Third : Outer;

    public bool HasOuter => Outer > Inner + 1;

    public bool HasThird => ThirdCount > 0 && Third > Outer + 1;
}

/// <summary>
/// Turning a pointer position into a wheel position. Kept free of Avalonia and of the window so the
/// direction and distance rules can be checked without a desktop.
/// </summary>
public static class WheelGeometry
{
    public const double CenterShare = 0.22;
    public const double InnerShare = 0.56;
    public const double OuterShare = 0.94;

    /// <summary>
    /// Rings is how many resident circles are actually drawn (1, 2 or 3). Empty outer circles can
    /// be hidden so the wheel shrinks to the circles that carry actions; leaving it at zero picks
    /// two circles, or three when the third circle has a capacity.
    /// </summary>
    public static WheelMetrics For(double diameter, bool wideOuter, int thirdCapacity = 0,
        int rings = 0)
    {
        var capacity = WheelShape.ClampThird(thirdCapacity);
        var drawn = rings <= 0 ? (capacity > 0 ? 3 : 2) : Math.Clamp(rings, 1, 3);
        var outerCount = wideOuter ? WheelShape.OuterWideCount : WheelShape.OuterCount;
        var half = diameter / 2;
        return drawn switch
        {
            1 => new WheelMetrics(half * 0.30, half * 0.94, half * 0.94, outerCount),
            3 => new WheelMetrics(half * 0.16, half * 0.40, half * 0.66, outerCount,
                half * 0.94, capacity),
            _ => new WheelMetrics(half * CenterShare, half * InnerShare,
                half * OuterShare, outerCount),
        };
    }

    /// <summary>
    /// Straight up is the middle of position zero, and positions run clockwise. A release inside
    /// the centre circle cancels; a release past the outer edge belongs to the invisible extended
    /// circle, which is why it is answered by direction rather than by a shape on screen.
    /// </summary>
    public static WheelSlotRef? At(double dx, double dy, WheelMetrics metrics)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < metrics.Center)
        {
            return null;
        }

        if (distance < metrics.Inner)
        {
            return new WheelSlotRef(WheelRing.Inner, Sector(dx, dy, WheelShape.InnerCount));
        }

        if (metrics.HasOuter && distance < metrics.Outer)
        {
            return new WheelSlotRef(WheelRing.Outer, Sector(dx, dy, metrics.OuterCount));
        }

        if (metrics.HasThird && distance < metrics.Third)
        {
            return new WheelSlotRef(WheelRing.Third, Sector(dx, dy, metrics.ThirdCount));
        }

        return new WheelSlotRef(WheelRing.Extended,
            Sector(dx, dy, WheelShape.ExtendedCount));
    }

    public static int Sector(double dx, double dy, int count)
    {
        var angle = Math.Atan2(dx, -dy);
        if (angle < 0)
        {
            angle += Math.PI * 2;
        }

        var step = Math.PI * 2 / count;
        return (int)Math.Floor((angle + step / 2) / step) % count;
    }

    /// <summary>Where the label of a position sits, relative to the wheel's centre.</summary>
    public static (double X, double Y) LabelAt(WheelSlotRef reference, WheelMetrics metrics,
        double diameter)
    {
        var radius = reference.Ring switch
        {
            WheelRing.Inner => (metrics.Center + metrics.Inner) / 2,
            WheelRing.Outer => (metrics.Inner + metrics.Outer) / 2,
            WheelRing.Third => (metrics.Outer + metrics.Third) / 2,
            _ => (metrics.Edge + diameter / 2) / 2,
        };
        var count = reference.Ring switch
        {
            WheelRing.Outer => metrics.OuterCount,
            WheelRing.Third => Math.Max(1, metrics.ThirdCount),
            WheelRing.Extended => WheelShape.ExtendedCount,
            _ => WheelShape.InnerCount,
        };
        var angle = reference.Index * Math.PI * 2 / count;
        return (radius * Math.Sin(angle), -radius * Math.Cos(angle));
    }
}
