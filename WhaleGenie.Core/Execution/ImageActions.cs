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
    /// the order the step asked to count them in. That order is not one the answer is sorted into
    /// afterwards: the search itself walks the areas the way the step counts, so "the third one
    /// down the screen" is the third down the screen rather than the third of the surest three.
    /// Which of the pictures the step listed it goes with is the step's own choice: the first one
    /// that turns up, whichever scored closest, or all of them at once. The hits that come back are
    /// the hits of the one picture the step went with, and "the third one" counts inside it.
    /// </summary>
    internal static List<ImageMatch> Hits(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var areas = run.Reads.Areas(step);
        var order = HitOrder.Of(run.Args(step), () => run.Points.AnchorOf(step).Corner, areas);
        var query = SearchQuery(run, step, order);
        var wanted = run.Pictures(step);

        // One picture, looked for in every area the step named: the places it turned up, in the
        // order this step counts them in.
        List<ImageMatch> Look(int number)
        {
            // What is left out of the comparing belongs to the reference picture rather than to
            // the step: a picture cropped over a background carries that background's colours, and
            // a step that listed several pictures has one set of them per picture.
            var query = SearchQuery(run, step, order) with
            {
                Transparent = Transparent(run, step, wanted[number].Frame),
            };

            var hits = new List<ImageMatch>();
            foreach (var (area, origin) in areas)
            {
                // 匹配是整个宏里最贵的一次同步调用（整屏找图中位数二百毫秒），它中途读不到取消
                // 令牌；至少在每块区域前后问一次，让「停止」在两块之间就能生效。
                token.ThrowIfCancellationRequested();

                // A point is given to the search in the area's own pixels, because that is the
                // picture it is handed; the hits come back moved to the screen below.
                var asked = order.Kind is MatchOrder.Near
                    ? query with
                    {
                        Near = new ScreenPoint(order.Near.X - origin.X, order.Near.Y - origin.Y),
                    }
                    : query;

                foreach (var found in run.Devices.Vision.FindAll(area, wanted[number].Frame, asked))
                {
                    hits.Add(found with
                    {
                        Location = new ScreenPoint(
                            found.Location.X + origin.X, found.Location.Y + origin.Y),
                    });
                }
            }

            // Each area answered in the step's own order; putting the areas' answers together is
            // the one sort that is left. Ties fall back to reading order so the count is stable.
            HitSort.Sort(hits, order);
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
    /// How a step asked for a picture to be looked for: which way, how sure, how many places it
    /// counts along the way, and which way round it counts them. What is left out of the comparing
    /// comes with the picture rather than with the step, so it is added by <see cref="Transparent"/>.
    /// </summary>
    private static VisionQuery SearchQuery(IRunContext run, ExecutableStep step, HitOrder order)
    {
        var args = run.Args(step);
        var confidence = args.Number("confidence");
        if (confidence <= 0)
        {
            confidence = 90;
        }

        var features = args.Number("minFeatures");
        return new VisionQuery(
            AlgorithmOf(args.Text("detectAlgorithm")),
            confidence,
            features > 0 ? features : 6,
            Transparent: null,
            Wanted(run, step, order),
            args.Number("tolerance"),
            order.Walk);
    }

    /// <summary>
    /// The colours of a reference picture that take no part in the comparing: the ones the step
    /// wrote down, and, when the step asks for it, the one the picture's four corners share. A
    /// colour nobody can read used to be read as black, which silently masked pixels that had
    /// nothing to do with what the step meant; saying so is the only answer that can be acted on.
    /// </summary>
    private static IReadOnlyList<PixelColor>? Transparent(IRunContext run, ExecutableStep step,
        ImageFrame picture)
    {
        var args = run.Args(step);
        var colours = new List<PixelColor>();
        foreach (var token in ColourList.Tokens(args.Text("transparentColors")))
        {
            colours.Add(ColourList.Read(token)
                ?? throw new StepFailure(RunKeys.BadPoint, token));
        }

        if (run.Flag(step, "autoTransparent", true) && Corner(picture, args.Number("tolerance")) is { } corner
            && !colours.Contains(corner))
        {
            colours.Add(corner);
        }

        return colours.Count > 0 ? colours : null;
    }

    /// <summary>
    /// The colour all four corners of a picture are, or nothing when they are not all the same. A
    /// picture cut out of a screenshot carries the colour behind it over its corners, and leaving
    /// that colour out is what lets the same picture be found over another one. It is the rule the
    /// mainstream macro tools apply, and it is offered as a switch because a picture whose corners
    /// are a colour that belongs to the thing itself has to be found with it.
    /// </summary>
    private static PixelColor? Corner(ImageFrame picture, double tolerance)
    {
        // Two rows and two columns of picture are nothing but corners, and a picture that is one
        // colour throughout has nothing left to compare once that colour is left out.
        if (picture.Width < 3 || picture.Height < 3 || picture.IsEmpty)
        {
            return null;
        }

        var corner = picture[0, 0];
        var across = picture.Width - 1;
        var down = picture.Height - 1;
        if (!corner.Matches(picture[across, 0], tolerance)
            || !corner.Matches(picture[0, down], tolerance)
            || !corner.Matches(picture[across, down], tolerance))
        {
            return null;
        }

        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                if (!corner.Matches(picture[x, y], tolerance))
                {
                    return corner;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// How many places the search is asked for, counted along the way it was asked to walk. The
    /// search answers in that walk's order, so the number it may stop at is the hit the step counts
    /// to — except where the whole set is wanted, which is one of the returns or an order that picks
    /// a place at random from all of them.
    /// </summary>
    private static int Wanted(IRunContext run, ExecutableStep step, HitOrder order)
        => run.Wants(step, "every") || order.Kind is MatchOrder.Random
            ? RunLimits.Matches
            : run.Index(step, "matchIndex", 1);

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
