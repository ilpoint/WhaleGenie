using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The detection actions: asking a model what it sees, waiting for a thing to turn up, and clicking
/// one. Loading the model and running it are the device's business; which model, what to look for
/// and which hit the step means are read here.
/// </summary>
internal static class DetectionActions
{
    /// <summary>
    /// The detection actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["vision.detect"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => LookForObjects(run, step, depth, token));
                return Signal.Normal;
            },

            ["vision.waitDetect"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await run.Reads.Showing(() => WaitForObjects(run, step, depth, token));
                return Signal.Normal;
            },

            ["vision.clickDetect"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await run.Reads.Showing(() => ClickObject(run, step, depth, token));
                return Signal.Normal;
            },
        };

    /// <summary>
    /// Everything a detection model found in the areas a step named, in the order the step counts
    /// them in and in screen pixels.
    /// </summary>
    internal static List<Sighting> Sightings(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var model = SearchHits.ModelOf(run, step);
        var query = Query(run, step);
        var areas = run.Reads.Areas(step);
        var found = new List<Sighting>();
        foreach (var (area, origin) in areas)
        {
            // 一次模型推理是整段同步的，中途读不到取消令牌：每块区域前后各问一次。
            token.ThrowIfCancellationRequested();
            foreach (var hit in run.Devices.Detector.Detect(model, area, query))
            {
                // The device counts from the corner of the picture it was handed; a step counts
                // from the screen, the way every other step that looks at the screen does.
                found.Add(new Sighting(
                    new ImageMatch(hit.Score,
                        new ScreenPoint(origin.X + hit.Location.X, origin.Y + hit.Location.Y),
                        hit.Size),
                    hit.ClassId,
                    hit.Label));
            }
        }

        SearchHits.Sort(found, step);
        SawDetections(run, step, areas, found);
        return found;
    }

    /// <summary>The hits a wait ended on: the whole set and the one the step asked for.</summary>
    private static async Task<FoundObjects> SightingsUntil(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var interval = run.Reads.Beat();

        FoundObjects? Enough()
        {
            var found = Sightings(run, step, token);
            return SearchHits.Choose(run, found, step) is { } match
                ? new FoundObjects(found, match)
                : null;
        }

        return await Pacing.WaitForValueAsync(Enough, interval, token)
            ?? throw new StepFailure(RunKeys.DetectNotFound, SearchHits.Looking(run, step));
    }

    /// <summary>Looks once and writes where the thing was, or an empty value when it was not there.</summary>
    private static void LookForObjects(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var found = Sightings(run, step, token);
        var match = SearchHits.Choose(run, found, step);
        run.Give(step, "found", Value.FromBool(match is not null));

        if (match is null)
        {
            run.GiveNothing(step);
        }
        else
        {
            run.GiveMatch(step, match.Match.Location, match.Match.Size, match.Match.Score,
                label: match.Label, classId: match.ClassId);
        }

        run.GiveEvery(step, Records(found));
        run.Log(LogLevel.Info, depth, step.Type,
            match is null ? LogKeys.DetectedNothing : LogKeys.Detected,
            match is null ? SearchHits.Looking(run, step) : SearchHits.Describe(match));
    }

    /// <summary>
    /// Waits until the model finds a thing the step wants, for as long as the step's own timeout
    /// allows, and writes where it was.
    /// </summary>
    private static async Task WaitForObjects(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var found = await SightingsUntil(run, step, token);

        run.GiveMatch(step, found.Match.Match.Location, found.Match.Match.Size,
            found.Match.Match.Score, label: found.Match.Label, classId: found.Match.ClassId);
        run.GiveEvery(step, Records(found.Found));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Detected,
            SearchHits.Describe(found.Match));
    }

    /// <summary>
    /// Waits until the model finds a thing, then clicks it: the middle of the box the model drew
    /// round it, moved by the step's own offsets.
    /// </summary>
    private static async Task ClickObject(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var seen = await SightingsUntil(run, step, token);

        // A step that asked to look again waits a moment and confirms the thing is still there: a
        // thing that flashed past, or a screen that has already moved on, is not clicked. The second
        // look is a single look rather than another wait, the same as a step that clicks a picture.
        var again = run.Pace(run.Args(step).Number("confirmMs"));
        if (again > 0)
        {
            await Pacing.Pause(again, token);
            var found = Sightings(run, step, token);
            seen = SearchHits.Choose(run, found, step) is { } match
                ? new FoundObjects(found, match)
                : throw new StepFailure(RunKeys.DetectNotFound, SearchHits.Looking(run, step));
        }

        var at = run.Points.Scatter(step, new ScreenPoint(
            seen.Match.Match.Center.X + run.Args(step).Number("offsetX"),
            seen.Match.Match.Center.Y + run.Args(step).Number("offsetY")));

        run.GiveMatch(step, seen.Match.Match.Location, seen.Match.Match.Size,
            seen.Match.Match.Score, label: seen.Match.Label, classId: seen.Match.ClassId);
        run.GiveEvery(step, Records(seen.Found));

        run.Reads.ClickAt(step, at);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedObject,
            SearchHits.Describe(seen.Match), $"{at.X},{at.Y}");
    }

    /// <summary>What a step that looked for things saw: the areas, every guess, and the one it took.</summary>
    private static void SawDetections(IRunContext run, ExecutableStep step,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas, IReadOnlyList<Sighting> found)
        => run.Reads.SawSearch(step, LookKind.Detection, areas,
            [.. found.Select(hit => (hit.Match, (string?)hit.Label))],
            SearchHits.WentWith(run, found, step), SearchHits.Looking(run, step),
            run.Args(step).Number("confidence") / 100.0);

    /// <summary>Every thing the model found as a record, kind of thing and all.</summary>
    private static IEnumerable<Value> Records(IReadOnlyList<Sighting> found)
        => found.Select(hit => HitRecord.Of(hit.Match.Location, hit.Match.Size, hit.Match.Score,
            label: hit.Label, classId: hit.ClassId));

    /// <summary>What a step asked the model to look for, as the device reads it.</summary>
    private static DetectionQuery Query(IRunContext run, ExecutableStep step)
    {
        var confidence = run.Args(step).Number("confidence");
        var overlap = run.Args(step).Number("iou");
        var input = run.Args(step).Number("inputSize");

        // Every box the model found is collected, whatever the step will take out of them. Looking
        // for more costs nothing here — one pass of the model answers with all of them — while asking
        // for as few as the step needs would leave the device to cut the list down by score before
        // the step's own 「命中排序」 and 「第几个命中」 had their say, so a step counting in reading
        // order would have been handed whichever box scored highest. The whole list is also what the
        // look hands over, which is the point of the window.
        return new DetectionQuery(
            confidence > 0 ? confidence : 25,
            input >= 32 ? input : 640,
            overlap > 0 ? overlap : 45,
            SearchHits.Labels(run.Args(step).Text("labels")),
            RunLimits.Matches,
            LabelFileOf(run, step));
    }

    /// <summary>The file whose lines name the model's classes, or empty when the step named none.</summary>
    private static string LabelFileOf(IRunContext run, ExecutableStep step)
    {
        var named = run.PathOf(step, "labelFile").Trim();
        return named.Length == 0 ? string.Empty : run.Devices.Files.Resolve(named);
    }

    /// <summary>The hits a wait ended on: the whole set and the one the step asked for.</summary>
    private sealed record FoundObjects(List<Sighting> Found, Sighting Match);
}
