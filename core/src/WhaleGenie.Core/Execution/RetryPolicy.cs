using System;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// When a step is tried again: how many tries its settings allow, how long the pause before one is,
/// and whether the time left can pay for it. Every answer is read from the settings alone, so what a
/// retry will cost can be shown before a run and checked without a device.
/// </summary>
internal static class RetryPolicy
{
    /// <summary>True while the step's own count leaves another try.</summary>
    public static bool Allows(StepMeta meta, int attempt) => attempt < meta.RetryCount;

    /// <summary>
    /// The pause before the given retry, counting the first retry as 1, at the speed the run was
    /// started with. A pause that the speed writes down to nothing is no pause rather than a round
    /// to zero: the same rule the other waits use.
    /// </summary>
    public static int PauseMs(StepMeta meta, int attempt, double scale = 1, Random? random = null)
    {
        var pause = meta.RetryDelayFor(attempt, random);
        return pause <= 0 ? 0 : (int)Math.Round(pause * scale);
    }

    /// <summary>
    /// True when the time left can pay for the pause and a try. No budget means no limit; a pause
    /// that uses the whole of what is left is refused rather than waited out, because a try nobody
    /// could start is a failure that arrived as a pause.
    /// </summary>
    public static bool CanAfford(int pauseMs, double? remainingMs)
        => remainingMs is not { } left || left > pauseMs;
}
