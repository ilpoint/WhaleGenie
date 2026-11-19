using System;

namespace WhaleGenie.Runtime;

/// <summary>
/// How often each kind of watch looks. The intervals are different because the costs are: a pixel
/// is a screen read, a program count is a process list, and a window listing is a title plus a
/// lookup per window.
/// </summary>
internal static class TriggerPace
{
    /// <summary>A watched pixel, and the clock a schedule is compared against.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// The window list, when the machine is not saying when a window changes. Half a second late is
    /// soon enough for the fallback; the notice path reads there and then.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Counting the running programs. A program that starts and quits inside the gap between two
    /// counts is one a trigger never notices, which is why this is the shortest of the three.
    /// </summary>
    public static readonly TimeSpan Program = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// Reading the controller. Windows has no word for a button going down, so a press that starts
    /// and ends between two readings is a press nothing saw.
    /// </summary>
    public static readonly TimeSpan Pad = TimeSpan.FromMilliseconds(30);

    /// <summary>A breather between the passes of a macro that repeats, so it cannot spin.</summary>
    public const int RepeatGapMs = 10;
}
