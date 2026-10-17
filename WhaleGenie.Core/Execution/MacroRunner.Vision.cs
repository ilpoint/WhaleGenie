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
    // -------------------------------------------------------------------- looks

    /// <summary>
    /// Runs one step that looks at the screen and hands over what it saw, whatever came of it: the
    /// look of a step that found nothing is the one worth having.
    /// </summary>
    private void Watching(Action body)
    {
        _saw = null;
        try
        {
            body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>The same, for a step that has to wait for what it is looking at.</summary>
    private async Task Watching(Func<Task> body)
    {
        _saw = null;
        try
        {
            await body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>Hands the look over to whoever is watching, and forgets it.</summary>
    private void Show()
    {
        var look = _saw;
        _saw = null;
        if (_looks is null || look is null)
        {
            return;
        }

        _looks.Look(look);
    }

    /// <summary>
    /// How the marks are built, and what sort of looking this was. Nothing is built when nobody is
    /// watching: a picture of every step of a run nobody is looking at is work for nothing, and a
    /// step that waits would build one every two hundred milliseconds.
    /// </summary>
    private void Saw(ExecutableStep step, LookKind kind, ImageFrame frame, ScreenPoint origin,
        IReadOnlyList<LookBox> boxes, int chosen, string looking = "", ImageFrame? needle = null,
        string? note = null, double? minimum = null, int picture = 0, int pictures = 0)
    {
        if (_looks is null)
        {
            return;
        }

        _saw = new StepLook
        {
            StepId = step.Id,
            StepType = step.Type,
            Kind = kind,
            Frame = frame,
            Origin = origin,
            Boxes = boxes,
            ChosenIndex = chosen,
            Looking = looking,
            Needle = needle,
            Note = note,
            Minimum = minimum,
            PictureNumber = picture,
            PictureCount = pictures,
            Source = _takenWindow,
            Method = _takenMethod ?? CaptureWay.Auto,
        };
    }

    /// <summary>
    /// What a step that looked for something saw: the areas it searched, every place the thing was
    /// found, and the one it went with. Several areas are put together into one picture so that
    /// every mark has somewhere to sit.
    /// </summary>
    private void SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string looking = "",
        ImageFrame? needle = null, double? minimum = null, int picture = 0, int pictures = 0)
    {
        if (_looks is null)
        {
            return;
        }

        var (frame, origin) = Picture(areas);
        Saw(step, kind, frame, origin, Marks(areas, found, chosen),
            chosen == 0 ? 0 : areas.Count + chosen, looking, needle, null, minimum, picture, pictures);
    }

    /// <summary>
    /// Adds where a step has just acted to the look it took to get there, so a step that clicked
    /// the wrong place can be seen rather than guessed at.
    /// </summary>
    private void Aim(ScreenPoint point)
    {
        if (_looks is null || _saw is null)
        {
            return;
        }

        var boxes = new List<LookBox>(_saw.Boxes)
        {
            new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
        };

        _saw = _saw with { Boxes = boxes, ChosenIndex = boxes.Count };
    }

    /// <summary>
    /// A look at a single pixel: a square of screen around it with the pixel itself marked, which
    /// is what a step watching for a colour has to show for itself.
    /// </summary>
    private void SawPixel(ExecutableStep step, ScreenPoint point, string looking,
        WindowInfo? window)
    {
        if (_looks is null)
        {
            return;
        }

        var desk = _devices.Screen.Desktop.Bounds;
        var left = Math.Clamp(point.X - Neighbourhood, desk.Left, Math.Max(desk.Left, desk.Right - 1));
        var top = Math.Clamp(point.Y - Neighbourhood, desk.Top, Math.Max(desk.Top, desk.Bottom - 1));
        var right = Math.Clamp(point.X + Neighbourhood + 1, left + 1, desk.Right);
        var bottom = Math.Clamp(point.Y + Neighbourhood + 1, top + 1, desk.Bottom);

        var shot = Shot(step, left, top, right - left, bottom - top, window);
        Saw(step, LookKind.Pixel, shot.Frame, shot.Origin,
            [
                new(new ImageMatch(1, shot.Origin,
                    new ScreenSize(shot.Frame.Width, shot.Frame.Height)), LookRole.Area),
                new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
            ],
            2, looking);
    }

    /// <summary>How far around a watched pixel the look reaches, so the pixel has some context.</summary>
    private const int Neighbourhood = 60;

    /// <summary>The marks for a look: where it looked, and everything that was found there.</summary>
    private static List<LookBox> Marks(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen)
    {
        var marks = new List<LookBox>();
        foreach (var (area, origin) in areas)
        {
            marks.Add(new LookBox(
                new ImageMatch(0, origin, new ScreenSize(area.Width, area.Height)), LookRole.Area));
        }

        for (var index = 0; index < found.Count; index++)
        {
            marks.Add(new LookBox(
                found[index].Match,
                index + 1 == chosen ? LookRole.Hit : LookRole.Candidate,
                found[index].Label));
        }

        return marks;
    }

    /// <summary>
    /// The picture to draw a step's marks on: the single area it looked at, or the whole of what
    /// it looked at when there are several. Several are laid out of the areas themselves rather
    /// than read off the screen again — where the areas came from is not always the screen, and a
    /// step that reads a window it does not cover would otherwise be drawn over a picture of
    /// whatever is on top of it.
    /// </summary>
    private static (ImageFrame Frame, ScreenPoint Origin) Picture(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas)
    {
        if (areas.Count == 1)
        {
            return areas[0];
        }

        var left = areas.Min(area => area.Origin.X);
        var top = areas.Min(area => area.Origin.Y);
        var width = Math.Max(1, areas.Max(area => area.Origin.X + area.Frame.Width) - left);
        var height = Math.Max(1, areas.Max(area => area.Origin.Y + area.Frame.Height) - top);
        var pixels = new byte[(long)width * height * 4];
        foreach (var (frame, origin) in areas)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Array.Copy(frame.Bgra, (long)row * frame.Width * 4, pixels,
                    ((long)(origin.Y - top + row) * width + (origin.X - left)) * 4,
                    (long)frame.Width * 4);
            }
        }

        return (new ImageFrame(width, height, pixels), new ScreenPoint(left, top));
    }

    /// <summary>Copies a region of the screen into a variable the macro can look at again.</summary>
    private void Capture(ExecutableStep step, int depth)
    {
        var (frame, corner) = CaptureBox(step);
        Keep(step, depth, frame, corner);
    }

    /// <summary>
    /// The rectangle a capture step covers, taken off the screen. The corner is worked out first so
    /// that the rectangle reported is the one on screen, which is what a later "region" naming this
    /// picture has to read.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Corner) CaptureBox(ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        var (corner, width, height) = Box(step, anchor.Corner);
        var shot = Shot(step, corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// The rectangle a step names, whether it reads or copies one part of the screen: the record it
    /// takes the rectangle from, or the four numbers it writes down. Every record that carries a
    /// rectangle carries all four sides of it, so a hit, a window or a picture taken earlier goes in
    /// as it stands — "read the writing on what was just found" is one field rather than four
    /// readings of <c>field()</c> with a chance of one of them being the neighbour's. A record's
    /// numbers are screen pixels, the way every record's are; numbers written out are counted from
    /// <paramref name="origin"/>, which is the corner the step says its own numbers are measured
    /// from. A side nobody wrote comes to whatever the catalogue says its empty cell means: a
    /// reading step runs to the edge of the screen.
    /// </summary>
    private (ScreenPoint Corner, int Width, int Height) Box(ExecutableStep step, ScreenPoint? origin)
    {
        if (FromRecord(step))
        {
            var raw = ReadText(step, "record").Trim();
            if (raw.Length == 0)
            {
                throw new StepFailure(RunKeys.BadPointEmpty, "record");
            }

            if (Read(raw) is not { IsRecord: true } record || Sides(record) is not { } sides)
            {
                throw new StepFailure(RunKeys.BadRegion, raw);
            }

            return (new ScreenPoint(sides.Left, sides.Top), sides.Width, sides.Height);
        }

        var corner = Placed(origin, Number(step, "x"), Number(step, "y"));
        var desk = _devices.Screen.Desktop.Bounds;
        return (corner,
            Reach(step, FieldNames.Width, corner.X, desk.Right),
            Reach(step, FieldNames.Height, corner.Y, desk.Bottom));
    }

    /// <summary>
    /// One side of the rectangle: the number the step wrote, or — for a side whose empty cell the
    /// catalogue calls "to the edge" — the rest of the screen from where the rectangle starts.
    /// </summary>
    private int Reach(ExecutableStep step, string name, int from, int limit)
        => !Given(step, name) && Empties(step, name) == ActionEmptyMeans.ToEdge
            ? Math.Max(1, limit - from)
            : Math.Max(1, Number(step, name));

    /// <summary>
    /// Copies a whole window into a variable. The window's own rectangle is what is copied, so the
    /// title bar and the border come with it, and a window that is not in front can be read too.
    /// </summary>
    private void CaptureWindow(ExecutableStep step, int depth)
    {
        var (frame, corner) = WindowBox(step);
        Keep(step, depth, frame, corner);
    }

    /// <summary>The whole of the window a step names, taken off the screen.</summary>
    private (ImageFrame Frame, ScreenPoint Corner) WindowBox(ExecutableStep step)
    {
        var window = Locate(step);
        var shot = Shot(step, window.Location.X, window.Location.Y, window.Size.Width,
            window.Size.Height, window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// Keeps a picture under the name a step gave it, with the rectangle it covers beside it, so a
    /// later step can search inside it or hand the region to something else. The size written down
    /// is the picture's own rather than the size that was asked for: a capture that ran off the
    /// edge of the screen comes back smaller, and a search region that is bigger than the picture
    /// would look at pixels that are not there.
    /// </summary>
    private void Keep(ExecutableStep step, int depth, ImageFrame frame, ScreenPoint corner)
    {
        // The picture is kept under the name of the image result, and only there: a step that was
        // told not to hand the picture over leaves nothing for a later step to search in, which is
        // what leaving that result unticked means.
        var name = Give(step, "image", Value.FromText($"<image {frame.Width}x{frame.Height}>"));
        if (name.Length > 0)
        {
            _images[name] = frame;
        }

        GiveTaken(step);

        // Where the picture sits is one record beside the picture, so it can be handed to a search
        // region as it stands or read one part at a time. The middle is in it beside the corner:
        // that is the point a step aims at, and a macro that means the middle of what it just took
        // should not have to work it out of two halves of the rectangle.
        var middle = Middle(corner, new ScreenSize(frame.Width, frame.Height));
        Give(step, "place", Value.FromRecord(
        [
            ("x", Value.FromNumber(middle.X)),
            ("y", Value.FromNumber(middle.Y)),
            (FieldNames.Left, Value.FromNumber(corner.X)),
            (FieldNames.Top, Value.FromNumber(corner.Y)),
            (FieldNames.Width, Value.FromNumber(frame.Width)),
            (FieldNames.Height, Value.FromNumber(frame.Height)),
            ("box", Value.FromText($"{corner.X},{corner.Y},{frame.Width},{frame.Height}")),
        ]));

        Saw(step, LookKind.Capture, frame, corner,
            [new LookBox(new ImageMatch(0, corner, new ScreenSize(frame.Width, frame.Height)), LookRole.Area)],
            1, name);
        if (name.Length > 0)
        {
            Log(LogLevel.Info, depth, step.Type, LogKeys.Capture, name, $"{frame.Width}x{frame.Height}");
        }

        KeepFile(step, depth, frame);
    }

    /// <summary>
    /// Writes the picture out where the step asked for a file, when it asked for one: one step takes
    /// the picture and keeps it, rather than a capture followed by a step that writes what it left
    /// behind. It is always a PNG, which is the small end of what a picture can be written as, and a
    /// file of that name is replaced — the same answer every other step that writes a file gives.
    /// </summary>
    private void KeepFile(ExecutableStep step, int depth, ImageFrame frame)
    {
        var path = PathOf(step);
        if (path.Trim().Length == 0)
        {
            return;
        }

        _devices.Files.WriteBytes(path, PngWriter.Encode(frame));
        Log(LogLevel.Info, depth, step.Type, LogKeys.SavedPicture, path,
            $"{frame.Width}x{frame.Height}");
    }

    private void GetPixel(ExecutableStep step, int depth)
    {
        var (point, window) = Spot(step);
        var colour = PixelAt(step, point, window);
        var asHex = !string.Equals(ReadText(step, "asHex").Trim(), "false", StringComparison.OrdinalIgnoreCase);
        var name = Give(step, "color", asHex
            ? Value.FromText(colour.ToHex())
            : Value.FromNumber((colour.R) | (colour.G << 8) | (colour.B << 16)));

        SawPixel(step, point, colour.ToHex(), window);
        if (name.Length > 0)
        {
            Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, colour.ToHex());
        }
    }

    private async Task WaitColor(ExecutableStep step, int depth, CancellationToken token)
    {
        var target = PixelColor.Parse(ReadValue(step, "color").AsText());
        var tolerance = ReadValue(step, "tolerance").AsNumber();

        // The corner is looked up again on every check, so a wait that watches a window keeps
        // watching the same spot inside it even if the window is moved while the macro waits.
        var (point, window) = Spot(step);
        var interval = LookBeat(step);
        await WaitForFlagAsync(
            () => PixelAt(step).Matches(target, tolerance), interval, token);
        SawPixel(step, point, target.ToHex(), window);

        Log(LogLevel.Info, depth, step.Type, LogKeys.SawColor, target.ToHex());
    }

    /// <summary>
    /// Looks for a colour in the search areas and writes where it was found, once: what it does not
    /// find it leaves empty rather than waiting for, the way finding a picture does. A step that
    /// names a least share of the region — how full a bar has to be before it acts — counts as
    /// finding nothing until that much of the region shows the colour, and waiting on it is the
    /// job of <c>vision.waitColor</c> or of a wait-until block.
    /// </summary>
    private void FindColor(ExecutableStep step, int depth)
    {
        var target = PixelColor.Parse(ReadValue(step, "color").AsText());
        var tolerance = ReadValue(step, "tolerance").AsNumber();
        var least = ReadValue(step, "minCoverage").AsNumber() / 100;

        // The share is only measured when the step asks about it, either by handing it over or by
        // saying below which share it will not act: the walk it costs is otherwise wasted.
        var measured = least > 0 || step.Return("coverage").Length > 0;
        var pass = Scan(step, target, tolerance, measured);

        // The share is handed over whether or not the colour was found: a region the step will not
        // act on is exactly when the number is worth reading.
        if (pass.Coverage is { } share)
        {
            Give(step, "coverage", Value.FromNumber(Math.Round(share * 100, 1)));
        }

        var hits = pass.Enough(least) ? pass.Hits : [];
        var match = Chosen(hits, step);
        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, match.Score);
        }

        GiveEvery(step, HitRecords(hits));
        Log(LogLevel.Info, depth, step.Type, match is null ? LogKeys.ImageMissing : LogKeys.ImageFound,
            match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    /// <summary>
    /// One pass over the search areas, in reading order. A step that asks how much of the areas
    /// shows the colour — rather than only where it is — pays for a walk of the whole of them,
    /// which is why it is done for the steps that ask and for no others.
    /// </summary>
    private ColourPass Scan(ExecutableStep step, PixelColor target, double tolerance, bool measured)
    {
        var wanted = Wanted(step);
        var areas = SearchAreas(step);
        // Asked for by score, the pixels closest to the colour are the hits; the rest of the time
        // the first ones read left to right and top to bottom are, which is the cheaper walk.
        var surest = OrderOf(step) is MatchOrder.Score;
        var hits = new List<ImageMatch>();
        long matched = 0;
        long total = 0;
        foreach (var (area, origin) in areas)
        {
            foreach (var point in PixelSearch.Find(area, target, tolerance, wanted, surest))
            {
                hits.Add(new ImageMatch(
                    1 - area[point.X, point.Y].DistanceTo(target),
                    new ScreenPoint(origin.X + point.X, origin.Y + point.Y),
                    new ScreenSize(1, 1)));
            }

            if (measured)
            {
                matched += PixelSearch.Count(area, target, tolerance);
                total += (long)area.Width * area.Height;
            }
        }

        Sorted(hits, step);
        SawSearch(step, LookKind.Colour, areas, [.. hits.Select(hit => (hit, (string?)null))],
            WentWith(hits, step), target.ToHex());
        return new ColourPass(hits, measured && total > 0 ? (double)matched / total : null);
    }

    /// <summary>
    /// Whether enough of the search areas shows the colour, which is the question a bar, a gauge or
    /// a lamp is asked: not where the colour is but how much of the region it fills. The whole of
    /// every area is walked either way, so this is a thing to wait on rather than to ask in a tight
    /// loop. A least share of nothing asks only whether the colour is anywhere in the region, which
    /// is what a pixel-by-pixel condition cannot say.
    /// </summary>
    private bool ColourShare(ExecutableStep condition)
    {
        var target = PixelColor.Parse(ReadValue(condition, "color").AsText());
        var tolerance = ReadValue(condition, "tolerance").AsNumber();
        var least = ReadValue(condition, "minCoverage").AsNumber() / 100;
        return Scan(condition, target, tolerance, measured: true).Enough(least);
    }

    /// <summary>
    /// What one pass over the search areas came back with: where the colour was, and, for a step
    /// that asked about the share rather than the place, how much of the areas shows it.
    /// </summary>
    private sealed record ColourPass(List<ImageMatch> Hits, double? Coverage)
    {
        /// <summary>
        /// True when this pass is what the step was after: the colour is somewhere in the areas and,
        /// where a least share was named, enough of the areas shows it. A least share of zero is
        /// the step that only asked where the colour is.
        /// </summary>
        public bool Enough(double least)
            => Hits.Count > 0 && (Coverage is not { } share || share >= least);
    }

    /// <summary>
    /// Looks for a colour that has to have the colours the step names around it, and writes where
    /// it was found. One colour on its own says "something red is here", which a busy screen is
    /// full of; the colours around it say which red thing this is, which is how two buttons drawn
    /// alike are told apart without a branch for each.
    /// </summary>
    private void FindColours(ExecutableStep step, int depth)
    {
        var hits = PatternHits(step);
        var match = Chosen(hits, step);
        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, match.Score);
        }

        GiveEvery(step, HitRecords(hits));
        Log(LogLevel.Info, depth, step.Type, match is null ? LogKeys.ImageMissing : LogKeys.ImageFound,
            match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    /// <summary>
    /// One pass over the search areas: the pixels that show the pattern's own colour with every one
    /// of the colours around it where it was asked for, in reading order. Each hit is a single pixel
    /// — its centre is the pixel itself — and its score says how close the whole pattern came, one
    /// being exact. The colours that had to be around the pixel the step went with are marked beside
    /// it, which is what tells a step whose pattern is wrong from one that looked in the wrong place.
    /// </summary>
    private List<ImageMatch> PatternHits(ExecutableStep step)
    {
        var target = PixelColor.Parse(ReadValue(step, "color").AsText());
        var tolerance = ReadValue(step, "tolerance").AsNumber();
        var around = Pattern(step);

        var wanted = Wanted(step);
        var areas = SearchAreas(step);
        var hits = new List<ImageMatch>();
        foreach (var (area, origin) in areas)
        {
            foreach (var point in PixelSearch.FindPattern(area, target, tolerance, around, wanted))
            {
                hits.Add(new ImageMatch(
                    Closeness(area, point, target, around),
                    new ScreenPoint(origin.X + point.X, origin.Y + point.Y),
                    new ScreenSize(1, 1)));
            }
        }

        Sorted(hits, step);
        var taken = WentWith(hits, step);
        var marks = new List<(ImageMatch Match, string? Label)>(
            hits.Select(hit => (hit, (string?)null)));
        if (taken > 0)
        {
            var at = hits[taken - 1].Location;
            foreach (var colour in around)
            {
                marks.Add((
                    new ImageMatch(1, new ScreenPoint(at.X + colour.Dx, at.Y + colour.Dy),
                        new ScreenSize(1, 1)),
                    colour.Colour.ToHex()));
            }
        }

        SawSearch(step, LookKind.Colour, areas, marks, taken, target.ToHex());
        return hits;
    }

    /// <summary>
    /// The colours a pattern has to have around it. They are written the way the points of a
    /// multi-point colour comparison are — a line of two numbers and a colour — but the numbers are
    /// counted from the middle of the pattern rather than from the corner of the screen, because
    /// the whole point of a pattern is that it is found wherever it happens to be.
    /// </summary>
    private IReadOnlyList<ColourStep> Pattern(ExecutableStep step)
    {
        var text = ReadText(step, "points").Trim();
        var around = new List<ColourStep>();
        foreach (var entry in text.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dx)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dy)
                || !IsColour(parts[2]))
            {
                throw new StepFailure(RunKeys.BadPoints, entry);
            }

            around.Add(new ColourStep(dx, dy, PixelColor.Parse(parts[2])));
        }

        if (around.Count == 0)
        {
            throw new StepFailure(RunKeys.BadPoints, text);
        }

        return around;
    }

    /// <summary>
    /// How close the whole pattern came, from 0 to 1: the average of how close each of its colours
    /// is, the one in the middle included, so a pattern found on a faded screen scores lower than
    /// the same pattern on a fresh one.
    /// </summary>
    private static double Closeness(ImageFrame area, ScreenPoint at, PixelColor target,
        IReadOnlyList<ColourStep> around)
    {
        var total = area[at.X, at.Y].DistanceTo(target);
        foreach (var step in around)
        {
            total += area[at.X + step.Dx, at.Y + step.Dy].DistanceTo(step.Colour);
        }

        return 1 - (total / (around.Count + 1));
    }

    /// <summary>
    /// Waits for the picture to hold still: the screen counts as settled once every watched area
    /// has stayed within the tolerance of the frame that started the wait for a whole quiet
    /// stretch. A fixed pause is what this replaces, and it is wrong in both directions — too
    /// short and the step after it works on half a picture, too long and every run pays for the
    /// slowest case.
    /// </summary>
    private async Task WaitStable(ExecutableStep step, int depth, CancellationToken token)
    {
        var tolerance = ReadValue(step, "tolerance").AsNumber();
        var allowed = ReadValue(step, "changedPercent").AsNumber() / 100;
        var quiet = Math.Max(0, Number(step, "quietMs"));
        var interval = LookBeat(step);

        var started = Stopwatch.GetTimestamp();
        long Elapsed() => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var anchor = SearchAreas(step);
        var since = Elapsed();

        while (true)
        {
            await Pause(interval, token);
            var now = SearchAreas(step);
            if (Still(anchor, now, tolerance, allowed))
            {
                if (Elapsed() - since >= quiet)
                {
                    Log(LogLevel.Info, depth, step.Type, LogKeys.FrameStill, Elapsed());
                    return;
                }
            }
            else
            {
                // Counted from this frame on: what has to hold still for the quiet stretch is not
                // the picture the wait opened on, which may have been another screen altogether,
                // but the picture the screen has just arrived at.
                anchor = now;
                since = Elapsed();
            }
        }
    }

    /// <summary>
    /// True when every watched area is the picture it is compared against, as far as the step is
    /// willing to say so: no more of it than the step allows may have moved by more than its
    /// colour tolerance.
    /// </summary>
    private static bool Still(IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> before,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> after, double tolerance, double allowed)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        for (var index = 0; index < before.Count; index++)
        {
            if (ImageFrame.ChangedShare(before[index].Frame, after[index].Frame, tolerance) > allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Looks once and writes where the picture was, or an empty value when it was not.</summary>
    private void LookFor(ExecutableStep step, int depth)
    {
        var hits = Hits(step);
        var match = Chosen(hits, step);

        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, match.Score);
        }

        GiveEvery(step, HitRecords(hits));
        Log(LogLevel.Info, depth, step.Type, match is null ? LogKeys.ImageMissing : LogKeys.ImageFound,
            match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    private async Task WaitForImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var found = await HitsUntil(step, token);

        GiveMatch(step, found.Match.Location, found.Match.Size, found.Match.Score);
        GiveEvery(step, HitRecords(found.Hits));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ImageFound,
            $"{found.Match.Center.X},{found.Match.Center.Y}");
    }

    private async Task ClickImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var found = await HitsUntil(step, token);

        // A step that asked to look again waits a moment and confirms the picture is still there: a
        // button that flashed past, or a screen that has already moved on, is not clicked. The
        // second look is a single look rather than another wait: what is being asked is whether the
        // picture is still there, not whether it turns up again.
        var again = Pace(Number(step, "recheckMs"));
        if (again > 0)
        {
            await Pause(again, token);
            var hits = Hits(step);
            found = Chosen(hits, step) is { } match
                ? new Found(hits, match)
                : throw new StepFailure(RunKeys.ImageNotFound, Written(step.Rows("images")));
        }

        var at = Scattered(new ScreenPoint(
            found.Match.Center.X + Number(step, "offsetX"),
            found.Match.Center.Y + Number(step, "offsetY")), Spread(step));

        // Where it was found and every place it was found, the same as the step that only looks:
        // a step that clicks something is also a step whose position a later one may want.
        GiveMatch(step, found.Match.Location, found.Match.Size, found.Match.Score);
        GiveEvery(step, HitRecords(found.Hits));

        await ClickAt(step, at, token);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedImage, at.X, at.Y);
    }

    /// <summary>One attempt at finding a reference picture: the hit the step asked for.</summary>
    private ImageMatch? Search(ExecutableStep step) => Chosen(Hits(step), step);

}
