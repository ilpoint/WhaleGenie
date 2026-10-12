namespace WhaleGenie.Core.Devices;

/// <summary>
/// One colour, and how far a pixel may sit from it and still count as it. Both numbers are worked
/// out once for a whole picture, because a walk over a picture asks the same question of every
/// pixel in it.
/// </summary>
/// <remarks>
/// How near a colour is, is the distance <see cref="PixelColor.DistanceTo"/> measures: the three
/// channels counted equally and the whole thing scaled so that 0 is the same colour and 100 is as
/// far apart as two colours get. Comparing a distance against a limit does not need the distance
/// itself — the sum of the squared channel differences against the limit squared is the same
/// question — and it is the square root, asked once per pixel, that made walking a picture dear.
/// The rule lives here alone so that a colour compared by a device and a colour compared pixel by
/// pixel cannot drift apart.
/// </remarks>
internal readonly struct ColourReach
{
    /// <summary>How far one channel may be from the colour and still be worth checking.</summary>
    private readonly int _box;

    /// <summary>The squared differences of the three channels that still add up to "near enough".</summary>
    private readonly int _owed;

    private readonly byte _blue;
    private readonly byte _green;
    private readonly byte _red;

    public ColourReach(PixelColor colour, double tolerancePercent)
    {
        _blue = colour.B;
        _green = colour.G;
        _red = colour.R;

        var reach = Math.Max(0, tolerancePercent) / 100 * 255;

        // The widest a single channel may sit away is the square root of three times the reach:
        // further than that and its square alone is over what the three may add up to, whatever
        // the other two are. Beyond a reach of 255 the whole of a channel's range is included
        // anyway, which is also what stops the multiplication below from overflowing.
        _box = reach >= 255 / Math.Sqrt(3) ? 255 : (int)(Math.Sqrt(3) * reach);

        // Rounded down rather than to nearest because the sum of three squares is a whole number:
        // below a limit is the same as at or below the whole number under it.
        _owed = (int)Math.Floor(3 * reach * reach);
    }

    /// <summary>How near a pixel of a picture is, read at the offset its four channels start at.</summary>
    public bool Near(ReadOnlySpan<byte> pixels, int at)
    {
        var blue = pixels[at] - _blue;
        if (blue < 0)
        {
            blue = -blue;
        }

        if (blue > _box)
        {
            return false;
        }

        var green = pixels[at + 1] - _green;
        if (green < 0)
        {
            green = -green;
        }

        if (green > _box)
        {
            return false;
        }

        var red = pixels[at + 2] - _red;
        if (red < 0)
        {
            red = -red;
        }

        return red <= _box && blue * blue + green * green + red * red <= _owed;
    }

    /// <summary>Whether a colour read one at a time is near enough, the same question as above.</summary>
    public bool Near(PixelColor other)
    {
        var blue = other.B - _blue;
        var green = other.G - _green;
        var red = other.R - _red;
        return blue * blue + green * green + red * red <= _owed;
    }

    /// <summary>
    /// How far a pixel of a picture sits from the colour: the sum of the three channels' squared
    /// differences, which sorts the same way their distance does and needs no square root of its
    /// own. Only asked of pixels that are already near enough, which is what keeps it off the walk.
    /// </summary>
    public int Apart(ReadOnlySpan<byte> pixels, int at)
    {
        var blue = pixels[at] - _blue;
        var green = pixels[at + 1] - _green;
        var red = pixels[at + 2] - _red;
        return (blue * blue) + (green * green) + (red * red);
    }
}
