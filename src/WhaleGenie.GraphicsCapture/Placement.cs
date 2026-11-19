namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// Which part of a rectangle of the desktop comes from which display. A rectangle that sits on one
/// display is one piece; one that straddles two is two, and the corner where they meet is followed
/// exactly rather than rounded to whichever display holds the middle.
/// </summary>
internal static class ScreenPlacement
{
    /// <summary>
    /// One display's share of the rectangle that was asked for: which part of the display to copy —
    /// counted from the display's own corner — and where that part lands in the picture that comes
    /// back, counted from the corner of the rectangle that was asked for.
    /// </summary>
    public readonly record struct Piece(ScreenArea Display, int SourceX, int SourceY,
        int ToX, int ToY, int Width, int Height);

    /// <summary>
    /// The displays the rectangle is made of, in the order the layout lists them. A rectangle no
    /// display holds is refused: there is no picture of a place that is not part of the desktop,
    /// which is a mistake in the asking rather than a picture of nothing.
    /// </summary>
    public static IReadOnlyList<Piece> Pieces(ScreenLayout layout, int x, int y, int width,
        int height)
        => Pieces([.. layout.Displays.Select(display => display.Bounds)], x, y, width, height);

    /// <summary>The same, for a way of reading the screen that knows its displays by rectangle only.</summary>
    public static IReadOnlyList<Piece> Pieces(IReadOnlyList<ScreenArea> displays, int x, int y,
        int width, int height)
    {
        var pieces = new List<Piece>();
        foreach (var display in displays)
        {
            var left = Math.Max(display.Left, x);
            var top = Math.Max(display.Top, y);
            var right = Math.Min(display.Right, x + width);
            var bottom = Math.Min(display.Bottom, y + height);
            if (right <= left || bottom <= top)
            {
                continue;
            }

            pieces.Add(new Piece(display, left - display.Left, top - display.Top,
                left - x, top - y, right - left, bottom - top));
        }

        if (pieces.Count == 0)
        {
            throw new EmptyRegionException($"{x},{y} {width}x{height}");
        }

        return pieces;
    }
}

/// <summary>
/// Putting a picture of a larger thing into the rectangle that was asked for: what the source has
/// of that rectangle is copied in where it belongs, and the rest is left black. A rectangle the
/// source has nothing in is refused rather than answered with an empty picture, which is a mistake
/// in the asking rather than a picture of nothing.
/// </summary>
internal static class Placement
{
    /// <summary>
    /// The rectangle <paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="width"/>×<paramref name="height"/>, filled in from a picture of something
    /// larger whose top left corner sits at <paramref name="sourceX"/>, <paramref name="sourceY"/>.
    /// </summary>
    public static ScreenGrab Of(ScreenGrab source, int sourceX, int sourceY,
        int x, int y, int width, int height)
    {
        var left = Math.Max(sourceX, x);
        var top = Math.Max(sourceY, y);
        var right = Math.Min(sourceX + source.Width, x + width);
        var bottom = Math.Min(sourceY + source.Height, y + height);
        if (right <= left || bottom <= top)
        {
            throw new EmptyRegionException($"{x},{y} {width}x{height}");
        }

        var pixels = new byte[(long)width * height * 4];
        Blit(source, left - sourceX, top - sourceY, pixels, width, left - x, top - y,
            right - left, bottom - top);
        return new ScreenGrab(width, height, pixels, source.Used);
    }

    /// <summary>
    /// One block of a picture copied into another at its own place. A rectangle of the desktop made
    /// of two displays is put together with one of these per display: what the first leaves black is
    /// beside what the second writes rather than under it, because each display is copied to the
    /// part of the picture it answers for.
    /// </summary>
    public static void Blit(ScreenGrab source, int sourceX, int sourceY, byte[] destination,
        int destinationStride, int x, int y, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var from = (((long)(sourceY + row) * source.Width) + sourceX) * 4;
            var to = (((long)(y + row) * destinationStride) + x) * 4;
            Array.Copy(source.Bgra, from, destination, to, (long)width * 4);
        }
    }
}
