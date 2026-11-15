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
