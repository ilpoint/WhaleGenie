using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Waiting, in the three shapes a macro needs it: a pause between two steps, a question asked
/// again until it holds, and a wait for something that turns up rather than becomes true. The
/// pacing is written once here so a family does not have to reach into the runner to wait, and so
/// the timeout rule reads the same wherever a step waits.
/// </summary>
internal static class Pacing
{
    /// <summary>Waits, or does nothing when the step asked for no wait.</summary>
    public static async Task Pause(int milliseconds, CancellationToken token)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking every <paramref name="interval"/> and giving up
    /// after <paramref name="timeoutMs"/>. Zero means no limit of its own: the caller's token is
    /// then what ends the wait. The question is asked before the clock is read, so a wait that
    /// already holds costs nothing.
    /// </summary>
    public static async Task<bool> WaitForFlagAsync(Func<bool> ready, int timeoutMs, int interval,
        CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (ready())
            {
                return true;
            }

            if (timeoutMs > 0
                && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return false;
            }

            await Pause(interval, token);
        }
    }

    /// <summary>The same wait, for something that turns up rather than becomes true.</summary>
    public static async Task<T?> WaitForValueAsync<T>(Func<T?> probe, int timeoutMs, int interval,
        CancellationToken token)
        where T : class
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            if (timeoutMs > 0
                && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return null;
            }

            await Pause(interval, token);
        }
    }

    /// <summary>
    /// The same wait for something that turns up, with the step's own token as the only end: a
    /// click that waits for a line of text does not know how long the line will take.
    /// </summary>
    public static async Task<T?> WaitForValueAsync<T>(Func<T?> probe, int interval,
        CancellationToken token)
        where T : class
    {
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await Pause(interval, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking every <paramref name="interval"/> until it does.
    /// How long that may take is not the wait's to say: the step's own token is what ends it, which
    /// is what makes the length of a wait a setting of the step rather than a second field.
    /// </summary>
    public static async Task WaitForFlagAsync(Func<bool> ready, int interval,
        CancellationToken token)
    {
        while (!ready())
        {
            await Pause(interval, token);
        }
    }
}
