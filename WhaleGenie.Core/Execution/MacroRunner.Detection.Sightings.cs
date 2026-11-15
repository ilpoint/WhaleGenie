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

// What the model found: reading the sightings, waiting for them, and clicking one.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Everything a detection model found in the areas a step named, in the order the step counts
    /// them in and in screen pixels. Which model it is and what it should look for are read from the
    /// step; loading the model and running it are the device's business.
    /// </summary>
    private List<Sighting> Sightings(ExecutableStep step)
    {
        var model = ModelOf(step);
        var query = Detection(step);
        var areas = SearchAreas(step);
        var found = new List<Sighting>();
        foreach (var (area, origin) in areas)
        {
            // 一次模型推理是整段同步的，中途读不到取消令牌：每块区域前后各问一次。
            _run.RunToken.ThrowIfCancellationRequested();
            foreach (var hit in _devices.Detector.Detect(model, area, query))
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

        Sorted(found, step);
        SawDetections(step, areas, found);
        return found;
    }

    /// <summary>The hits a wait ended on: the whole set and the one the step asked for.</summary>
    private async Task<FoundObjects> SightingsUntil(ExecutableStep step, CancellationToken token)
    {
        var interval = LookBeat();

        FoundObjects? Enough()
        {
            var found = Sightings(step);
            return Chosen(found, step) is { } match ? new FoundObjects(found, match) : null;
        }

        return await WaitForValueAsync(Enough, interval, token);
    }

    /// <summary>Looks once and writes where the thing was, or an empty value when it was not there.</summary>
    private void LookForObjects(ExecutableStep step, int depth)
    {
        var found = Sightings(step);
        var match = Chosen(found, step);
        Give(step, "found", Value.FromBool(match is not null));

        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Match.Location, match.Match.Size, match.Match.Score,
                label: match.Label, classId: match.ClassId);
        }

        GiveEvery(step, HitRecords(found));
        Log(LogLevel.Info, depth, step.Type,
            match is null ? LogKeys.DetectedNothing : LogKeys.Detected,
            match is null ? Looking(step) : Saying(match));
    }

    /// <summary>
    /// Waits until the model finds a thing the step wants, for as long as the step's own timeout
    /// allows, and writes where it was.
    /// </summary>
    private async Task WaitForObjects(ExecutableStep step, int depth, CancellationToken token)
    {
        var found = await SightingsUntil(step, token);

        GiveMatch(step, found.Match.Match.Location, found.Match.Match.Size, found.Match.Match.Score,
            label: found.Match.Label, classId: found.Match.ClassId);
        GiveEvery(step, HitRecords(found.Found));
        Log(LogLevel.Info, depth, step.Type, LogKeys.Detected, Saying(found.Match));
    }

    /// <summary>
    /// Waits until the model finds a thing, then clicks it: the middle of the box the model drew
    /// round it, moved by the step's own offsets.
    /// </summary>
    private async Task ClickObject(ExecutableStep step, int depth, CancellationToken token)
    {
        var seen = await SightingsUntil(step, token);

        // A step that asked to look again waits a moment and confirms the thing is still there: a
        // thing that flashed past, or a screen that has already moved on, is not clicked. The second
        // look is a single look rather than another wait, the same as a step that clicks a picture.
        var again = Pace(Args(step).Number("confirmMs"));
        if (again > 0)
        {
            await Pause(again, token);
            var found = Sightings(step);
            seen = Chosen(found, step) is { } match
                ? new FoundObjects(found, match)
                : throw new StepFailure(RunKeys.DetectNotFound, Looking(step));
        }

        var at = Scattered(new ScreenPoint(
            seen.Match.Match.Center.X + Args(step).Number("offsetX"),
            seen.Match.Match.Center.Y + Args(step).Number("offsetY")), Spread(step));

        GiveMatch(step, seen.Match.Match.Location, seen.Match.Match.Size, seen.Match.Match.Score,
            label: seen.Match.Label, classId: seen.Match.ClassId);
        GiveEvery(step, HitRecords(seen.Found));

        ClickAt(step, at);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedObject, Saying(seen.Match),
            $"{at.X},{at.Y}");
    }

    /// <summary>What a step that looked for things saw: the areas, every guess, and the one it took.</summary>
    private void SawDetections(ExecutableStep step,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas, IReadOnlyList<Sighting> found)
        => SawSearch(step, LookKind.Detection, areas,
            [.. found.Select(hit => (hit.Match, (string?)hit.Label))],
            WentWith(found, step), Looking(step), minimum: Args(step).Number("confidence") / 100.0);

    /// <summary>The model file a step named, as the path it is read from.</summary>
    private string ModelOf(ExecutableStep step) => _devices.Files.Resolve(PathOf(step, "model"));
}
