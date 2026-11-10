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

// Choosing one of several hits, and reading which one a step asked for.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The hits, once there are enough of them. How long that may take is the step's own timeout
    /// and nothing else: the wait ends where the clock does, and the step it belongs to is what
    /// fails, retries or gives up from there.
    /// </summary>
    private async Task<Found> HitsUntil(ExecutableStep step, CancellationToken token)
    {
        var interval = LookBeat();

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
    private int Wanted(ExecutableStep step)
    {
        if (Wants(step, "every") || CountsFromTheFarSide(step))
        {
            return MatchLimit;
        }

        return Index(step, "matchIndex", 1);
    }

    /// <summary>
    /// True while the step counts its hits from a corner other than the top left. "The third one"
    /// then means the third from that corner, which cannot be known until every hit has been read:
    /// stopping at three would count them from the wrong end and pick the wrong one.
    /// </summary>
    private static bool CountsFromTheFarSide(ExecutableStep step) => OrderOf(step) is
        MatchOrder.ReadingUp or MatchOrder.ReadingBack or MatchOrder.ReadingBackUp;

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
}
