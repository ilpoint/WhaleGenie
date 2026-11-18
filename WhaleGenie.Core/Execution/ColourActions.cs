using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Colours: reading one pixel, waiting for a colour to appear, finding it anywhere in the areas a
/// step named, and finding a whole pattern of colours. A miss is an answer rather than a failure
/// unless the step says otherwise, which is what lets the next step branch on it.
/// </summary>
internal static class ColourActions
{
    /// <summary>
    /// The colour actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["vision.matchColor"] = (run, step, depth, _) =>
            {
                run.Reads.Showing(() => MatchColor(run, step, depth));
                return Task.FromResult(Signal.Normal);
            },

            ["vision.getPixel"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => GetPixel(run, step, depth));
                return Signal.Normal;
            },

            ["vision.waitColor"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await run.Reads.Showing(() => WaitColor(run, step, depth, token));
                return Signal.Normal;
            },

            ["vision.findColor"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => FindColor(run, step, depth));
                return Signal.Normal;
            },

            ["vision.findColors"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => FindColours(run, step, depth));
                return Signal.Normal;
            },

            ["vision.clickColor"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await run.Reads.Showing(() => ClickColor(run, step, depth, token));
                return Signal.Normal;
            },
        };

    /// <summary>
    /// One look for the trial window: what the step would see right now, without waiting, clicking
    /// or writing a result.
    /// </summary>
    internal static void Preview(IRunContext run, ExecutableStep step)
    {
        switch (step.Type)
        {
            case "vision.findColors":
                run.Reads.Showing(() => PatternHits(run, step));
                break;

            case "vision.matchColor":
            case "vision.getPixel":
                run.Reads.Showing(() =>
                {
                    var (point, window) = run.Shots.Spot(step);
                    run.Shots.SawPixel(step, point,
                        run.Shots.At(step, point, window).ToHex(), window);
                });
                break;

            default:
                run.Reads.Showing(() =>
                {
                    var args = run.Args(step);
                    Scan(run, step, PixelColor.Parse(args.ValueOf("color").AsText()),
                        args.ValueOf("tolerance").AsNumber(), measured: false);
                });
                break;
        }
    }

    /// <summary>What one named pixel shows, right now.</summary>
    private static void GetPixel(IRunContext run, ExecutableStep step, int depth)
    {
        var (point, window) = run.Shots.Spot(step);
        var colour = run.Shots.At(step, point, window);
        run.Points.GivePointAt(step, point, window);

        // The same colour is handed over in both readings the action has always had: the text a
        // person reads off a colour picker and the whole number a macro can compare. Neither is a
        // switch any more, so a step that ticks one does not have to know how the other is spelled.
        run.Give(step, "color", Value.FromText(colour.ToHex()));
        run.Give(step, "number", Value.FromNumber((colour.R) | (colour.G << 8) | (colour.B << 16)));

        run.Shots.SawPixel(step, point, colour.ToHex(), window);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.SawColor, colour.ToHex());
    }

    private static async Task WaitColor(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var args = run.Args(step);
        var target = PixelColor.Parse(args.ValueOf("color").AsText());
        var tolerance = args.ValueOf("tolerance").AsNumber();

        // The corner is looked up again on every check, so a wait that watches a window keeps
        // watching the same spot inside it even if the window is moved while the macro waits.
        while (!Reading(run, step).Matches(target, tolerance))
        {
            await run.Reads.WaitForScreen(token);
        }

        var (point, window) = run.Shots.Spot(step);
        run.Points.GivePointAt(step, point, window);
        run.Shots.SawPixel(step, point, target.ToHex(), window);

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.SawColor, target.ToHex());
    }

    /// <summary>The colour of the pixel a step points at, read on the spot it names each time.</summary>
    private static PixelColor Reading(IRunContext run, ExecutableStep step)
    {
        var (point, window) = run.Shots.Spot(step);
        return run.Shots.At(step, point, window);
    }

    /// <summary>
    /// Whether one named pixel shows a colour, right now. This is the question "is the light on",
    /// asked of a place that does not move: there is no region to search and nothing to wait for.
    /// It hands back what it saw as well as whether it matched, because the answer people want when
    /// it does not match is "so what colour is it".
    /// </summary>
    private static void MatchColor(IRunContext run, ExecutableStep step, int depth)
    {
        var args = run.Args(step);
        var target = PixelColor.Parse(args.ValueOf("color").AsText());
        var tolerance = args.ValueOf("tolerance").AsNumber();
        var (point, window) = run.Shots.Spot(step);
        var colour = run.Shots.At(step, point, window);
        var matched = colour.Matches(target, tolerance);

        run.Give(step, "matches", Value.FromBool(matched));
        run.Give(step, "color", Value.FromText(colour.ToHex()));
        run.Give(step, "delta", Value.FromNumber(Math.Round(colour.DistanceTo(target) * 100, 1)));
        run.Shots.SawPixel(step, point, colour.ToHex(), window);
        run.Log(LogLevel.Info, depth, step.Type,
            matched ? LogKeys.SawColor : LogKeys.ImageMissing, colour.ToHex());
    }

    /// <summary>
    /// Looks for a colour in the search areas and writes where it was found, once: what it does not
    /// find it leaves empty rather than waiting for, the way finding a picture does. A step that
    /// names a least share of the region — how full a bar has to be before it acts — counts as
    /// finding nothing until that much of the region shows the colour, and waiting on it is the
    /// job of <c>vision.waitColor</c> or of a wait-until block.
    /// </summary>
    private static void FindColor(IRunContext run, ExecutableStep step, int depth)
    {
        var args = run.Args(step);
        var target = PixelColor.Parse(args.ValueOf("color").AsText());
        var tolerance = args.ValueOf("tolerance").AsNumber();
        var least = args.ValueOf("minCoverage").AsNumber() / 100;

        // The share is only measured when the step asks about it, either by handing it over or by
        // saying below which share it will not act: the walk it costs is otherwise wasted.
        var measured = least > 0 || step.Return("coverage").Length > 0;
        var pass = Scan(run, step, target, tolerance, measured);

        // The share is handed over whether or not the colour was found: a region the step will not
        // act on is exactly when the number is worth reading.
        if (pass.Coverage is { } share)
        {
            run.Give(step, "coverage", Value.FromNumber(Math.Round(share * 100, 1)));
        }

        var hits = pass.Enough(least) ? pass.Hits : [];
        var match = SearchHits.Choose(run, hits, step);
        if (match is null)
        {
            MightHaveMissed(run, step, target.ToHex());
        }

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

    /// <summary>
    /// Looks for a colour that has to have the colours the step names around it, and writes where
    /// it was found. One colour on its own says "something red is here", which a busy screen is
    /// full of; the colours around it say which red thing this is, which is how two buttons drawn
    /// alike are told apart without a branch for each.
    /// </summary>
    private static void FindColours(IRunContext run, ExecutableStep step, int depth)
    {
        var hits = PatternHits(run, step);
        var match = SearchHits.Choose(run, hits, step);
        if (match is null)
        {
            MightHaveMissed(run, step);
        }

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

    /// <summary>
    /// Looks for a colour in the search areas and clicks it once it is there, waiting for it the way
    /// clicking a picture waits. The colour actions that only look hand a miss back as an answer and
    /// leave the branch to say what to do about it; this one is the step that has to work, so a
    /// colour that never turns up fails with the colour's own key, and a run that catches it reads
    /// the same name the waiting colour action reports.
    /// </summary>
    private static async Task ClickColor(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var args = run.Args(step);
        var target = PixelColor.Parse(args.ValueOf("color").AsText());
        var tolerance = args.ValueOf("tolerance").AsNumber();
        var least = args.ValueOf("minCoverage").AsNumber() / 100;

        // Enough of the region showing the colour is not the same question as the hit the step
        // asked for being there: a step that asks for the second hit of a colour that is in one
        // place has not found what it is waiting for, and must keep waiting rather than click
        // whatever happened to be first.
        ColourPass? Present()
        {
            var pass = Scan(run, step, target, tolerance, least > 0);
            return pass.Enough(least) && SearchHits.Choose(run, pass.Hits, step) is not null
                ? pass
                : null;
        }

        ColourPass? present;
        while ((present = Present()) is null)
        {
            await run.Reads.WaitForScreen(token);
        }

        var match = SearchHits.Choose(run, present.Hits, step)!;

        // The click aims at the middle of what was found, moved by the offset the step gives, so a
        // click can land beside the colour it recognised rather than on it.
        var at = run.Points.Scatter(step, new ScreenPoint(
            match.Center.X + args.Number("offsetX"),
            match.Center.Y + args.Number("offsetY")));

        // Where it was found and everywhere it was found, as the searching action hands them over:
        // a step that clicks something is also a step whose position a later one may want.
        run.GiveMatch(step, match.Location, match.Size, match.Score);
        run.GiveEvery(step, HitRecord.Each(present.Hits));

        run.Reads.ClickAt(step, at);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedColour, at.X, at.Y);
    }

    /// <summary>
    /// One pass over the search areas, in reading order. A step that asks how much of the areas
    /// shows the colour — rather than only where it is — pays for a walk of the whole of them,
    /// which is why it is done for the steps that ask and for no others.
    /// </summary>
    internal static ColourPass Scan(IRunContext run, ExecutableStep step, PixelColor target,
        double tolerance, bool measured)
    {
        var wanted = SearchHits.Wanted(run, step);
        var areas = run.Reads.Areas(step);
        // Asked for by score, the pixels closest to the colour are the hits; the rest of the time
        // the first ones read left to right and top to bottom are, which is the cheaper walk.
        var surest = SearchHits.OrderOf(step) is MatchOrder.Score;
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

        SearchHits.Sort(hits, step);
        run.Reads.SawSearch(step, LookKind.Colour, areas,
            [.. hits.Select(hit => (hit, (string?)null))],
            SearchHits.WentWith(run, hits, step), target.ToHex());
        return new ColourPass(hits, measured && total > 0 ? (double)matched / total : null);
    }

    /// <summary>
    /// One pass over the search areas: the pixels that show the pattern's own colour with every one
    /// of the colours around it where it was asked for, in reading order. Each hit is a single pixel
    /// — its centre is the pixel itself — and its score says how close the whole pattern came, one
    /// being exact. The colours that had to be around the pixel the step went with are marked beside
    /// it, which is what tells a step whose pattern is wrong from one that looked in the wrong place.
    /// </summary>
    internal static List<ImageMatch> PatternHits(IRunContext run, ExecutableStep step)
    {
        var args = run.Args(step);
        var target = PixelColor.Parse(args.ValueOf("color").AsText());
        var tolerance = args.ValueOf("tolerance").AsNumber();
        var around = Pattern(run, step);

        var wanted = SearchHits.Wanted(run, step);
        var areas = run.Reads.Areas(step);
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

        SearchHits.Sort(hits, step);
        var taken = SearchHits.WentWith(run, hits, step);
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

        run.Reads.SawSearch(step, LookKind.Colour, areas, marks, taken, target.ToHex());
        return hits;
    }

    /// <summary>
    /// The colours a pattern has to have around it: one row per colour, each saying where it sits
    /// relative to the base colour and which colour has to be there. The numbers are counted from
    /// the middle of the pattern rather than from the corner of the screen, because the whole point
    /// of a pattern is that it is found wherever it happens to be. A row is a place in the pattern,
    /// so a pattern with nothing in it is a step that would match everywhere and is refused: it is
    /// a mistake in the step rather than an answer about the screen.
    /// </summary>
    internal static IReadOnlyList<ColourStep> Pattern(IRunContext run, ExecutableStep step)
    {
        var rows = step.Rows("points");
        if (rows.Count == 0)
        {
            // An empty pattern is not a pattern with something unreadable in it: it is the step
            // that would match the base colour anywhere, which is what the plain colour search is
            // for. It says so rather than borrowing the key about a row that cannot be read.
            throw new StepFailure(RunKeys.NoColourPoints);
        }

        var around = new List<ColourStep>(rows.Count);
        foreach (var row in rows)
        {
            var dx = run.Cell(row, "dx");
            var dy = run.Cell(row, "dy");
            var colour = run.Cell(row, "colour");
            var entry = $"{dx.AsText()},{dy.AsText()},{colour.AsText()}";
            if (dx.AsText().Length == 0 || dy.AsText().Length == 0 || colour.AsText().Length == 0)
            {
                throw new StepFailure(RunKeys.BadPoints, entry);
            }

            // One place may allow more than one colour, written one after another with a bar
            // between them: the same button drawn light in one state and dark in another is one
            // place, not two patterns. A place that says it must not be these is satisfied by
            // anything else, which is how a pattern says "this corner is empty".
            var palette = colour.AsText()
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => PatternColour(token, entry))
                .ToList();
            if (palette.Count == 0)
            {
                throw new StepFailure(RunKeys.BadPoints, entry);
            }

            // A cell may be a number, a variable or a sum: all of them are already the value the
            // shape came to. Nothing here spells a value out into a line to be read back.
            around.Add(new ColourStep(PatternOffset(dx, entry), PatternOffset(dy, entry),
                palette[0], PatternTolerance(run.Cell(row, "tolerance"), entry),
                palette.Count > 1 ? [.. palette[1..]] : null,
                string.Equals(run.Word(row, "mustNot"), "true", StringComparison.OrdinalIgnoreCase)));
        }

        return around;
    }

    /// <summary>
    /// How far this one colour may sit from what was written, or nothing when the row leaves it to
    /// the search as a whole. An anchor that has to be exact often sits next to shapes whose shading
    /// drifts; one number for both is then either too loose for the anchor or too tight for the
    /// rest, which is why the step may say a different number per point.
    /// </summary>
    private static double? PatternTolerance(Value value, string entry)
    {
        var token = value.AsText().Trim();
        if (token.Length == 0)
        {
            return null;
        }

        var percent = value.IsNumber
            ? value.AsNumber()
            : double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var written)
                ? written
                : double.NaN;

        return double.IsNaN(percent)
            ? throw new StepFailure(RunKeys.BadPoints, entry)
            : Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// One offset of a pattern. It is written out more often than not, and that is read straight;
    /// anything else is a value — a variable, or a sum of them — worked out here, so a pattern can be
    /// moved by a number another step produced instead of being retyped.
    /// </summary>
    private static int PatternOffset(Value value, string entry)
    {
        if (value.IsNumber)
        {
            return (int)Math.Round(value.AsNumber(), MidpointRounding.AwayFromZero);
        }

        return int.TryParse(value.AsText().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var number)
            ? number
            : throw new StepFailure(RunKeys.BadPoints, entry);
    }

    /// <summary>One colour of a pattern. The value has already been worked out by the time it is here.</summary>
    private static PixelColor PatternColour(string token, string entry)
        => PixelColor.TryParse(token, out var colour)
            ? colour
            : throw new StepFailure(RunKeys.BadPoints, entry);

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
    /// Fails the step for a miss when the step asked for it. A colour that is not there is normally
    /// an answer — <c>found = false</c>, which the next step branches on — and most macros want to
    /// read it that way. The ones that only make sense when the colour is there say so, and get the
    /// failure key a branch can catch instead of every author writing the same condition after the
    /// step. The waiting and clicking actions never needed the switch: they fail on their own.
    /// </summary>
    private static void MightHaveMissed(IRunContext run, ExecutableStep step, string detail = "")
    {
        if (!run.Args(step).ValueOf("missingIsFailure").AsBool())
        {
            return;
        }

        throw detail.Length == 0
            ? new StepFailure(RunKeys.ColourNotFound)
            : new StepFailure(RunKeys.ColourNotFound, detail);
    }

    /// <summary>
    /// What one pass over the search areas came back with: where the colour was, and, for a step
    /// that asked about the share rather than the place, how much of the areas shows it.
    /// </summary>
    internal sealed record ColourPass(List<ImageMatch> Hits, double? Coverage)
    {
        /// <summary>
        /// True when this pass is what the step was after: the colour is somewhere in the areas and,
        /// where a least share was named, enough of the areas shows it. A least share of zero is
        /// the step that only asked where the colour is.
        /// </summary>
        public bool Enough(double least)
            => Hits.Count > 0 && (Coverage is not { } share || share >= least);
    }
}
