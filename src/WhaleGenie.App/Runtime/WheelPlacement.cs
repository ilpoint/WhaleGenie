using System;

namespace WhaleGenie.Runtime;

/// <summary>
/// The wheel's centre after screen clamping. Kept apart from the window so the edge rule can be
/// checked without a display: only the visible circle is clamped, never the transparent square.
/// </summary>
public static class WheelPlacement
{
    public static (double X, double Y) Center(double originX, double originY, double wheel,
        double left, double top, double right, double bottom, bool clamp)
    {
        if (!clamp)
        {
            return (originX, originY);
        }

        var half = wheel / 2;
        return (Math.Clamp(originX, left + half, Math.Max(left + half, right - half)),
            Math.Clamp(originY, top + half, Math.Max(top + half, bottom - half)));
    }
}
