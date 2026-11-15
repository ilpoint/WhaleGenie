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

// Colours: waiting for one, matching one, finding one, and the scan behind them.
public sealed partial class MacroRunner
{
    private async Task WaitColor(ExecutableStep step, int depth, CancellationToken token)
    {
        var target = PixelColor.Parse(Args(step).ValueOf("color").AsText());
        var tolerance = Args(step).ValueOf("tolerance").AsNumber();

        // The corner is looked up again on every check, so a wait that watches a window keeps
        // watching the same spot inside it even if the window is moved while the macro waits.
        var (point, window) = Spot(step);
        var interval = LookBeat();
        await WaitForFlagAsync(
            () => PixelAt(step).Matches(target, tolerance), interval, token);
        (point, window) = Spot(step);
        GivePointAt(step, point, window);
        SawPixel(step, point, target.ToHex(), window);

        Log(LogLevel.Info, depth, step.Type, LogKeys.SawColor, target.ToHex());
    }

    /// <summary>
    /// Fails the step for a miss when the step asked for it. A colour that is not there is normally
    /// an answer — <c>found = false</c>, which the next step branches on — and most macros want to
    /// read it that way. The ones that only make sense when the colour is there say so, and get the
    /// failure key a branch can catch instead of every author writing the same condition after the
    /// step. The waiting and clicking actions never needed the switch: they fail on their own.
    /// </summary>
    private void MightHaveMissed(ExecutableStep step, string detail = "")
    {
        if (!Args(step).ValueOf("missingIsFailure").AsBool())
        {
            return;
        }

        throw detail.Length == 0
            ? new StepFailure(RunKeys.ColourNotFound)
            : new StepFailure(RunKeys.ColourNotFound, detail);
    }

    /// <summary>
    /// Whether one named pixel shows a colour, right now. This is the question "is the light on",
    /// asked of a place that does not move: there is no region to search and nothing to wait for.
    /// It hands back what it saw as well as whether it matched, because the answer people want when
    /// it does not match is "so what colour is it".
    /// </summary>
    private void MatchColor(ExecutableStep step, int depth)
    {
        var target = PixelColor.Parse(Args(step).ValueOf("color").AsText());
        var tolerance = Args(step).ValueOf("tolerance").AsNumber();
        var (point, window) = Spot(step);
        var colour = PixelAt(step, point, window);
        var matched = colour.Matches(target, tolerance);

        Give(step, "matches", Value.FromBool(matched));
        Give(step, "color", Value.FromText(colour.ToHex()));
        Give(step, "delta", Value.FromNumber(Math.Round(colour.DistanceTo(target) * 100, 1)));
        SawPixel(step, point, colour.ToHex(), window);
        Log(LogLevel.Info, depth, step.Type,
            matched ? LogKeys.SawColor : LogKeys.ImageMissing, colour.ToHex());
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
        var target = PixelColor.Parse(Args(step).ValueOf("color").AsText());
        var tolerance = Args(step).ValueOf("tolerance").AsNumber();
        var least = Args(step).ValueOf("minCoverage").AsNumber() / 100;

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
            MightHaveMissed(step, target.ToHex());
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
        var target = PixelColor.Parse(Args(condition).ValueOf("color").AsText());
        var tolerance = Args(condition).ValueOf("tolerance").AsNumber();
        var least = Args(condition).ValueOf("minCoverage").AsNumber() / 100;
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
}
