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

// Waiting for a picture or a colour to appear, and clicking what turned up.
public sealed partial class MacroRunner
{
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
        var again = Pace(Args(step).Number("confirmMs"));
        if (again > 0)
        {
            await Pause(again, token);
            var hits = Hits(step);
            found = Chosen(hits, step) is { } match
                ? new Found(hits, match)
                : throw new StepFailure(RunKeys.ImageNotFound, Written(step.Rows("images")));
        }

        var at = Scattered(new ScreenPoint(
            found.Match.Center.X + Args(step).Number("offsetX"),
            found.Match.Center.Y + Args(step).Number("offsetY")), Spread(step));

        // Where it was found and every place it was found, the same as the step that only looks:
        // a step that clicks something is also a step whose position a later one may want.
        GiveMatch(step, found.Match.Location, found.Match.Size, found.Match.Score);
        GiveEvery(step, HitRecords(found.Hits));

        ClickAt(step, at);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedImage, at.X, at.Y);
    }

    /// <summary>
    /// Looks for a colour in the search areas and clicks it once it is there, waiting for it the way
    /// clicking a picture waits. The colour actions that only look hand a miss back as an answer and
    /// leave the branch to say what to do about it; this one is the step that has to work, so a
    /// colour that never turns up fails with the colour's own key, and a run that catches it reads
    /// the same name the waiting colour action reports.
    /// </summary>
    private async Task ClickColor(ExecutableStep step, int depth, CancellationToken token)
    {
        var target = PixelColor.Parse(Args(step).ValueOf("color").AsText());
        var tolerance = Args(step).ValueOf("tolerance").AsNumber();
        var least = Args(step).ValueOf("minCoverage").AsNumber() / 100;
        var interval = LookBeat();

        // Enough of the region showing the colour is not the same question as the hit the step
        // asked for being there: a step that asks for the second hit of a colour that is in one
        // place has not found what it is waiting for, and must keep waiting rather than click
        // whatever happened to be first.
        ColourPass? Present()
        {
            var pass = Scan(step, target, tolerance, least > 0);
            return pass.Enough(least) && Chosen(pass.Hits, step) is not null ? pass : null;
        }

        var found = await WaitForValueAsync(Present, interval, token);
        var match = Chosen(found.Hits, step)!;

        // The click aims at the middle of what was found, moved by the offset the step gives, so a
        // click can land beside the colour it recognised rather than on it.
        var at = Scattered(new ScreenPoint(
            match.Center.X + Args(step).Number("offsetX"),
            match.Center.Y + Args(step).Number("offsetY")), Spread(step));

        // Where it was found and everywhere it was found, as the searching action hands them over:
        // a step that clicks something is also a step whose position a later one may want.
        GiveMatch(step, match.Location, match.Size, match.Score);
        GiveEvery(step, HitRecords(found.Hits));

        ClickAt(step, at);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedColour, at.X, at.Y);
    }

    /// <summary>One attempt at finding a reference picture: the hit the step asked for.</summary>
    private ImageMatch? Search(ExecutableStep step) => Chosen(Hits(step), step);
}
