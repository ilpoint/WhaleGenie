namespace Viktor.Core.Devices;

/// <summary>
/// Looking for a colour in a picture. It needs no hardware, so it sits beside the device types
/// rather than behind a device: the engine hands it a frame it has already captured and gets back
/// the pixels that showed the colour.
/// </summary>
public static class PixelSearch
{
    /// <summary>
    /// Every pixel within <paramref name="tolerancePercent"/> of <paramref name="colour"/>, read
    /// left to right and top to bottom, at most <paramref name="limit"/> of them. The limit is what
    /// stops a flat background from filling memory with hits.
    /// </summary>
    public static IReadOnlyList<ScreenPoint> Find(ImageFrame frame, PixelColor colour,
        double tolerancePercent, int limit)
    {
        var found = new List<ScreenPoint>();
        if (frame.IsEmpty || limit <= 0)
        {
            return found;
        }

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (!frame[x, y].Matches(colour, tolerancePercent))
                {
                    continue;
                }

                found.Add(new ScreenPoint(x, y));
                if (found.Count >= limit)
                {
                    return found;
                }
            }
        }

        return found;
    }
}
