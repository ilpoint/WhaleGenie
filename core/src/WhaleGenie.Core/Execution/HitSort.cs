using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Putting one look's hits in the order the step asked to count them in. Every family that finds
/// something hands its candidates here, so "the third one" means one thing whether the candidates
/// are picture matches, boxes a model drew or words that were read.
/// </summary>
/// <remarks>
/// The orders that are not a direction are ranked over the whole set rather than over the first
/// few: a weighted score or a distance can put any of them first, which is why the number the
/// search was asked for never decides the answer on its own. Equal ranks fall back to reading
/// order, so the same screen counted twice counts the same way.
/// </remarks>
internal static class HitSort
{
    /// <summary>What one hit is ranked by, whichever family found it.</summary>
    private readonly record struct Rank(double Score, ScreenPoint Location, ScreenSize Size)
    {
        public ScreenPoint Centre => ScreenMath.Middle(Location, Size);

        public long Area => (long)Size.Width * Size.Height;
    }

    /// <summary>The places a picture search found, in the order the step counts them in.</summary>
    public static void Sort(List<ImageMatch> hits, HitOrder order, Random? random = null)
        => Arrange(hits, order, hit => new Rank(hit.Score, hit.Location, hit.Size), random);

    /// <summary>The things a model found, in the order the step counts them in.</summary>
    public static void Sort(List<Sighting> found, HitOrder order, Random? random = null)
        => Arrange(found, order, hit => new Rank(hit.Match.Score, hit.Match.Location,
            hit.Match.Size), random);

    /// <summary>
    /// What was read off the screen, in the order the step counts it in. A reading is ranked like
    /// any other hit; the model already hands the lines over in the order they were read, which is
    /// why the families that read writing offer no direction of their own.
    /// </summary>
    public static void Sort(List<TextSpan> spans, HitOrder order, Random? random = null)
        => Arrange(spans, order, span => new Rank(span.Confidence, span.Location, span.Size),
            random);

    /// <summary>One list put in order, ranked through the projection that fits its hit kind.</summary>
    private static void Arrange<T>(List<T> items, HitOrder order, Func<T, Rank> rank,
        Random? random)
    {
        if (items.Count < 2)
        {
            return;
        }

        if (order.Kind is MatchOrder.Random)
        {
            Shuffle(items, random);
            return;
        }

        // Only the weighted score needs the set's own extremes: both halves of it are made to reach
        // one at the best the set has, so likeness and size can be added together at all.
        var weighted = order.Kind is MatchOrder.Combined;
        var maxScore = weighted ? items.Max(item => rank(item).Score) : 0;
        var maxArea = weighted ? items.Max(item => rank(item).Area) : 0;

        items.Sort((left, right) => Compare(order, rank(left), rank(right), maxScore, maxArea));
    }

    /// <summary>
    /// The two hits in order. A tie is settled by reading order rather than left to whichever the
    /// sorting happened to see first, so a lookup that finds the same places again numbers them the
    /// same way.
    /// </summary>
    private static int Compare(HitOrder order, Rank left, Rank right, double maxScore,
        long maxArea)
    {
        var decided = order.Kind switch
        {
            MatchOrder.ReadingBack => ReadingBack(left, right),
            MatchOrder.ReadingUp => ReadingUp(left, right),
            MatchOrder.ReadingBackUp => ReadingBackUp(left, right),
            MatchOrder.Combined => Weighted(order, right, maxScore, maxArea)
                .CompareTo(Weighted(order, left, maxScore, maxArea)),
            MatchOrder.Near => Away(order.Near, left).CompareTo(Away(order.Near, right)),
            MatchOrder.Score => right.Score.CompareTo(left.Score),
            MatchOrder.Area => right.Area.CompareTo(left.Area),
            _ => Reading(left, right),
        };

        return decided != 0 ? decided : Reading(left, right);
    }

    /// <summary>Down the screen first and then across, the order a person counts them in.</summary>
    private static int Reading(Rank left, Rank right)
        => left.Location.Y != right.Location.Y
            ? left.Location.Y.CompareTo(right.Location.Y)
            : left.Location.X.CompareTo(right.Location.X);

    private static int ReadingBack(Rank left, Rank right)
        => left.Location.Y != right.Location.Y
            ? left.Location.Y.CompareTo(right.Location.Y)
            : right.Location.X.CompareTo(left.Location.X);

    private static int ReadingUp(Rank left, Rank right)
        => left.Location.Y != right.Location.Y
            ? right.Location.Y.CompareTo(left.Location.Y)
            : left.Location.X.CompareTo(right.Location.X);

    private static int ReadingBackUp(Rank left, Rank right)
        => left.Location.Y != right.Location.Y
            ? right.Location.Y.CompareTo(left.Location.Y)
            : right.Location.X.CompareTo(left.Location.X);

    /// <summary>
    /// The likeness and the size added together, each measured against the best this set has: a
    /// share of 100 is likeness alone, a share of 0 is size alone.
    /// </summary>
    private static double Weighted(HitOrder order, Rank hit, double maxScore, long maxArea)
    {
        var share = order.ScoreWeight / 100.0;
        var score = maxScore > 0 ? Math.Clamp(hit.Score, 0, maxScore) / maxScore : 0;
        var area = maxArea > 0 ? (double)hit.Area / maxArea : 0;
        return (share * score) + ((1 - share) * area);
    }

    /// <summary>How far a hit's middle is from a point, squared so no root is worked out per pair.</summary>
    private static long Away(ScreenPoint point, Rank hit)
    {
        var centre = hit.Centre;
        var across = (long)centre.X - point.X;
        var down = (long)centre.Y - point.Y;
        return (across * across) + (down * down);
    }

    /// <summary>Shuffles in place, so a macro that takes the first of several does not always take the same one.</summary>
    private static void Shuffle<T>(IList<T> items, Random? random)
    {
        var dice = random ?? Random.Shared;
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = dice.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }
}
