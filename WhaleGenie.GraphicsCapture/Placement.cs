namespace WhaleGenie.GraphicsCapture;

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
        for (var row = top; row < bottom; row++)
        {
            var from = (((long)(row - sourceY) * source.Width) + (left - sourceX)) * 4;
            var to = (((long)(row - y) * width) + (left - x)) * 4;
            Array.Copy(source.Bgra, from, pixels, to, (right - left) * 4);
        }

        return new ScreenGrab(width, height, pixels, source.Used);
    }
}
