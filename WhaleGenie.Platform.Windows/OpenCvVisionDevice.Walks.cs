using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// Which places a picture was found at, walked the way the step counts them. The score map OpenCV
/// leaves behind says how alike each position is; how many of them are handed over and in which
/// order is the step's question rather than the search's, so each walk below answers one of them.
/// </summary>
/// <remarks>
/// A walk that starts at a corner reads the map once and blanks the patch it takes as it goes: a
/// reference picture the size of the area cannot overlap itself, so blanking a hit is enough to
/// move on to a different place, and one pass over the map is the whole cost.
/// </remarks>
public sealed partial class OpenCvVisionDevice
{
    /// <summary>The surest places first, wherever on the screen they are.</summary>
    private static List<ImageMatch> ByScore(Mat result, VisionQuery query, ScreenSize size)
    {
        var found = new List<ImageMatch>();
        var exact = query.Algorithm is MatchAlgorithm.Difference;
        while (found.Count < query.Limit)
        {
            Cv2.MinMaxLoc(result, out var low, out var high, out var lowAt, out var highAt);

            // A square difference is the distance between the pictures rather than how alike they
            // are: the smallest one is the best, and it is read out the other way round so that a
            // macro's confidence means "bigger is surer" whichever way was chosen.
            var where = exact ? lowAt : highAt;
            var score = ScoreOf(exact ? low : high, exact);
            if (score * 100 < query.ConfidencePercent)
            {
                break;
            }

            found.Add(new ImageMatch(score, new ScreenPoint(where.X, where.Y), size));
            Suppress(result, where, size, exact);
        }

        return found;
    }

    /// <summary>
    /// The places read from one corner of the area outwards, which is what "the third one down the
    /// screen" counts along. A hit blanks the patch it covers in the map being walked, so the same
    /// place cannot be read twice.
    /// </summary>
    private static List<ImageMatch> ByDirection(Mat result, VisionQuery query, ScreenSize size)
    {
        var exact = query.Algorithm is MatchAlgorithm.Difference;
        var (down, forward) = DirectionOf(query.Walk);
        var found = new List<ImageMatch>();
        var width = result.Cols;
        var height = result.Rows;
        var values = Borrow(result, width, height);
        try
        {
            for (var row = 0; row < height && found.Count < query.Limit; row++)
            {
                var y = down ? row : height - 1 - row;
                for (var column = 0; column < width && found.Count < query.Limit; column++)
                {
                    var x = forward ? column : width - 1 - column;
                    var score = ScoreOf(values[(y * width) + x], exact);
                    if (score * 100 < query.ConfidencePercent)
                    {
                        continue;
                    }

                    found.Add(new ImageMatch(score, new ScreenPoint(x, y), size));
                    Blank(values, width, height, x, y, size, exact);
                }
            }

            return found;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(values);
        }
    }

    /// <summary>
    /// The places nearest the point the step named. What "near" has to look at is every place the
    /// picture was found, not the surest few of them, so the map is read once for its peaks and the
    /// nearest of those are taken. A peak within a hit of one already taken is the same thing seen
    /// twice and is left out.
    /// </summary>
    private static List<ImageMatch> ByNearest(Mat result, VisionQuery query, ScreenSize size)
    {
        var exact = query.Algorithm is MatchAlgorithm.Difference;
        var width = result.Cols;
        var height = result.Rows;
        var values = Borrow(result, width, height);
        try
        {
            var peaks = new List<(ImageMatch Match, long Away)>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var score = ScoreOf(values[(y * width) + x], exact);
                    if (score * 100 < query.ConfidencePercent
                        || !IsPeak(values, width, height, x, y, exact, score))
                    {
                        continue;
                    }

                    var match = new ImageMatch(score, new ScreenPoint(x, y), size);
                    peaks.Add((match, Away(match.Center, query.Near)));
                }
            }

            peaks.Sort((left, right) => left.Away != right.Away
                ? left.Away.CompareTo(right.Away)
                : Reading(left.Match.Location, right.Match.Location));

            var found = new List<ImageMatch>();
            foreach (var peak in peaks)
            {
                if (found.Count >= query.Limit)
                {
                    break;
                }

                if (found.Any(taken => Overlaps(taken, peak.Match)))
                {
                    continue;
                }

                found.Add(peak.Match);
            }

            return found;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(values);
        }
    }

    /// <summary>Which way round a walk reads the map: down the rows or up them, and across or back.</summary>
    private static (bool Down, bool Forward) DirectionOf(SearchWalk walk) => walk switch
    {
        SearchWalk.ReadingUp => (false, true),
        SearchWalk.ReadingBack => (true, false),
        SearchWalk.ReadingBackUp => (false, false),
        _ => (true, true),
    };

    /// <summary>
    /// The score a place in the map stands for, whichever way the pictures were compared: always
    /// "bigger is surer", so a threshold means the same thing across the algorithms.
    /// </summary>
    private static double ScoreOf(double value, bool exact) => exact ? 1 - value : Math.Clamp(value, -1, 1);

    /// <summary>The map copied out once, so a walk over every place in it is a walk over an array.</summary>
    private static float[] Borrow(Mat result, int width, int height)
    {
        var values = ArrayPool<float>.Shared.Rent(width * height);
        Marshal.Copy(result.Data, values, 0, width * height);
        return values;
    }

    /// <summary>
    /// Marks the patch one hit covers as taken, so the walk that is reading the map does not read
    /// it again. The value written is outside what the comparing can produce, which is what keeps a
    /// square difference from reading its own blanked patch as the best place in the area.
    /// </summary>
    private static void Blank(float[] values, int width, int height, int x, int y, ScreenSize size,
        bool exact)
    {
        var worst = exact ? float.MaxValue : float.MinValue;
        var right = Math.Clamp(x + size.Width, x + 1, width);
        var bottom = Math.Clamp(y + size.Height, y + 1, height);
        for (var row = y; row < bottom; row++)
        {
            for (var column = Math.Clamp(x, 0, width - 1); column < right; column++)
            {
                values[(row * width) + column] = worst;
            }
        }
    }

    /// <summary>
    /// True when this place is the best of its neighbours, which is one place per thing found rather
    /// than a slope of places leading up to it. A flat top counts once: the place an equal neighbour
    /// has already been read from is not a second peak.
    /// </summary>
    private static bool IsPeak(float[] values, int width, int height, int x, int y, bool exact,
        double score)
    {
        for (var down = -1; down <= 1; down++)
        {
            for (var across = -1; across <= 1; across++)
            {
                if (down == 0 && across == 0)
                {
                    continue;
                }

                var otherX = x + across;
                var otherY = y + down;
                if (otherX < 0 || otherY < 0 || otherX >= width || otherY >= height)
                {
                    continue;
                }

                var around = ScoreOf(values[(otherY * width) + otherX], exact);
                if (around > score
                    || (around == score && (down < 0 || (down == 0 && across < 0))))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>How far a place is from a point, squared so no root is worked out per place.</summary>
    private static long Away(ScreenPoint place, ScreenPoint point)
    {
        var across = (long)place.X - point.X;
        var down = (long)place.Y - point.Y;
        return (across * across) + (down * down);
    }

    /// <summary>Two places of the same size within a hit of each other are one thing seen twice.</summary>
    private static bool Overlaps(ImageMatch taken, ImageMatch candidate)
        => Math.Abs(taken.Location.X - candidate.Location.X) < taken.Size.Width
           && Math.Abs(taken.Location.Y - candidate.Location.Y) < taken.Size.Height;

    /// <summary>Down the screen first and then across, which the ties of a distance keep to.</summary>
    private static int Reading(ScreenPoint left, ScreenPoint right)
        => left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.X.CompareTo(right.X);
}
