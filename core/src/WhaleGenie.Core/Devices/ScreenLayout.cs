namespace WhaleGenie.Core.Devices;

/// <summary>One display, as Windows lays the desktop out.</summary>
public sealed record ScreenDisplay(ScreenRect Bounds, bool Primary);

/// <summary>
/// The desktop as Windows lays it out: one rectangle covering every display, which is what "the
/// whole screen" means, and the displays it is made of. More than one display is a desktop wider or
/// taller than any single one of them, and reading it takes a piece from each.
/// </summary>
public sealed record ScreenLayout(ScreenRect Bounds, IReadOnlyList<ScreenDisplay> Displays)
{
    /// <summary>
    /// The display a point is on, or the primary one when the point is on none of them — which is
    /// what a window dragged off the edge of the desktop lands on, and the answer that keeps a
    /// step reading something rather than nothing.
    /// </summary>
    public ScreenDisplay At(int x, int y)
        => Displays.FirstOrDefault(display => display.Bounds.Holds(x, y))
            ?? Displays.FirstOrDefault(display => display.Primary)
            ?? Displays[0];
}
