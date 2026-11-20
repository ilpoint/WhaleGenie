using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What the step being attempted has worked out so far: what it saw, what it read, which window the
/// picture came from, and why it did not work. It lasts one attempt and is cleared where the attempt
/// begins, so a step that is retried starts from nothing rather than from what the try before it
/// left behind.
/// </summary>
/// <remarks>
/// It is separate from <see cref="RunState"/> because the two lifetimes differ: this one is emptied
/// between attempts of the same step, while the run's bookkeeping lasts until the run ends.
/// </remarks>
internal sealed class StepProgress
{
    /// <summary>
    /// What the step being run has worked out so far, newest last, or null when nobody is watching
    /// this run. A step that holds steps hands its own list back to the one around it once the steps
    /// inside are done, so every result is filed under the step that worked it out.
    /// </summary>
    public List<StepResult>? Worked { get; set; }

    /// <summary>
    /// What the step going on right now has just seen, kept until the step is done so that one
    /// step hands over one look however many times it had to look to get there.
    /// </summary>
    public StepLook? Saw { get; set; }

    /// <summary>The areas the last reading of the screen was taken from, so a look can draw them.</summary>
    public IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> Read { get; set; } = [];

    /// <summary>
    /// What the last picture taken for the step going on right now was taken of: which window, and
    /// by which means. It travels with the result the step leaves behind, because a step that found
    /// nothing is answered by asking whether it was even looking at the right window.
    /// </summary>
    public string TakenWindow { get; set; } = string.Empty;

    public CaptureWay? TakenMethod { get; set; }

    /// <summary>Why the step did not work, or null while it still might.</summary>
    public StepFailure? Failure { get; set; }
}
