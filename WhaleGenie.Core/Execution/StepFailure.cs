using System;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Why a step could not run. The key is translated by the interface; the detail is what the run
/// knows about this particular time — which window, which file — and is shown beside it.
/// </summary>
/// <remarks>
/// It is thrown to leave the step that failed and caught by the one that can answer for it, so it
/// travels through the same call stack the steps do. It is a type of its own rather than a nested
/// one because the run's own bookkeeping keeps the ones a catch has taken hold of, and that
/// bookkeeping is no longer written inside the runner.
/// </remarks>
internal sealed class StepFailure : Exception
{
    public StepFailure(string key, string detail = "")
        : base(key)
    {
        Key = key;
        Detail = detail;
    }

    public string Key { get; }

    public string Detail { get; }
}
