using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Devices;

/// <summary>How the pointer travels from one point to another.</summary>
public enum MouseRoute
{
    /// <summary>In a straight line at an even speed, which is what a macro asks for by default.</summary>
    Direct,

    /// <summary>Along a shallow arc instead of cutting straight across.</summary>
    Smooth,

    /// <summary>Along an arc with a hand's drift on it, slowing down at both ends.</summary>
    Human,
}

/// <summary>
/// Works out the stops a move is made of. A straight move is a line; a bent or hand-like move is
/// a shallow arc, and a hand-like one also drifts either side of it and covers more ground in the
/// middle than at the ends. The device waits the same time between two stops, so where the stops
/// sit is what makes the pointer speed up, slow down, and look like it was pushed by a person.
/// </summary>
public static class MousePath
{
    /// <summary>The most a path may bow away from the straight line, in pixels.</summary>
    private const double MostBow = 120;

    /// <summary>How far a bent path bows, as a share of the distance it covers.</summary>
    private const double BowShare = 0.08;

    /// <summary>The least a hand-like path bows, so that no two moves trace the same curve.</summary>
    private const double LeastBowShare = 0.05;

    /// <summary>How far a hand-like path may drift off the arc, in pixels.</summary>
    private const double MostDrift = 3;

    /// <summary>Reads the style a step asked for; anything unrecognised travels straight.</summary>
    public static MouseRoute Route(string style) => style.Trim().ToLowerInvariant() switch
    {
        "smooth" => MouseRoute.Smooth,
        "human" => MouseRoute.Human,
        _ => MouseRoute.Direct,
    };

    /// <summary>
    /// How many stops a move of this length is broken into. A straight move keeps the pacing the
    /// device has always used; a bent one is broken into finer stops, because an arc drawn in a
    /// handful of them shows its corners.
    /// </summary>
    public static int StepsFor(MouseRoute route, int durationMs) => route is MouseRoute.Direct
        ? Math.Clamp(durationMs / 16, 1, 120)
        : Math.Clamp(durationMs / 8, 12, 120);

    /// <summary>
    /// The points the pointer should visit, starting where it already is and ending exactly at
    /// <paramref name="to"/>. <paramref name="steps"/> is how many stops follow the start.
    /// </summary>
    public static IReadOnlyList<ScreenPoint> Plan(MouseRoute route, ScreenPoint from, ScreenPoint to,
        int steps, Random? random = null)
    {
        var count = Math.Clamp(steps, 1, 200);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        if (route is MouseRoute.Direct)
        {
            return Straight(from, dx, dy, count);
        }

        var dice = random ?? Random.Shared;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        // The arc bows to one side or the other, and a hand-like move picks how much it bows,
        // so that a pair of moves never trace the same curve.
        var side = dice.Next(2) == 0 ? 1 : -1;
        var share = route is MouseRoute.Human
            ? LeastBowShare + (dice.NextDouble() * (BowShare - LeastBowShare))
            : BowShare;
        var bow = Math.Min(MostBow, distance * share) * side;

        // A unit vector across the line: the direction the arc bulges in.
        var acrossX = distance > 0 ? -dy / distance : 0;
        var acrossY = distance > 0 ? dx / distance : 0;

        var drift = route is MouseRoute.Human ? MostDrift : 0;
        var wander = 0.0;
        var path = new List<ScreenPoint>(count + 1) { from };
        for (var index = 1; index <= count; index++)
        {
            var ratio = (double)index / count;

            // Hand-like moves sit closer together at the ends than in the middle, which is the
            // pointer easing in and out of the move.
            var along = route is MouseRoute.Human ? Eased(ratio) : ratio;
            var across = Math.Sin(Math.PI * ratio) * bow;

            if (drift > 0 && index < count)
            {
                // A slow random walk rather than fresh noise at every stop: a hand drifts, it
                // does not shake. The last stop is left on the arc, so the pointer still lands
                // exactly where the macro said rather than a few pixels off it.
                wander = Math.Clamp(wander + (((dice.NextDouble() * 2) - 1) * drift), -drift, drift);
                across += wander;
            }

            path.Add(new ScreenPoint(
                (int)Math.Round(from.X + (dx * along) + (acrossX * across)),
                (int)Math.Round(from.Y + (dy * along) + (acrossY * across))));
        }

        return path;
    }

    private static IReadOnlyList<ScreenPoint> Straight(ScreenPoint from, int dx, int dy, int steps)
    {
        var path = new List<ScreenPoint>(steps + 1) { from };
        for (var index = 1; index <= steps; index++)
        {
            var ratio = (double)index / steps;
            path.Add(new ScreenPoint(
                (int)Math.Round(from.X + (dx * ratio)),
                (int)Math.Round(from.Y + (dy * ratio))));
        }

        return path;
    }

    /// <summary>Slows the start and the end, the way a hand does.</summary>
    private static double Eased(double ratio)
        => ratio * ratio * ratio * ((ratio * ((ratio * 6) - 15)) + 10);
}
