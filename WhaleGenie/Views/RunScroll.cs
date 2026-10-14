using System;

namespace WhaleGenie.Views;

/// <summary>
/// Where the steps list is scrolled to when the run moves on to another row. Kept apart from the
/// window for the same reason as <see cref="LookGeometry"/>: the answer is what is worth checking,
/// and a list that scrolls to the row only when the row is off the top, or that leaves the row
/// against the edge where the row after it cannot be read, is wrong in a way no assertion about
/// "it scrolled" would catch.
/// </summary>
internal static class RunScroll
{
    /// <summary>
    /// The offset that puts one row in view with a row of room above and below it. While the row is
    /// already that far inside, nothing moves: a list that jumped on every step would be harder to
    /// read than one that never moved. Where the room is too small for that much slack, the row is
    /// put as far in as it can be, which is the best a short list can do.
    /// </summary>
    /// <param name="offset">Where the list is scrolled to now.</param>
    /// <param name="rowTop">Where the row starts, measured down the whole list.</param>
    /// <param name="rowHeight">How tall the row is.</param>
    /// <param name="room">How much of the list fits in the space it is shown in.</param>
    public static double Where(double offset, double rowTop, double rowHeight, double room)
    {
        if (room <= 0 || rowHeight <= 0)
        {
            return Math.Max(0, offset);
        }

        // A row of room on each side, or as much of that as fits around the row.
        var slack = Math.Min(rowHeight, Math.Max(0, room - rowHeight) / 2);

        if (rowTop - offset < slack)
        {
            return Math.Max(0, rowTop - slack);
        }

        return rowTop + rowHeight + slack - room > offset
            ? Math.Max(0, rowTop + rowHeight + slack - room)
            : Math.Max(0, offset);
    }
}
