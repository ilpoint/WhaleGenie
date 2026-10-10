namespace WhaleGenie.Core.Devices;

/// <summary>
/// Looking for a colour in a picture. It needs no hardware, so it sits beside the device types
/// rather than behind a device: the engine hands it a frame it has already captured and gets back
/// the pixels that showed the colour.
/// </summary>
public static class PixelSearch
{
    /// <summary>
    /// At most <paramref name="limit"/> of the pixels within <paramref name="tolerancePercent"/> of
    /// <paramref name="colour"/>. The limit is what stops a flat background from filling memory
    /// with hits, and <paramref name="surest"/> decides which ones it keeps: the first ones read
    /// left to right and top to bottom, or the ones closest to the colour that was asked for.
    /// </summary>
    public static IReadOnlyList<ScreenPoint> Find(ImageFrame frame, PixelColor colour,
        double tolerancePercent, int limit, bool surest = false)
    {
        var found = new List<ScreenPoint>();
        if (frame.IsEmpty || limit <= 0)
        {
            return found;
        }

        if (surest)
        {
            return Nearest(frame, colour, tolerancePercent, limit);
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

    /// <summary>
    /// The pixels closest to the colour asked for, walking the whole area so that one further away
    /// that is a better match still wins. Equally close ones stay in the order they were read in,
    /// so the answer does not depend on which way round the area happened to be walked.
    /// </summary>
    private static List<ScreenPoint> Nearest(ImageFrame frame, PixelColor colour,
        double tolerancePercent, int limit)
    {
        var best = new List<(double Distance, ScreenPoint At)>();
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = frame[x, y];
                if (!pixel.Matches(colour, tolerancePercent))
                {
                    continue;
                }

                var distance = pixel.DistanceTo(colour);
                if (best.Count == limit && distance >= best[^1].Distance)
                {
                    continue;
                }

                var at = best.FindIndex(item => item.Distance > distance);
                if (at < 0)
                {
                    best.Add((distance, new ScreenPoint(x, y)));
                }
                else
                {
                    best.Insert(at, (distance, new ScreenPoint(x, y)));
                }

                if (best.Count > limit)
                {
                    best.RemoveAt(best.Count - 1);
                }
            }
        }

        return [.. best.Select(item => item.At)];
    }
}
