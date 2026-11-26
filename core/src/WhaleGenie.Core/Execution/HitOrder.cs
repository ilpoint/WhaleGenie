using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// How one step asked its hits to be counted, with the point a distance is measured from already
/// worked out: which order, where "nearest" means nearest to, and how much of the composite score
/// is likeness rather than size.
/// </summary>
/// <remarks>
/// It is read once per look rather than once per hit, because the point may be a variable the
/// macro changes between looks: a wait that keeps looking measures each new sweep against the
/// value in play at that moment.
/// </remarks>
internal readonly record struct HitOrder(MatchOrder Kind, ScreenPoint Near, int ScoreWeight)
{
    /// <summary>What the likeness share of a composite score is when the step says nothing.</summary>
    public const int UsualScoreWeight = 50;

    /// <summary>
    /// The order a step asked for, as it stands right now. A step that named no point of its own —
    /// "nearest the middle of what I searched" — is measured from the middle of the areas it
    /// searched, which is the same middle the look draws a composite picture around.
    /// </summary>
    public static HitOrder Of(StepArguments args, Func<ScreenPoint?> corner,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas)
    {
        var written = args.Text("orderBy").Trim().ToLowerInvariant();
        var kind = KindOf(written);

        // The step's own corner is asked for only by the order that needs it: looking a window up
        // again for a step that counts from a corner would be a second enumeration of the desktop
        // per look, and a window that went away between the two would fail a step that was fine.
        var near = kind is MatchOrder.Near
            ? written == Centre ? Middle(areas)
                : ScreenMath.Placed(corner(), args.Number("nearX"), args.Number("nearY"))
            : default;

        // A field nobody wrote is the usual share rather than zero: zero is "size only", which a
        // step has to say out loud. The catalogue's empty-means would answer the same for a field
        // it declares, but the finding families that offer no weight at all read no field here.
        var weight = args.Given("scoreWeight") ? args.Number("scoreWeight") : UsualScoreWeight;
        return new HitOrder(kind, near, Math.Clamp(weight, 0, 100));
    }

    /// <summary>What the search itself is asked to walk, which is only the directional answers.</summary>
    public SearchWalk Walk => Kind switch
    {
        MatchOrder.Reading => SearchWalk.Reading,
        MatchOrder.ReadingBack => SearchWalk.ReadingBack,
        MatchOrder.ReadingUp => SearchWalk.ReadingUp,
        MatchOrder.ReadingBackUp => SearchWalk.ReadingBackUp,
        MatchOrder.Near => SearchWalk.Near,
        _ => SearchWalk.Score,
    };

    /// <summary>
    /// The order a word stands for. A word the catalogue allows but this table does not know is
    /// read as reading order, the way every other fallback-word field is: which words are legal is
    /// the catalogue's answer, and a step that wrote its own is not refused here a second time.
    /// </summary>
    internal static MatchOrder KindOf(string written) => written switch
    {
        "readingback" => MatchOrder.ReadingBack,
        "readingup" => MatchOrder.ReadingUp,
        "readingbackup" => MatchOrder.ReadingBackUp,
        "combined" => MatchOrder.Combined,
        "near" => MatchOrder.Near,
        Centre => MatchOrder.Near,
        "score" => MatchOrder.Score,
        "area" => MatchOrder.Area,
        "random" => MatchOrder.Random,
        _ => MatchOrder.Reading,
    };

    /// <summary>True for the orders that can only be answered by walking the area themselves.</summary>
    internal static bool CountsFromTheFarSide(MatchOrder kind)
        => kind is MatchOrder.ReadingUp or MatchOrder.ReadingBack or MatchOrder.ReadingBackUp;

    /// <summary>The word a step writes for "nearest the middle of what I searched".</summary>
    private const string Centre = "centre";

    /// <summary>How far from the top left corner of the last of the areas the whole of them reaches.</summary>
    private static ScreenPoint Middle(IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas)
    {
        if (areas.Count == 0)
        {
            return default;
        }

        var left = areas.Min(area => area.Origin.X);
        var top = areas.Min(area => area.Origin.Y);
        var right = areas.Max(area => area.Origin.X + area.Frame.Width);
        var bottom = areas.Max(area => area.Origin.Y + area.Frame.Height);
        return new ScreenPoint(left + ((right - left) / 2), top + ((bottom - top) / 2));
    }
}
