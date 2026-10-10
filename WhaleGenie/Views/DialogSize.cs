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
    /// The width the dialog is allowed down to when it stands beside the window it was opened from
    /// rather than over it. The action list on the left keeps its own width, so it is the fields
    /// that give ground; below this they stop being read comfortably, and covering the window would
    /// have been the better trade.
    /// </summary>
    public const double SmallestBesideWidth = 800;

    /// <summary>
    /// Room left at every edge of the desktop, so the dialog is never flush against it and the
    /// taskbar never sits on top of the buttons at the bottom.
    /// </summary>
    public const double EdgeRoom = 40;

    /// <summary>The size to open at, given the room the desktop has for a window.</summary>
    public static (double Width, double Height) Fit(double roomWidth, double roomHeight)
        => (Within(WantedWidth, roomWidth, SmallestWidth), Within(WantedHeight, roomHeight, SmallestHeight));

    /// <summary>
    /// Where and how wide the dialog opens when it stands beside <paramref name="window"/> instead
    /// of over it: on whichever side has more room left, level with the top of that window, and as
    /// wide as the room there allows — down to <see cref="SmallestBesideWidth"/> — with
    /// <paramref name="height"/> saying how tall it is so the answer can stay inside the desktop.
    /// Nothing when neither side has room, which is when standing over the window is the only place
    /// left for it. Both rectangles are in the same units: what the caller measures the desktop in.
    /// </summary>
    public static (double X, double Y, double Width)? Beside(
        (double X, double Y, double Width, double Height) window,
        (double X, double Y, double Width, double Height) desktop,
        double height)
    {
        var right = desktop.X + desktop.Width - (window.X + window.Width);
        var left = window.X - desktop.X;
        var room = Math.Max(right, left);

        if (room < SmallestBesideWidth)
        {
            return null;
        }

        // A tie goes to the right, which is where a form is expected to come up.
        var width = Math.Min(WantedWidth, room);
        var x = right >= left ? window.X + window.Width : window.X - width;

        // Level with the top of the window it stands beside, unless the desktop cannot hold it
        // there: then it moves up by as much as it has to and no further.
        var bottom = desktop.Y + desktop.Height - height;
        var y = Math.Clamp(window.Y, desktop.Y, Math.Max(desktop.Y, bottom));

        return (x, y, width);
    }

    /// <summary>The size wanted, no more than the room there is, and never less than the smallest.</summary>
    private static double Within(double wanted, double room, double smallest)
        => Math.Max(smallest, Math.Min(wanted, room - (EdgeRoom * 2)));
}
