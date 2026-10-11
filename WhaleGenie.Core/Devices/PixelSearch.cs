namespace WhaleGenie.Core.Devices;

/// <summary>
/// One colour a pattern asks for and where it has to be: the offset is counted from the pixel the
/// pattern's own colour was found at, so the whole thing is found wherever on the screen it sits.
/// </summary>
public readonly record struct ColourStep(int Dx, int Dy, PixelColor Colour);

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
    /// How many pixels of a picture are within <paramref name="tolerancePercent"/> of
    /// <paramref name="colour"/>. The whole picture is walked rather than stopping at the first
    /// one, because what this answers is "how much of it" rather than "where is it".
    /// </summary>
    public static int Count(ImageFrame frame, PixelColor colour, double tolerancePercent)
    {
        if (frame.IsEmpty)
        {
            return 0;
        }

        var matched = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame[x, y].Matches(colour, tolerancePercent))
                {
                    matched++;
                }
            }
        }

        return matched;
    }

    /// <summary>
    /// At most <paramref name="limit"/> of the pixels that show <paramref name="baseColour"/> with
    /// every one of <paramref name="around"/> showing its own colour where it was asked for, read
    /// left to right and top to bottom. One colour says "something red is here", which a busy screen
    /// is full of; the colours around it say which red thing this is. A pixel whose pattern would
    /// reach outside the picture is not a hit: half a pattern on the edge of an area is not the
    /// thing that was asked for.
    /// </summary>
    public static IReadOnlyList<ScreenPoint> FindPattern(ImageFrame frame, PixelColor baseColour,
        double tolerancePercent, IReadOnlyList<ColourStep> around, int limit)
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
                if (!frame[x, y].Matches(baseColour, tolerancePercent)
                    || !Surrounded(frame, x, y, around, tolerancePercent))
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

    /// <summary>Whether every colour of a pattern sits where it was asked for, counted from one pixel.</summary>
    private static bool Surrounded(ImageFrame frame, int x, int y,
        IReadOnlyList<ColourStep> around, double tolerancePercent)
    {
        foreach (var step in around)
        {
            var at = new ScreenPoint(x + step.Dx, y + step.Dy);
            if (at.X < 0 || at.Y < 0 || at.X >= frame.Width || at.Y >= frame.Height
                || !frame[at.X, at.Y].Matches(step.Colour, tolerancePercent))
            {
                return false;
            }
        }

        return true;
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
