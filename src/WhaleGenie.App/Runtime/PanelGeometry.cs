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

    /// <summary>
    /// The card's border and the padding around the members: one pixel of border on each side and
    /// two of padding inside it. The page draws the card with the same numbers, so the window has
    /// to include them or its rounded right and bottom corners would be cut off.
    /// </summary>
    private const int PanelChrome = 6;

    /// <summary>The card's corner radius, shared with the page's panel style and the window region.</summary>
    public const int PanelCornerRadius = 6;

    public static (int Width, int Height) Size(int count, bool horizontal, bool collapsed)
    {
        var visible = Math.Max(1, Math.Min(MaximumVisible, count));
        if (collapsed)
        {
            return ((int)PanelWidth, (int)HeaderHeight + 2);
        }

        if (horizontal)
        {
            return ((int)(visible * MacroWidth + Math.Max(0, visible - 1) * MacroGap + PanelChrome),
                (int)(HeaderHeight + MacroHeight + PanelChrome));
        }

        return ((int)PanelWidth,
            (int)HeaderHeight + PanelChrome + visible * (int)MacroHeight
            + Math.Max(0, visible - 1) * (int)MacroGap);
    }

    /// <summary>
    /// The offset a panel is stored at, held inside the target's client area. A drag may take the
    /// window past its target's edge while the pointer is down; the place that is kept, and the
    /// place every later frame is drawn at, is still inside the window the buttons belong to.
    /// </summary>
    public static (int X, int Y) ClampOffset((int Width, int Height) client,
        int offsetX, int offsetY, (int Width, int Height) panel)
    {
        var x = Math.Clamp(offsetX, 0, Math.Max(0, client.Width - panel.Width));
        var y = Math.Clamp(offsetY, 0, Math.Max(0, client.Height - panel.Height));
        return (x, y);
    }

    /// <summary>Placement inside the target, clamped to the client area the way the native panel was.</summary>
    public static (int X, int Y) Place((int X, int Y) origin, (int Width, int Height) client,
        int offsetX, int offsetY, (int Width, int Height) panel)
    {
        var (x, y) = ClampOffset(client, offsetX, offsetY, panel);
        return (origin.X + x, origin.Y + y);
    }
}
