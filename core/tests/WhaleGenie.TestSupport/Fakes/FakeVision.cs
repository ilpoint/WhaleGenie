using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.TestSupport;

/// <summary>
/// The pretend vision: what a reference picture is read as, and where it turns up on the screen.
/// </summary>
internal sealed class FakeVision(FakeDesk desk) : IVisionDevice
{
    /// <summary>Where the reference picture is reported, for a check that needs one answer.</summary>
    public ImageMatch? Match { get; set; }

    /// <summary>
    /// The places the fake reports the reference picture at. Normally the one <see cref="Match"/>
    /// sets up; a check that wants several fills this in instead.
    /// </summary>
    public List<ImageMatch> Matches { get; } = [];

    /// <summary>What a picture read from a path the fake was not told about comes back as.</summary>
    public ImageFrame? Loaded { get; set; } = new(2, 2, new byte[16]);

    /// <summary>How many searches happen before a match starts being returned.</summary>
    public int MatchAfter { get; set; } = 1;

    /// <summary>How many times the reference picture was looked for.</summary>
    public int Searches { get; private set; }

    /// <summary>
    /// What each reference picture is read as, by path, for the checks that need two of them to
    /// stand for different things. A path that is not in here is read as <see cref="Loaded"/>.
    /// </summary>
    public Dictionary<string, ImageFrame> Pictures { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the screen answers for one reference picture, when they have to differ.</summary>
    public Func<ImageFrame, VisionQuery, IReadOnlyList<ImageMatch>>? PictureAnswers { get; set; }

    /// <summary>Every search asked for, so a check can see how the step was read.</summary>
    public List<VisionQuery> Queries { get; } = [];

    public ImageFrame? Load(string path)
    {
        desk.Note($"load {path}");
        return Pictures.TryGetValue(path, out var picture) ? picture : Loaded;
    }

    public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle, VisionQuery query)
    {
        Searches++;
        Queries.Add(query);
        desk.Note($"findAll {needle.Width}x{needle.Height} {query.ConfidencePercent:0} {query.Algorithm}");

        if (Searches < MatchAfter)
        {
            return [];
        }

        // A check that needs one reference picture to turn up and another not to says so here; the
        // rest of the time every picture is answered the way the one <see cref="Match"/> is.
        var hits = PictureAnswers?.Invoke(needle, query)
            ?? (Matches.Count > 0 ? Matches : Match is null ? [] : [Match]);
        return [.. Walk([.. hits], query).Take(query.Limit)];
    }

    /// <summary>
    /// The places in the order the query asked for them, which the real device decides for itself:
    /// the answers a check put here are handed over as they stand unless the step counts them in
    /// another direction or from another point, in which case the counted ones come first. It is
    /// the same rule the caller applies when it puts several areas together, so a check cannot be
    /// answered by a fake that orders differently from the search it stands for.
    /// </summary>
    private static IEnumerable<ImageMatch> Walk(List<ImageMatch> hits, VisionQuery query)
    {
        if (query.Walk is SearchWalk.Near)
        {
            return [.. hits.OrderBy(hit => Away(hit.Center, query.Near))
                .ThenBy(hit => hit.Location.Y)
                .ThenBy(hit => hit.Location.X)];
        }

        return query.Walk switch
        {
            SearchWalk.Reading => [.. hits.OrderBy(hit => hit.Location.Y).ThenBy(hit => hit.Location.X)],
            SearchWalk.ReadingBack => [.. hits.OrderBy(hit => hit.Location.Y)
                .ThenByDescending(hit => hit.Location.X)],
            SearchWalk.ReadingUp => [.. hits.OrderByDescending(hit => hit.Location.Y)
                .ThenBy(hit => hit.Location.X)],
            SearchWalk.ReadingBackUp => [.. hits.OrderByDescending(hit => hit.Location.Y)
                .ThenByDescending(hit => hit.Location.X)],
            _ => hits,
        };
    }

    /// <summary>How far a place is from a point, squared so no root has to be worked out.</summary>
    private static long Away(ScreenPoint place, ScreenPoint point)
    {
        var across = (long)place.X - point.X;
        var down = (long)place.Y - point.Y;
        return (across * across) + (down * down);
    }
}
