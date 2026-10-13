using System.Collections.Generic;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One value a step worked out while it ran.
/// </summary>
/// <remarks>
/// <paramref name="Key"/> is the name the engine knows the result by and <paramref name="Name"/> is
/// the name the macro keeps it under — empty when nobody ticked that result, which is the case a
/// debugger is for: what a step <em>could</em> have handed over, without ticking something and
/// running the macro again.
/// </remarks>
public sealed record StepResult(string Key, string Name, string Text);

/// <summary>
/// What one step of a run came to: how long it took, how it ended, why if it failed, and everything
/// it worked out on the way.
/// </summary>
/// <remarks>
/// The same three endings the outcome variable carries, so a row that says how a step went and a
/// macro that asks about it say the same thing. The time is the step's own: it is counted from the
/// moment the step began its work to the moment its ending was settled, so a step that was retried
/// or that waited out its own clock reports the whole of that, while the pauses written around it
/// belong to the macro rather than to it.
/// </remarks>
public sealed record StepRun
{
    /// <summary>The step that ran, named the way breakpoints and step results name it.</summary>
    public required string StepId { get; init; }

    public required string StepType { get; init; }

    /// <summary><c>ok</c>, <c>failed</c> or <c>skipped</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>How long the step took, in milliseconds.</summary>
    public double ElapsedMs { get; init; }

    /// <summary>Why the step failed, as a message key, or empty when it did not fail.</summary>
    public string FailureKey { get; init; } = string.Empty;

    /// <summary>What the engine was doing when it failed, or empty when it did not fail.</summary>
    public string FailureDetail { get; init; } = string.Empty;

    /// <summary>Everything the step worked out, in the order it did.</summary>
    public IReadOnlyList<StepResult> Results { get; init; } = [];
}
