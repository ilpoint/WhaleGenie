using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One turn of the wheel in a smooth scroll: how many units it sends, and the pause before it. The
/// first turn has no pause — it is where the gesture starts.
/// </summary>
internal readonly record struct WheelTurn(int Units, int PauseBeforeMs);

/// <summary>
/// How one scroll is split over the wheel events that send it. A scroll with no duration is one
/// event; a smooth one is the same total split over several, with a beat between them so an
/// application that animates its scrolling has the time to follow. The split is worked out here
/// rather than where the events are sent, so a step can be read without a device and the numbers can
/// be checked without waiting.
/// </summary>
internal static class WheelPlan
{
    /// <summary>
    /// One event per 15 ms is about 66 a second: roughly the fastest an application would draw its
    /// scrolling at, and never more events than there are units to send.
    /// </summary>
    private const int MsPerEvent = 15;

    public static IReadOnlyList<WheelTurn> Of(int total, int smoothMs)
    {
        if (total <= 0)
        {
            return [];
        }

        if (smoothMs <= 0)
        {
            return [new WheelTurn(total, 0)];
        }

        var events = Math.Clamp(smoothMs / MsPerEvent, 1, total);
        var share = total / events;
        var extra = total % events;
        var pause = smoothMs / events;

        var turns = new List<WheelTurn>(events);
        for (var index = 0; index < events; index++)
        {
            turns.Add(new WheelTurn(share + (index < extra ? 1 : 0), index > 0 ? pause : 0));
        }

        return turns;
    }
}
