using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Which of the places a search found a step means, and the words a log line reads a sighting in.
/// The order those places are counted in is <see cref="HitSort"/>'s business and the family that
/// found them hands them over already in it; everything here counts from one and from the top.
/// </summary>
internal static class SearchHits
{
    /// <summary>The place a step wants, counted from the top left, or null when there are not that many.</summary>
    public static ImageMatch? Choose(IRunContext run, IReadOnlyList<ImageMatch> hits,
        ExecutableStep step)
    {
        var index = run.Index(step, "matchIndex", 1);
        return index <= hits.Count ? hits[index - 1] : null;
    }

    /// <summary>Which of the places a step went with, counted from one, or zero when there are too few.</summary>
    public static int WentWith(IRunContext run, IReadOnlyList<ImageMatch> hits, ExecutableStep step)
    {
        var index = run.Index(step, "matchIndex", 1);
        return index <= hits.Count ? index : 0;
    }

    /// <summary>The thing a step wants, counted from the top left, or null when there are not that many.</summary>
    public static Sighting? Choose(IRunContext run, IReadOnlyList<Sighting> found,
        ExecutableStep step)
    {
        var index = run.Index(step, "matchIndex", 1);
        return index <= found.Count ? found[index - 1] : null;
    }

    /// <summary>Which of the hits a step went with, counted from one, or zero when there are too few.</summary>
    public static int WentWith(IRunContext run, IReadOnlyList<Sighting> found, ExecutableStep step)
    {
        var index = run.Index(step, "matchIndex", 1);
        return index <= found.Count ? index : 0;
    }

    /// <summary>One thing that was found, in the words a log line reads it in: person 640,360.</summary>
    public static string Describe(Sighting sighting)
        => sighting.Label.Length > 0
            ? $"{sighting.Label} {sighting.Match.Center.X},{sighting.Match.Center.Y}"
            : $"{sighting.Match.Center.X},{sighting.Match.Center.Y}";

    /// <summary>
    /// What a step was after, as the user wrote it: which model, and which classes of thing. It is
    /// what a step that found nothing says it did not find, and what the look hands over as what was
    /// being looked for.
    /// </summary>
    public static string Looking(IRunContext run, ExecutableStep step)
    {
        var model = Path.GetFileName(ModelOf(run, step));
        var labels = Labels(run.Args(step).Text("labels"));
        return labels.Count == 0 ? model : $"{model} {string.Join(',', labels)}";
    }

    /// <summary>The model file a step named, as the path it is read from.</summary>
    public static string ModelOf(IRunContext run, ExecutableStep step)
        => run.Devices.Files.Resolve(run.PathOf(step, "model"));

    /// <summary>The rows of a list a step wrote: one per line, or separated by semicolons.</summary>
    public static IReadOnlyList<string> Labels(string text)
        =>
        [
            .. text.Split([';', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ];

    /// <summary>
    /// How many hits a walk that counts them where it finds them has to collect: every one it can
    /// hold when the step keeps the whole list or counts from a corner the walk does not start in,
    /// and only as far as the hit it named otherwise.
    /// </summary>
    /// <remarks>
    /// A search that walks its own order answers the "how many" question itself — the picture and
    /// detection families ask the device for that number instead of this one. What is left here is
    /// the pixel walk, which costs one probe per pixel: a step that takes the first red pixel pays
    /// for the first red pixel and no more.
    /// </remarks>
    public static int Wanted(IRunContext run, ExecutableStep step)
    {
        if (run.Wants(step, "every"))
        {
            return RunLimits.Matches;
        }

        var kind = HitOrder.KindOf(step.Text("orderBy").Trim().ToLowerInvariant());
        return HitOrder.CountsFromTheFarSide(kind) || kind is MatchOrder.Near or MatchOrder.Random
            ? RunLimits.Matches
            : run.Index(step, "matchIndex", 1);
    }
}
