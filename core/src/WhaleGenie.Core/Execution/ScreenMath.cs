using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>Where a rectangle's middle is, which is the point an action aims at.</summary>
internal static class ScreenMath
{
    /// <summary>One written position, counted from the corner a step says its numbers are from.</summary>
    public static ScreenPoint Placed(ScreenPoint? origin, int x, int y)
        => origin is { } corner ? new ScreenPoint(corner.X + x, corner.Y + y) : new ScreenPoint(x, y);

    /// <summary>The middle of a rectangle.</summary>
    public static ScreenPoint Middle(ScreenPoint corner, ScreenSize size)
        => new(corner.X + (size.Width / 2), corner.Y + (size.Height / 2));
}
