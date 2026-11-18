using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// What a breakpoint waits for, written as what the person meant rather than as a step: how many
/// times the run has to arrive here, and an expression that has to hold. A breakpoint with nothing
/// set stops every time, which is the shape it starts in.
/// </summary>
public sealed record BreakpointRule
{
    /// <summary>The expression that has to hold, or empty when the breakpoint asks nothing.</summary>
    public string Expression { get; init; } = string.Empty;

    /// <summary>How many arrivals count before it stops; 1 means every time.</summary>
    public int HitTimes { get; init; } = 1;

    /// <summary>True while this breakpoint asks a question about the values in play.</summary>
    public bool WaitsForExpression => Expression.Trim().Length > 0;

    /// <summary>True while this breakpoint skips the first few arrivals.</summary>
    public bool WaitsForHits => HitTimes > 1;

    /// <summary>True when the breakpoint stops the moment the run reaches it.</summary>
    public bool Always => !WaitsForExpression && !WaitsForHits;

    /// <summary>The rule as the row says it, or empty when the breakpoint stops every time.</summary>
    public string Note => (WaitsForHits, WaitsForExpression) switch
    {
        (true, true) => Strings.Format("Run.BreakpointNoteBoth", HitTimes, Expression.Trim()),
        (true, false) => Strings.Format("Run.BreakpointNoteHits", HitTimes),
        (false, true) => Expression.Trim(),
        _ => string.Empty,
    };
}
