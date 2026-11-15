using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Which picture is searched, which match is taken, and the orders a step can ask for.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Which of the pictures a step listed it goes with, as written in its field. Anything that
    /// does not read as one of the other two is the first that turns up, which is what a step
    /// written before this field existed asks for and what almost every step wants.
    /// </summary>
    private static string WhichOfThePictures(ExecutableStep step)
    {
        var written = step.Text("imageSet").Trim().ToLowerInvariant();
        return written.Length == 0 ? "first" : written;
    }

    /// <summary>
    /// How a step asked for a picture to be looked for: which way, how sure, what to leave out of
    /// the comparing, and how many hits it wants.
    /// </summary>
    private VisionQuery SearchQuery(ExecutableStep step)
    {
        var confidence = Args(step).Number("confidence");
        if (confidence <= 0)
        {
            confidence = 90;
        }

        var features = Args(step).Number("minFeatures");
        var written = Args(step).ValueOf("transparentColor").AsText().Trim();

        // A colour nobody can read used to be read as black, which silently masked pixels that had
        // nothing to do with what the step meant. Saying so is the only answer that can be acted on.
        PixelColor? transparent = null;
        if (written.Length > 0)
        {
            transparent = PixelColor.TryParse(written, out var colour)
                ? colour
                : throw new StepFailure(RunKeys.BadPoint, written);
        }

        return new VisionQuery(
            AlgorithmOf(Args(step).Text("detectAlgorithm")),
            confidence,
            features > 0 ? features : 6,
            transparent,
            Wanted(step),
            Args(step).Number("tolerance"));
    }

    /// <summary>
    /// How a step asked for a picture to be looked for, as written in its field. One field answers
    /// both "which way" and "comparing what", because those were never two questions.
    /// </summary>
    private static MatchAlgorithm AlgorithmOf(string written) => written.Trim().ToLowerInvariant()
        switch
    {
        "correlated" => MatchAlgorithm.Correlated,
        "difference" => MatchAlgorithm.Difference,
        "feature" => MatchAlgorithm.Feature,
        _ => MatchAlgorithm.Normed,
    };

    /// <summary>What order a step asked its hits to be counted in, as written in its field.</summary>
    private static MatchOrder OrderOf(ExecutableStep step) => step.Text("orderBy").Trim()
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

    /// <summary>
    /// The hits in the order the step wants to count them in, which is what "the third one" means.
    /// Reading order is how a person counts them; by score is for a macro that wants the surest one
    /// wherever it is on the screen; by area is for one that wants the whole of something rather
    /// than a piece of it; shuffled is for one that must not always take the same one.
    /// </summary>
    private static void Sorted(List<ImageMatch> hits, ExecutableStep step)
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

    /// <summary>The same for what was read off the screen: a reading of writing is a hit as well.</summary>
    private static void Sorted(List<TextSpan> spans, ExecutableStep step)
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

            default:
                // Reading order is the order the readings came back in: the reading model hands
                // them over in the order they appear on the screen, and re-sorting them by position
                // would break up a line whose words sit at slightly different heights.
                break;
        }
    }

    /// <summary>The same for what a detection model found: a thing it found is a hit as well.</summary>
    private static void Sorted(List<Sighting> found, ExecutableStep step)
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

    /// <summary>
    /// How much room a hit takes up, which is what "the biggest one" counts. An area of nothing is
    /// possible: a colour hit is one pixel and a reading of no characters has no width.
    /// </summary>
    private static long Box(ImageMatch hit) => Box(hit.Size);

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

    /// <summary>The hits a wait ended on: the whole set and the one the step asked for.</summary>
    private sealed record Found(List<ImageMatch> Hits, ImageMatch Match);
}
