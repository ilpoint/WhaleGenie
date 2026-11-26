namespace WhaleGenie.Core.Execution;

/// <summary>
/// Whether a break or a continue has something to leave. A break also has a switch to leave; a
/// continue only ever means a loop. With nothing above it there is nowhere to go, and the step is
/// refused rather than quietly ending the macro where it stands — a run that reports success after
/// skipping the rest of a macro is a lie the user cannot see.
/// </summary>
internal static class LoopControl
{
    /// <summary>True when the signal has a block above it that it can act on.</summary>
    public static bool HasSomethingToLeave(Signal signal, int loops, int switches)
        => signal is Signal.Break ? loops > 0 || switches > 0 : loops > 0;

    /// <summary>The failure key a break or a continue reports when there is nothing above it.</summary>
    public static string FailureKey(Signal signal)
        => signal is Signal.Break ? RunKeys.BreakOutsideLoop : RunKeys.ContinueOutsideLoop;
}
