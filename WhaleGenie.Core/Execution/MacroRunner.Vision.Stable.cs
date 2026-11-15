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

// Waiting for the screen to stop moving, and looking once at what a step asks for.
public sealed partial class MacroRunner
{
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
        var interval = LookBeat();

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
                    if (now.FirstOrDefault() is { Frame: { } frame, Origin: { } origin })
                    {
                        GivePoints(step,
                            Middle(origin, new ScreenSize(frame.Width, frame.Height)), null);
                    }

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
}
