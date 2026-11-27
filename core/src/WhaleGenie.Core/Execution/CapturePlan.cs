using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Where a capture starts and how far it reaches. A capture is a rectangle on the screen: its corner
/// is either the pixels a step wrote or those pixels counted from the corner the step measures from,
/// and each side is either the number written or, for a side whose empty cell means "to the edge",
/// the rest of the screen from that corner. A rectangle is never smaller than one pixel, because an
/// empty picture is not what a step asking for a picture meant.
/// </summary>
internal static class CapturePlan
{
    /// <summary>One written position, in the screen pixels the devices ask for.</summary>
    public static ScreenPoint Placed(ScreenPoint? origin, int x, int y)
        => origin is { } corner ? new ScreenPoint(corner.X + x, corner.Y + y) : new ScreenPoint(x, y);

    /// <summary>One side of the rectangle: the number written, or the rest of the screen.</summary>
    public static int Reach(bool given, ActionEmptyMeans emptyMeans, int value, int from, int limit)
        => !given && emptyMeans == ActionEmptyMeans.ToEdge
            ? Math.Max(1, limit - from)
            : Math.Max(1, value);
}
