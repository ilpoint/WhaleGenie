using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Finding a reference picture on the screen, waiting for it to turn up, and clicking it. Every
/// place the picture appears is a hit, counted in the order the step asked for, so "the third one"
/// means the same thing here as in every other search.
/// </summary>
internal static class ImageActions
{
    /// <summary>
    /// The picture actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["vision.findImage"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => LookFor(run, step, depth, token));
                return Signal.Normal;
            },

            ["vision.waitImage"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await run.Reads.Showing(() => WaitForImage(run, step, depth, token));
                return Signal.Normal;
            },

            ["vision.clickImage"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await run.Reads.Showing(() => ClickImage(run, step, depth, token));
                return Signal.Normal;
            },
        };

    /// <summary>
    /// One look for the trial window: where the picture is right now. A wait does not wait and a
    /// click does not click — what is shown is the looking they would do first.
    /// </summary>
    internal static void Preview(IRunContext run, ExecutableStep step)
        => run.Reads.Showing(() => Hits(run, step, CancellationToken.None));

    /// <summary>Looks once and writes where the picture was, or an empty value when it was not.</summary>
    private static void LookFor(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var hits = Hits(run, step, token);
        var match = SearchHits.Choose(run, hits, step);
        run.Give(step, "found", Value.FromBool(match is not null));

        if (match is null)
        {
            run.GiveNothing(step);
        }
        else
        {
            run.GiveMatch(step, match.Location, match.Size, match.Score);
        }

        run.GiveEvery(step, HitRecord.Each(hits));
        run.Log(LogLevel.Info, depth, step.Type,
            match is null ? LogKeys.ImageMissing : LogKeys.ImageFound,
            match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    private static async Task WaitForImage(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var found = await HitsUntil(run, step, token);

        run.GiveMatch(step, found.Match.Location, found.Match.Size, found.Match.Score);
        run.GiveEvery(step, HitRecord.Each(found.Hits));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ImageFound,
            $"{found.Match.Center.X},{found.Match.Center.Y}");
    }

    private static async Task ClickImage(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var found = await HitsUntil(run, step, token);

        // A step that asked to look again waits a moment and confirms the picture is still there: a
        // button that flashed past, or a screen that has already moved on, is not clicked. The
        // second look is a single look rather than another wait: what is being asked is whether the
        // picture is still there, not whether it turns up again.
        var again = run.Pace(run.Args(step).Number("confirmMs"));
        if (again > 0)
        {
            await Pacing.Pause(again, token);
            var hits = Hits(run, step, token);
            found = SearchHits.Choose(run, hits, step) is { } match
                ? new Found(hits, match)
                : throw new StepFailure(RunKeys.ImageNotFound, StepRows.Written(step.Rows("images")));
        }

        var at = run.Points.Scatter(step, new ScreenPoint(
            found.Match.Center.X + run.Args(step).Number("offsetX"),
            found.Match.Center.Y + run.Args(step).Number("offsetY")));

        // Where it was found and every place it was found, the same as the step that only looks:
        // a step that clicks something is also a step whose position a later one may want.
        run.GiveMatch(step, found.Match.Location, found.Match.Size, found.Match.Score);
        run.GiveEvery(step, HitRecord.Each(found.Hits));

        run.Reads.ClickAt(step, at);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedImage, at.X, at.Y);
    }

    /// <summary>
    /// The hits, once there are enough of them. How long that may take is the step's own timeout
    /// and nothing else: the wait ends where the clock does, and the step it belongs to is what
    /// fails, retries or gives up from there. The beat between two looks is the machine's, so a wait
    /// running beside another macro's takes its turn rather than looking beside it.
    /// </summary>
    private static async Task<Found> HitsUntil(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        while (true)
        {
            var hits = Hits(run, step, token);
            if (SearchHits.Choose(run, hits, step) is { } match)
            {
                return new Found(hits, match);
            }

            await run.Reads.WaitForScreen(token);
        }
    }

    /// <summary>
    /// Every place the reference picture appears in the search areas, in screen coordinates and in
    /// reading order: down the screen first, then across. That is the order a person counts them in
    /// when looking at a screenshot, which is what "the third one" has to mean to be useful.
    /// Which of the pictures the step listed it goes with is the step's own choice: the first one
    /// that turns up, whichever scored closest, or all of them at once. The hits that come back are
    /// the hits of the one picture the step went with, and "the third one" counts inside it.
    /// </summary>
    internal static List<ImageMatch> Hits(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var query = SearchQuery(run, step);
        var areas = run.Reads.Areas(step);
        var wanted = run.Pictures(step);

        // One picture, looked for in every area the step named: the places it turned up, in the
        // order this step counts them in.
        List<ImageMatch> Look(int number)
        {
            var hits = new List<ImageMatch>();
            foreach (var (area, origin) in areas)
            {
                // 匹配是整个宏里最贵的一次同步调用（整屏找图中位数二百毫秒），它中途读不到取消
                // 令牌；至少在每块区域前后问一次，让「停止」在两块之间就能生效。
                token.ThrowIfCancellationRequested();
                foreach (var found in run.Devices.Vision.FindAll(area, wanted[number].Frame, query))
                {
                    hits.Add(found with
                    {
                        Location = new ScreenPoint(
                            found.Location.X + origin.X, found.Location.Y + origin.Y),
                    });
                }
            }

            SearchHits.Sort(hits, step);
            return hits;
        }

        // What the window is shown: the picture the step went with, or the first one it listed when
        // it went with none of them. Handing over nothing at all would leave out what was being
        // looked for and where.
        void SawPicture(int number, IReadOnlyList<ImageMatch> hits, bool found)
            => run.Reads.SawSearch(step, LookKind.Template, areas,
                [.. hits.Select(hit => (hit, (string?)null))],
                found ? SearchHits.WentWith(run, hits, step) : 0, wanted[number].Name,
                wanted[number].Frame, query.ConfidencePercent / 100, number + 1, wanted.Count);

        switch (WhichOfThePictures(step))
        {
            case "every":
                {
                    // The whole set has to be there. The step goes with the picture it listed
                    // first, which is the one whose hits are handed over.
                    var looked = new List<ImageMatch>[wanted.Count];
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        looked[number] = Look(number);
                        if (looked[number].Count == 0)
                        {
                            SawPicture(0, [], found: false);
                            return [];
                        }
                    }

                    SawPicture(0, looked[0], found: true);
                    return looked[0];
                }

            case "best":
                {
                    // Every picture is looked at, and the one whose closest match scored highest
                    // is the one the step goes with.
                    var looked = new List<ImageMatch>[wanted.Count];
                    var winner = -1;
                    var surest = double.NegativeInfinity;
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        looked[number] = Look(number);
                        var score = looked[number].Count == 0
                            ? double.NegativeInfinity
                            : looked[number].Max(hit => hit.Score);
                        if (score > surest)
                        {
                            surest = score;
                            winner = number;
                        }
                    }

                    if (winner < 0)
                    {
                        SawPicture(0, [], found: false);
                        return [];
                    }

                    SawPicture(winner, looked[winner], found: true);
                    return looked[winner];
                }

            default:
                {
                    // The first picture that turns up is the one the step goes with, and the rest
                    // of the list is not looked at at all.
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        var hits = Look(number);
                        if (hits.Count == 0)
                        {
                            continue;
                        }

                        SawPicture(number, hits, found: true);
                        return hits;
                    }

                    SawPicture(0, [], found: false);
                    return [];
                }
        }
    }

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
    private static VisionQuery SearchQuery(IRunContext run, ExecutableStep step)
    {
        var args = run.Args(step);
        var confidence = args.Number("confidence");
        if (confidence <= 0)
        {
            confidence = 90;
        }

        var features = args.Number("minFeatures");
        var written = args.ValueOf("transparentColor").AsText().Trim();

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
            AlgorithmOf(args.Text("detectAlgorithm")),
            confidence,
            features > 0 ? features : 6,
            transparent,
            SearchHits.Wanted(run, step),
            args.Number("tolerance"));
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

    /// <summary>The hits a wait ended on: the whole set and the one the step asked for.</summary>
    private sealed record Found(List<ImageMatch> Hits, ImageMatch Match);

    /// <summary>What a step listed, for a failure that has to show what it could not find.</summary>
    private static string Written(IReadOnlyList<IReadOnlyDictionary<string, ExpressionNode>> rows)
        => string.Join("; ", rows.Select(row =>
            string.Join(',', row.Values.Select(value => Formula.Speak(value)))));
}
