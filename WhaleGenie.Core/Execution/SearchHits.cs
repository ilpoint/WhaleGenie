using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What a search found and how a step chooses among it: the order hits are counted in, which one
/// "the third" means, and the words a log line reads a sighting in. A detection model's findings go
/// through here, and so does everything else a step counts, so "third from the top" means the same
/// thing whichever kind of search produced the hits.
/// </summary>
internal static class SearchHits
{
    /// <summary>
    /// What order a step asked its hits to be counted in, as written in its field. Reading order is
    /// how a person counts them; by score is for a macro that wants the surest one wherever it is on
    /// the screen; by area is for one that wants the whole of something rather than a piece of it;
    /// shuffled is for one that must not always take the same one.
    /// </summary>
    public static MatchOrder OrderOf(ExecutableStep step) => step.Text("orderBy").Trim()
        .ToLowerInvariant() switch
    {
        "score" => MatchOrder.Score,
        "area" => MatchOrder.Area,
        "random" => MatchOrder.Random,
        "readingup" => MatchOrder.ReadingUp,
        "readingback" => MatchOrder.ReadingBack,
        "readingbackup" => MatchOrder.ReadingBackUp,
        _ => MatchOrder.Reading,
    };

    /// <summary>The things a detection model found, in the order the step counts them in.</summary>
    public static void Sort(List<Sighting> found, ExecutableStep step)
    {
        switch (OrderOf(step))
        {
            case MatchOrder.Score:
                found.Sort((left, right) => right.Match.Score.CompareTo(left.Match.Score));
                break;

            case MatchOrder.Area:
                found.Sort((left, right) => Box(right.Match).CompareTo(Box(left.Match)));
                break;

            case MatchOrder.Random:
                Shuffle(found);
                break;

            default:
                found.Sort((left, right) => Reading(left.Match, right.Match));
                break;
        }
    }

    /// <summary>The places a picture search found, in the order the step counts them in.</summary>
    public static void Sort(List<ImageMatch> hits, ExecutableStep step)
    {
        switch (OrderOf(step))
        {
            case MatchOrder.Score:
                hits.Sort((left, right) => right.Score.CompareTo(left.Score));
                break;

            case MatchOrder.Area:
                hits.Sort((left, right) => Box(right).CompareTo(Box(left)));
                break;

            case MatchOrder.Random:
                Shuffle(hits);
                break;

            // The other three readings are the same two questions asked the other way round: which
            // corner to start from decides which of several identical things counts as the first.
            case MatchOrder.ReadingUp:
                hits.Sort((left, right) => left.Location.Y != right.Location.Y
                    ? right.Location.Y.CompareTo(left.Location.Y)
                    : left.Location.X.CompareTo(right.Location.X));
                break;

            case MatchOrder.ReadingBack:
                hits.Sort((left, right) => left.Location.Y != right.Location.Y
                    ? left.Location.Y.CompareTo(right.Location.Y)
                    : right.Location.X.CompareTo(left.Location.X));
                break;

            case MatchOrder.ReadingBackUp:
                hits.Sort((left, right) => left.Location.Y != right.Location.Y
                    ? right.Location.Y.CompareTo(left.Location.Y)
                    : right.Location.X.CompareTo(left.Location.X));
                break;

            default:
                hits.Sort(Reading);
                break;
        }
    }

    /// <summary>
    /// The same for what was read off the screen: a reading of writing is a hit as well. Reading
    /// order is left alone — the reading model hands the lines over in the order they appear, and
    /// re-sorting them by position would break up a line whose words sit at slightly different
    /// heights.
    /// </summary>
    public static void Sort(List<TextSpan> spans, ExecutableStep step)
    {
        switch (OrderOf(step))
        {
            case MatchOrder.Score:
                spans.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));
                break;

            case MatchOrder.Area:
                spans.Sort((left, right) => Box(right.Size).CompareTo(Box(left.Size)));
                break;

            case MatchOrder.Random:
                Shuffle(spans);
                break;
        }
    }

    /// <summary>
    /// How many hits a search has to collect: all it can hold when the step keeps the whole list,
    /// and only as far as the hit it named otherwise. Asking for the list is what makes collecting
    /// it worth anything, and one step hands that over by ticking the box rather than by a switch
    /// of its own.
    /// </summary>
    public static int Wanted(IRunContext run, ExecutableStep step)
    {
        if (run.Wants(step, "every") || CountsFromTheFarSide(step))
        {
            return RunLimits.Matches;
        }

        return run.Index(step, "matchIndex", 1);
    }

    /// <summary>
    /// True while the step counts its hits from a corner other than the top left. "The third one"
    /// then means the third from that corner, which cannot be known until every hit has been read:
    /// stopping at three would count them from the wrong end and pick the wrong one.
    /// </summary>
    private static bool CountsFromTheFarSide(ExecutableStep step) => OrderOf(step) is
        MatchOrder.ReadingUp or MatchOrder.ReadingBack or MatchOrder.ReadingBackUp;

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

    /// <summary>Reading order: down the screen first, then across.</summary>
    private static int Reading(ImageMatch left, ImageMatch right)
        => left.Location.Y != right.Location.Y
            ? left.Location.Y.CompareTo(right.Location.Y)
            : left.Location.X.CompareTo(right.Location.X);

    private static long Box(ImageMatch hit) => (long)hit.Size.Width * hit.Size.Height;

    private static long Box(ScreenSize size) => (long)size.Width * size.Height;

    /// <summary>Shuffles in place, so a macro that takes the first of several does not always take the same one.</summary>
    private static void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = Random.Shared.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }
}
