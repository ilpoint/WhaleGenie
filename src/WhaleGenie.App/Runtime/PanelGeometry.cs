using System;

namespace WhaleGenie.Runtime;

/// <summary>
/// The panel's size and where it sits inside its target's client area. The numbers are the native
/// panel's; keeping them here means the page and the placement agree without the page measuring.
/// </summary>
public static class PanelGeometry
{
    public const double MacroWidth = 80;
    public const double MacroHeight = 24;
    public const double MacroGap = 2;
    public const double HeaderHeight = 20;
    public const double PanelWidth = 86;
    public const int MaximumVisible = 6;

    public static (int Width, int Height) Size(int count, bool horizontal, bool collapsed)
    {
        var visible = Math.Max(1, Math.Min(MaximumVisible, count));
        if (collapsed)
        {
            return ((int)PanelWidth, (int)HeaderHeight + 2);
        }

        if (horizontal)
        {
            return ((int)(visible * MacroWidth + Math.Max(0, visible - 1) * MacroGap + 2),
                (int)(HeaderHeight + MacroHeight + 2));
        }

        return ((int)PanelWidth,
            (int)(HeaderHeight + 2) + visible * (int)MacroHeight
            + Math.Max(0, visible - 1) * (int)MacroGap);
    }

    /// <summary>Placement inside the target, clamped to the client area the way the native panel was.</summary>
    public static (int X, int Y) Place((int X, int Y) origin, (int Width, int Height) client,
        int offsetX, int offsetY, (int Width, int Height) panel)
    {
        var x = Math.Clamp(offsetX, 0, Math.Max(0, client.Width - panel.Width));
        var y = Math.Clamp(offsetY, 0, Math.Max(0, client.Height - panel.Height));
        return (origin.X + x, origin.Y + y);
    }
}
