using WhaleGenie.Core.Execution;

namespace WhaleGenie.Localization;

/// <summary>
/// One line of a run log worded for whoever reads it. Both hosts that carry a run's words — the one
/// behind the run window and the one behind a run a trigger started — word their lines here, so a
/// line shown in the window and the same line written into a failed run's file read alike.
/// </summary>
public static class RunLogText
{
    public static string Of(LogEntry entry)
        => entry.Arguments.Count == 0
            ? Strings.Get(entry.Key)
            : Strings.Format(entry.Key, [.. entry.Arguments]);
}
