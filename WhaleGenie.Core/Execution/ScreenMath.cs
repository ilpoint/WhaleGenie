using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>Where a rectangle's middle is, which is the point an action aims at.</summary>
internal static class ScreenMath
{
    /// <summary>The middle of a rectangle.</summary>
    public static ScreenPoint Middle(ScreenPoint corner, ScreenSize size)
        => new(corner.X + (size.Width / 2), corner.Y + (size.Height / 2));
}
