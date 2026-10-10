using System;

namespace WhaleGenie.Views;

/// <summary>
/// How big the "Add Action" dialog opens. Filling in a step's settings takes room — four pages of
/// fields do not read in a column 600 wide — but a laptop screen has less of it, so the wanted size
/// is trimmed to what the desktop actually offers. Kept as a plain sum over two numbers so both
/// screens can be checked without a window between them.
/// </summary>
internal static class DialogSize
{
    /// <summary>The size the dialog opens at when there is room for it.</summary>
    public const double WantedWidth = 1024;

    public const double WantedHeight = 800;

    /// <summary>Below this the four pages stop being readable, so it is never gone below.</summary>
    public const double SmallestWidth = 900;

    public const double SmallestHeight = 560;

    /// <summary>
    /// Room left at every edge of the desktop, so the dialog is never flush against it and the
    /// taskbar never sits on top of the buttons at the bottom.
    /// </summary>
    public const double EdgeRoom = 40;

    /// <summary>The size to open at, given the room the desktop has for a window.</summary>
    public static (double Width, double Height) Fit(double roomWidth, double roomHeight)
        => (Within(WantedWidth, roomWidth, SmallestWidth), Within(WantedHeight, roomHeight, SmallestHeight));

    /// <summary>The size wanted, no more than the room there is, and never less than the smallest.</summary>
    private static double Within(double wanted, double room, double smallest)
        => Math.Max(smallest, Math.Min(wanted, room - (EdgeRoom * 2)));
}
