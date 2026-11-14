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

// A pattern of colours: reading the rows, the offsets between them, and how close is close enough.
public sealed partial class MacroRunner
{
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
            MightHaveMissed(step);
        }

        Give(step, "found", Value.FromBool(match is not null));
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
    /// The colours a pattern has to have around it: one row per colour, each saying where it sits
    /// relative to the base colour and which colour has to be there. The numbers are counted from
    /// the middle of the pattern rather than from the corner of the screen, because the whole point
    /// of a pattern is that it is found wherever it happens to be. A row is a place in the pattern,
    /// so a pattern with nothing in it is a step that would match everywhere and is refused: it is
    /// a mistake in the step rather than an answer about the screen.
    /// </summary>
    private IReadOnlyList<ColourStep> Pattern(ExecutableStep step)
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
            var dx = CellValue(row, "dx");
            var dy = CellValue(row, "dy");
            var colour = CellValue(row, "colour");
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
                palette[0], PatternTolerance(row, entry),
                palette.Count > 1 ? [.. palette[1..]] : null, MustNotHold(row)));
        }

        return around;
    }

    /// <summary>Whether this place is asked to be none of the colours it names, rather than one of them.</summary>
    private static bool MustNotHold(IReadOnlyDictionary<string, ExpressionNode> row)
        => string.Equals(Word(row, "mustNot"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How far this one colour may sit from what was written, or nothing when the row leaves it to
    /// the search as a whole. An anchor that has to be exact often sits next to shapes whose shading
    /// drifts; one number for both is then either too loose for the anchor or too tight for the
    /// rest, which is why the step may say a different number per point.
    /// </summary>
    private double? PatternTolerance(IReadOnlyDictionary<string, ExpressionNode> row, string entry)
    {
        var value = CellValue(row, "tolerance");
        var token = value.AsText().Trim();
        if (token.Length == 0)
        {
            return null;
        }

        var percent = value.IsNumber
            ? value.AsNumber()
            : double.TryParse(value.AsText().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
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
        => IsColour(token)
            ? PixelColor.Parse(token)
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
}
