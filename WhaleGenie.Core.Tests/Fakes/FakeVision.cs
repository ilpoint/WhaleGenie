using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

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

        // A check that needs one reference picture to turn up and another not to says so here; the
        // rest of the time every picture is answered the way the one <see cref="Match"/> is.
        var hits = PictureAnswers?.Invoke(needle, query)
            ?? (Matches.Count > 0 ? Matches : Match is null ? [] : [Match]);
        return Searches >= MatchAfter ? [.. hits.Take(query.Limit)] : [];
    }
}
