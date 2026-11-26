namespace WhaleGenie.Core.Execution;

/// <summary>
/// The numbers a counting loop runs over: where it starts, where it ends and what it moves by.
/// </summary>
internal readonly record struct CountRange(int From, int To, int Stride);

/// <summary>
/// The bounds of a counting loop, read from its three fields alone. A step of zero takes its
/// direction from where the loop starts and where it ends, which is what "count from 1 to 10" meant
/// before a step could be written down at all; a step that moves away from the end is a loop with
/// nothing to do rather than a run that never finishes.
/// </summary>
internal static class CountPlan
{
    public static CountRange Of(int from, int to, int step)
        => step != 0 ? new CountRange(from, to, step) : new CountRange(from, to, to >= from ? 1 : -1);

    /// <summary>True while the counter has not passed the end it counts to.</summary>
    public static bool Covers(CountRange range, int value)
        => range.Stride > 0 ? value <= range.To : value >= range.To;
}
