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
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // ------------------------------------------------------------------ detection

    /// <summary>
    /// One thing a detection model found: where it is on the screen, how sure the model was, and
    /// which kind of thing it is — the number the model answers with and the name the label file
    /// gives that number, which is empty when the file does not name it.
    /// </summary>
    private sealed record Sighting(ImageMatch Match, int ClassId, string Label);

    /// <summary>What one look found: everything there was, and the one the step asked for.</summary>
    private sealed record FoundObjects(List<Sighting> Found, Sighting Match);

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
        var interval = LookBeat(step);

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
        var again = Pace(Number(step, "recheckMs"));
        if (again > 0)
        {
            await Pause(again, token);
            var found = Sightings(step);
            seen = Chosen(found, step) is { } match
                ? new FoundObjects(found, match)
                : throw new StepFailure(RunKeys.DetectNotFound, Looking(step));
        }

        var at = Scattered(new ScreenPoint(
            seen.Match.Match.Center.X + Number(step, "offsetX"),
            seen.Match.Match.Center.Y + Number(step, "offsetY")), Spread(step));

        GiveMatch(step, seen.Match.Match.Location, seen.Match.Match.Size, seen.Match.Match.Score,
            label: seen.Match.Label, classId: seen.Match.ClassId);
        GiveEvery(step, HitRecords(seen.Found));

        await ClickAt(step, at, token);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedObject, Saying(seen.Match),
            $"{at.X},{at.Y}");
    }

    /// <summary>What a step that looked for things saw: the areas, every guess, and the one it took.</summary>
    private void SawDetections(ExecutableStep step,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas, IReadOnlyList<Sighting> found)
        => SawSearch(step, LookKind.Detection, areas,
            [.. found.Select(hit => (hit.Match, (string?)hit.Label))],
            WentWith(found, step), Looking(step), minimum: Number(step, "confidence") / 100.0);

    /// <summary>The model file a step named, as the path it is read from.</summary>
    private string ModelOf(ExecutableStep step) => _devices.Files.Resolve(PathOf(step, "model"));

    /// <summary>The file whose lines name the model's classes, or empty when the step named none.</summary>
    private string LabelFileOf(ExecutableStep step)
    {
        var named = PathOf(step, "labelFile").Trim();
        return named.Length == 0 ? string.Empty : _devices.Files.Resolve(named);
    }

    /// <summary>What a step asked the model to look for, as the device reads it.</summary>
    private DetectionQuery Detection(ExecutableStep step)
    {
        var confidence = Number(step, "confidence");
        var overlap = Number(step, "iou");
        var input = Number(step, "inputSize");

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
            Rows(ReadText(step, "labels")),
            MatchLimit,
            LabelFileOf(step));
    }

    /// <summary>
    /// What a step was after, as the user wrote it: which model, and which classes of thing. It is
    /// what a step that found nothing says it did not find, and what the look hands over as what was
    /// being looked for.
    /// </summary>
    private string Looking(ExecutableStep step)
    {
        var model = System.IO.Path.GetFileName(ModelOf(step));
        var labels = Rows(ReadText(step, "labels"));
        return labels.Count == 0 ? model : $"{model} {string.Join(',', labels)}";
    }

    /// <summary>One thing that was found, in the words a log line reads it in: person 640,360.</summary>
    private static string Saying(Sighting sighting)
        => sighting.Label.Length > 0
            ? $"{sighting.Label} {sighting.Match.Center.X},{sighting.Match.Center.Y}"
            : $"{sighting.Match.Center.X},{sighting.Match.Center.Y}";

    /// <summary>The rows of a list a step wrote: one per line, or separated by semicolons.</summary>
    private IReadOnlyList<string> Rows(string text)
        =>
        [
            .. text.Split([';', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ];

    /// <summary>
    /// Every place the reference picture appears in the search areas, in screen coordinates and in
    /// reading order: down the screen first, then across. That is the order a person counts them in
    /// when looking at a screenshot, which is what "the third one" has to mean to be useful.
    /// Which of the pictures the step listed it goes with is the step's own choice: the first one
    /// that turns up, whichever scored closest, or all of them at once. The hits that come back are
    /// the hits of the one picture the step went with, and "the third one" counts inside it.
    /// </summary>
    private List<ImageMatch> Hits(ExecutableStep step)
    {
        var query = SearchQuery(step);
        var areas = SearchAreas(step);
        var wanted = References(step);

        // One picture, looked for in every area the step named: the places it turned up, in the
        // order this step counts them in.
        List<ImageMatch> Look(int number)
        {
            var hits = new List<ImageMatch>();
            foreach (var (area, origin) in areas)
            {
                foreach (var found in _devices.Vision.FindAll(area, wanted[number].Frame, query))
                {
                    hits.Add(found with
                    {
                        Location = new ScreenPoint(
                            found.Location.X + origin.X, found.Location.Y + origin.Y),
                    });
                }
            }

            Sorted(hits, step);
            return hits;
        }

        // What the window is shown: the picture the step went with, or the first one it listed when
        // it went with none of them. Handing over nothing at all would leave out what was being
        // looked for and where.
        void SawPicture(int number, IReadOnlyList<ImageMatch> hits, bool found)
            => SawSearch(step, LookKind.Template, areas,
                [.. hits.Select(hit => (hit, (string?)null))],
                found ? WentWith(hits, step) : 0, wanted[number].Name, wanted[number].Frame,
                query.ConfidencePercent / 100, number + 1, wanted.Count);

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
    private VisionQuery SearchQuery(ExecutableStep step)
    {
        var confidence = Number(step, "confidence");
        if (confidence <= 0)
        {
            confidence = 90;
        }

        var features = Number(step, "minFeatures");
        var skip = ReadValue(step, "ignoreColor").AsText().Trim();

        return new VisionQuery(
            AlgorithmOf(ReadText(step, "detectAlgorithm")),
            confidence,
            features > 0 ? features : 6,
            skip.Length == 0 ? null : PixelColor.Parse(skip),
            Wanted(step));
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

    /// <summary>
    /// The hits, once there are enough of them. How long that may take is the step's own timeout
    /// and nothing else: the wait ends where the clock does, and the step it belongs to is what
    /// fails, retries or gives up from there.
    /// </summary>
    private async Task<Found> HitsUntil(ExecutableStep step, CancellationToken token)
    {
        var interval = LookBeat(step);

        Found? Enough()
        {
            var hits = Hits(step);
            return Chosen(hits, step) is { } match ? new Found(hits, match) : null;
        }

        return await WaitForValueAsync(Enough, interval, token);
    }

    /// <summary>How many hits to look for: the one the step asked for, or the whole list.</summary>
    /// <summary>
    /// How many hits a search has to collect: all it can hold when the step keeps the whole list,
    /// and only as far as the hit it named otherwise. Asking for the list is what makes collecting
    /// it worth anything, and one step hands that over by ticking the box rather than by a switch of
    /// its own.
    /// </summary>
    private int Wanted(ExecutableStep step) => Wants(step, "every")
        ? MatchLimit
        : Index(step, "matchIndex", 1);

    /// <summary>The hit a step wants, counted from the top left, or null when there are not that many.</summary>
    private ImageMatch? Chosen(IReadOnlyList<ImageMatch> hits, ExecutableStep step)
    {
        var index = Index(step, "matchIndex", 1);
        return index <= hits.Count ? hits[index - 1] : null;
    }

    /// <summary>The same, for the things a detection model found.</summary>
    private Sighting? Chosen(IReadOnlyList<Sighting> found, ExecutableStep step)
    {
        var index = Index(step, "matchIndex", 1);
        return index <= found.Count ? found[index - 1] : null;
    }

    /// <summary>
    /// Which of the hits a step went with, counted from one, or zero when there are not that many
    /// to choose from.
    /// </summary>
    private int WentWith<T>(IReadOnlyList<T> hits, ExecutableStep step)
    {
        var index = Index(step, "matchIndex", 1);
        return index <= hits.Count ? index : 0;
    }

    /// <summary>
    /// Which hit a step means, counted from one. Anything that does not read as a number from one
    /// onwards falls back to the first, so a half-written step aims at something rather than at
    /// nothing.
    /// </summary>
    private int Index(ExecutableStep step, string name, int fallback)
    {
        var text = ReadText(step, name).Trim();
        return text.Length == 0 ? fallback : Math.Clamp(Number(step, name), 1, MatchLimit);
    }

    /// <summary>Reading order, the order the hits are counted in.</summary>
    private static int Reading(ImageMatch left, ImageMatch right)
        => left.Location.Y != right.Location.Y
            ? left.Location.Y.CompareTo(right.Location.Y)
            : left.Location.X.CompareTo(right.Location.X);

    /// <summary>The most hits one step collects, so a flat colour cannot fill memory.</summary>
    private const int MatchLimit = 200;

    /// <summary>
    /// The pictures a step looks for, in the order it listed them. A picture the step did not fill
    /// in is left out rather than looked for; a step that lists none is a step that cannot look for
    /// anything, and says so.
    /// </summary>
    private IReadOnlyList<WantedPicture> References(ExecutableStep step)
    {
        var wanted = new List<WantedPicture>();
        foreach (var row in step.Rows("images"))
        {
            var written = row.TryGetValue("image", out var text) ? text.Trim() : string.Empty;
            if (written.Length > 0)
            {
                wanted.Add(new WantedPicture(written, Picture(written)));
            }
        }

        return wanted.Count > 0 ? wanted : throw new StepFailure(RunKeys.MissingImage, string.Empty);
    }

    /// <summary>One reference picture of a step: how the step wrote it down, and the picture itself.</summary>
    private sealed record WantedPicture(string Name, ImageFrame Frame);

    /// <summary>
    /// The one reference picture a step names, for the steps that hold a single one rather than a
    /// list: something an earlier Capture saved, or a file on disk.
    /// </summary>
    private ImageFrame Reference(ExecutableStep step)
    {
        var text = ReadText(step, "imageSource").Trim();
        if (text.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingImage, string.Empty);
        }

        return Picture(text);
    }

    /// <summary>
    /// A picture the step wrote down: what an earlier Capture saved, or a file on disk. The picture
    /// is kept beside the time the file was last written, so a macro that looks for the same picture
    /// again — in a loop, or every time it polls while it waits — uses the one it already has
    /// instead of reading and decoding the file again.
    /// </summary>
    private ImageFrame Picture(string text)
    {
        // A picture an earlier Capture saved, named with or without the dollar sign so it reads
        // the same as every other field a variable can go in.
        var named = text.StartsWith('$') ? text[1..].Trim() : text;
        if (_images.TryGetValue(named, out var captured))
        {
            return captured;
        }

        // Otherwise it is a file, either written out or held in a variable.
        var path = Read(text).AsText();
        var written = Written(path);
        if (written is { } stamp
            && _references.TryGetValue(path, out var kept)
            && kept.Written == stamp)
        {
            return kept.Frame;
        }

        var picture = _devices.Vision.Load(path) ?? throw new StepFailure(RunKeys.MissingImage, text);
        if (written is not null)
        {
            _references[path] = (written.Value, picture);
        }

        return picture;
    }

    /// <summary>
    /// When a file was last written, or null when it cannot be asked — a picture held in a
    /// variable, or a path this run has no business reading. Nothing is kept for those.
    /// </summary>
    private static DateTime? Written(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The places to search: the whole screen unless the step names regions, each of which may be
    /// counted from a window's corner when the step says so. One row of the step's region is either
    /// a rectangle written out — which is what the region picker fills in — or the name of a
    /// variable, which is how a rectangle or a picture taken earlier gets to be searched.
    /// </summary>
    private IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> SearchAreas(ExecutableStep step)
    {
        var rows = step.Rows("region");
        if (rows.Count == 0)
        {
            // With nowhere written down, the whole of whatever the step is about: the window its
            // numbers are counted from when it names one, and the desktop otherwise.
            var anchor = AnchorOf(step);
            if (anchor.Window is { } window)
            {
                var client = _devices.Windows.ClientOrigin(window.Handle);
                var size = _devices.Windows.ClientSize(window.Handle);
                var whole = Shot(step, client.X, client.Y, size.Width, size.Height, window);
                return [(whole.Frame, whole.Origin)];
            }

            var desk = _devices.Screen.Desktop.Bounds;
            var desktop = Shot(step, desk.Left, desk.Top, desk.Width, desk.Height);
            return [(desktop.Frame, desktop.Origin)];
        }

        var areas = new List<(ImageFrame, ScreenPoint)>();
        foreach (var row in rows)
        {
            areas.AddRange(Areas(step, row));
        }

        if (areas.Count == 0)
        {
            throw new StepFailure(RunKeys.BadRegion, Written(rows));
        }

        return areas;
    }

    /// <summary>What the regions of a step say, for a failure that has to show what it could not read.</summary>
    private static string Written(IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
        => string.Join("; ", rows.Select(row => string.Join(',', row.Values)));

    /// <summary>
    /// What one region of a step comes to. A rectangle written out says where it is; a region that
    /// names something looks the name up, which is how a picture an earlier Capture took is
    /// searched — the screen is grabbed once and looked at as often as the macro likes — and how a
    /// rectangle held in a variable gets to be used.
    /// </summary>
    private IEnumerable<(ImageFrame Frame, ScreenPoint Origin)> Areas(ExecutableStep step,
        IReadOnlyDictionary<string, string> row)
    {
        if (!row.TryGetValue("text", out var written))
        {
            yield return Rectangle(step, row);
            yield break;
        }

        // A picture taken earlier is looked up by the name the step wrote rather than through the
        // variables: what a variable holds is a note about the picture, and it is the picture
        // itself that is searched.
        var raw = written.Trim();
        var name = raw.StartsWith('$') ? raw[1..].Trim() : raw;
        if (_images.TryGetValue(name, out var picture))
        {
            yield return (picture, new ScreenPoint(0, 0));
            yield break;
        }

        // A record is one rectangle with a name for each of its sides, which is what a hit hands
        // over: naming that record here is how "look again at what you just found" is written
        // without the macro spelling the four numbers out again.
        if (Read(raw) is { IsRecord: true } record && Held(step, record) is { } held)
        {
            yield return held;
            yield break;
        }

        // Anything else is a rectangle, written out or held in a variable. It is only interpolated
        // rather than read as a whole expression: the commas of a written rectangle would stop an
        // expression at its first number.
        var named = Interpolate(raw).Trim();

        // Several rectangles are separated by a semicolon or a line break, so a variable holding a
        // list of them goes on working the way it always did.
        foreach (var rectangle in named.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Written(step, rectangle);
        }
    }

    /// <summary>
    /// The rectangle a record stands for: its four sides by name, or the one written rectangle an
    /// action that hands its box over as text puts in it. Nothing when the record is neither, so a
    /// record that is not a rectangle is reported as a region nobody could read rather than as a
    /// failure of its own.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin)? Held(ExecutableStep step, Value record)
    {
        if (Sides(record) is { } sides)
        {
            // The record's own numbers are where the rectangle is on screen, the way a hit's are:
            // the step's coordinate origin moves the four numbers it is given as text, not this.
            // Which window the picture is read out of is a different question, and stays the
            // step's own answer.
            var shot = Shot(step, sides.Left, sides.Top, sides.Width, sides.Height,
                AnchorOf(step).Window);
            return (shot.Frame, shot.Origin);
        }

        var box = record.Field("box")?.AsText().Trim() ?? string.Empty;
        return box.Length > 0 ? Written(step, box) : null;
    }

    /// <summary>One side of a rectangle held in a record, in whole screen pixels.</summary>
    private static int Whole(Value side) => (int)Math.Round(side.AsNumber());

    /// <summary>
    /// The four sides a record says a rectangle has, in whole screen pixels: the corner it starts at
    /// and how far it reaches from there. Every record that carries a rectangle carries all four of
    /// them, which is what lets a hit, a window or a picture taken earlier say where a step has to
    /// look without the macro spelling the numbers out again. Nothing when the record is not a
    /// rectangle, so a record of some other shape is refused by whoever asked for one. A rectangle
    /// with no width or no height is one pixel wide, the way a width nobody wrote is read.
    /// </summary>
    private static (int Left, int Top, int Width, int Height)? Sides(Value record)
    {
        if (record.Field(FieldNames.Left) is not { } left
            || record.Field(FieldNames.Top) is not { } top
            || record.Field(FieldNames.Width) is not { } width
            || record.Field(FieldNames.Height) is not { } height)
        {
            return null;
        }

        return (Whole(left), Whole(top), Math.Max(1, Whole(width)), Math.Max(1, Whole(height)));
    }

    /// <summary>One region written as four numbers, captured where on the screen it says.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Rectangle(ExecutableStep step,
        IReadOnlyDictionary<string, string> row)
    {
        var x = Cell(row, "x");
        var y = Cell(row, "y");
        var width = Cell(row, FieldNames.Width);
        var height = Cell(row, FieldNames.Height);
        if (width <= 0 || height <= 0)
        {
            throw new StepFailure(RunKeys.BadRegion, Written([row]));
        }

        return Capture(step, x, y, width, height);
    }

    /// <summary>What a row of a list parameter says in one column, or nothing when it has none.</summary>
    private static string Column(IReadOnlyDictionary<string, string> row, string column)
        => row.TryGetValue(column, out var written) ? written : string.Empty;

    /// <summary>One number of a row of a list parameter, as its field was filled in.</summary>
    private int Cell(IReadOnlyDictionary<string, string> row, string column)
        => (int)Math.Round(Read(Column(row, column)).AsNumber());

    /// <summary>
    /// One number of a row that may be left empty, where empty means the run's own value: a row
    /// only has to spell out the hold or the gap that is not the one everywhere else.
    /// </summary>
    private int Cell(IReadOnlyDictionary<string, string> row, string column, int fallback)
        => Column(row, column).Trim().Length > 0
            ? (int)Math.Round(Read(Column(row, column)).AsNumber())
            : fallback;

    /// <summary>One written rectangle, such as one a variable holds.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Written(ExecutableStep step, string rectangle)
    {
        var parts = rectangle.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            throw new StepFailure(RunKeys.BadRegion, rectangle);
        }

        return Capture(step, x, y, width, height);
    }

    /// <summary>
    /// The picture taken of one rectangle. A window-anchored rectangle is counted from that
    /// window's corner, and the origin handed back is the one on screen, so the positions read out
    /// of the picture are pixels the rest of the macro can click on.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin) Capture(ExecutableStep step, int x, int y,
        int width, int height)
    {
        var anchor = AnchorOf(step);
        var corner = Placed(anchor.Corner, x, y);
        var shot = Shot(step, corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// One picture of the screen, taken the way this machine is set up to take one: a step says what
    /// it is looking at — a window, or a rectangle of the desktop — and whether the pixels come off
    /// the screen or out of the window itself is an answer about the machine rather than about the
    /// step. The window is the one the step's own coordinates are counted from, or the one it names
    /// outright.
    /// </summary>
    private ScreenShot Shot(ExecutableStep step, int x, int y, int width, int height,
        WindowInfo? window = null)
    {
        // Which window this is about to be read out of is asked for before anything is read, so a
        // debugger can put a frame there while the reading is going on rather than after it.
        Reading(window);

        var method = window is null
            ? _devices.Capture.ForScreen()
            : _devices.Capture.ForWindow();

        var source = window?.Handle ?? 0;
        var age = Math.Max(0, Number(step, "frameMaxAgeMs"));
        if (age == 0)
        {
            // Nothing already taken is old enough to do, so the rectangle asked for is the rectangle
            // read: this is the reading a macro has always done, at the cost it has always had.
            var exact = _devices.Screen.Capture(new ScreenCaptureRequest(x, y, width, height,
                method, source));
            Taken(window, exact.Method);
            return exact;
        }

        var key = Reused(source, method);
        var now = Stopwatch.GetTimestamp();
        if (_frames.TryGetValue(key, out var kept)
            && Stopwatch.GetElapsedTime(kept.TakenAt, now).TotalMilliseconds <= age
            && Covers(kept.Frame, kept.Origin, x, y, width, height))
        {
            Taken(window, kept.Method);
            return Cut(kept.Frame, kept.Origin, x, y, width, height, kept.Method);
        }

        // The whole of what the step is reading, so that the next step wanting another part of it
        // is served without reading the screen again: asking for one rectangle costs more than that
        // rectangle, and asking for the whole source is what makes the next look free.
        var whole = Whole(method, window);
        _frames[key] = (whole.Frame, whole.Origin, whole.Method, now);
        Taken(window, whole.Method);
        return Cut(whole.Frame, whole.Origin, x, y, width, height, whole.Method);
    }

    /// <summary>
    /// Which pictures are worth keeping for a second look: one to each source, one to each way of
    /// reading it. Two steps reading the same window the same way see the same picture, and a step
    /// reading it another way gets its own — a window copied off the screen and a window read as the
    /// window itself shows it are not the same pixels. What is keyed on is the way that was asked
    /// for rather than the one that came back, because the next step has to look its picture up
    /// before anything is read: the automatic choice is a question, and this machine answers it the
    /// same way twice in a run.
    /// </summary>
    private static (long Source, CaptureWay Method) Reused(long source, CaptureWay method)
        => (source, method);

    /// <summary>The whole of what a step reads from: the window, or the screen.</summary>
    private ScreenShot Whole(CaptureWay method, WindowInfo? window)
    {
        if (window is null)
        {
            var desk = _devices.Screen.Desktop.Bounds;
            return _devices.Screen.Capture(
                new ScreenCaptureRequest(desk.Left, desk.Top, desk.Width, desk.Height, method));
        }

        return _devices.Screen.Capture(new ScreenCaptureRequest(window.Location.X, window.Location.Y,
            window.Size.Width, window.Size.Height, method, window.Handle));
    }

    /// <summary>True when a picture taken whole covers the rectangle a step is asking for.</summary>
    private static bool Covers(ImageFrame frame, ScreenPoint origin, int x, int y, int width,
        int height)
        => x >= origin.X && y >= origin.Y
           && x + width <= origin.X + frame.Width && y + height <= origin.Y + frame.Height;

    /// <summary>
    /// The part of a picture taken whole that a step asked for. A rectangle reaching past the
    /// picture is cut down to what is there, the way a window read as itself is, and one altogether
    /// outside the picture is nothing to look at.
    /// </summary>
    private static ScreenShot Cut(ImageFrame whole, ScreenPoint origin, int x, int y, int width,
        int height, CaptureWay method)
    {
        if (origin.X == x && origin.Y == y && whole.Width == width && whole.Height == height)
        {
            return new ScreenShot(whole, origin, method);
        }

        var left = Math.Max(0, x - origin.X);
        var top = Math.Max(0, y - origin.Y);
        var right = Math.Min(whole.Width, x - origin.X + width);
        var bottom = Math.Min(whole.Height, y - origin.Y + height);
        if (right <= left || bottom <= top)
        {
            throw new StepFailure(RunKeys.EmptyRegion, $"{x},{y} {width}x{height}");
        }

        var kept = new byte[(long)(right - left) * (bottom - top) * 4];
        for (var row = 0; row < bottom - top; row++)
        {
            Array.Copy(whole.Bgra, ((long)(top + row) * whole.Width + left) * 4, kept,
                (long)row * (right - left) * 4, (right - left) * 4);
        }

        return new ScreenShot(new ImageFrame(right - left, bottom - top, kept),
            new ScreenPoint(origin.X + left, origin.Y + top), method);
    }

    /// <summary>Notes what the picture a step is about to be given was taken of.</summary>
    private void Taken(WindowInfo? window, CaptureWay method)
    {
        _takenWindow = window?.Title ?? string.Empty;
        _takenMethod = method;
    }

    /// <summary>Tells whoever is watching which window the next picture comes out of.</summary>
    private void Reading(WindowInfo? window) => _looks?.Reading(window);

    /// <summary>
    /// Waits out however much of this step's gap is left and notes the look. Every step that reads
    /// the screen comes through here, so a step held to a gap is held to it whichever way it reads.
    /// </summary>
    private async Task Gate(ExecutableStep step, CancellationToken token)
    {
        var left = Until(step);
        if (left > 0)
        {
            await Pause(left, token);
        }

        Looked(step);
    }

    /// <summary>
    /// The same wait for a condition, which is asked for its answer rather than awaited, so there
    /// is nothing to hand back to while the gap runs out.
    /// </summary>
    private void GateNow(ExecutableStep step, CancellationToken token)
    {
        var left = Until(step);
        while (left > 0)
        {
            token.ThrowIfCancellationRequested();
            var nap = Math.Min(left, 20);
            Thread.Sleep(nap);
            left -= nap;
        }

        Looked(step);
    }

    /// <summary>
    /// How long this step still has to wait before it may look: nothing when it has never looked or
    /// leaves no gap at all.
    /// </summary>
    private int Until(ExecutableStep step)
    {
        var interval = Math.Max(0, Number(step, "intervalMs"));
        if (interval <= 0 || !_looked.TryGetValue(Key(step), out var last))
        {
            return 0;
        }

        return Math.Max(0, interval - (int)Stopwatch.GetElapsedTime(last).TotalMilliseconds);
    }

    /// <summary>Notes that a step is looking now, which is what the step after it is measured from.</summary>
    private void Looked(ExecutableStep step) => _looked[Key(step)] = Stopwatch.GetTimestamp();

    private static object Key(ExecutableStep step) => step.Id.Length > 0 ? step.Id : step;

}
