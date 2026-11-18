using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Runtime;

/// <summary>
/// Two watchers of one run: the window that is showing it and the record that outlives the window.
/// The engine has one place to report to, so the reporting is handed on to both rather than the
/// window having to know about the record or the other way round.
/// </summary>
internal sealed class RunLooks(params IRunLooks[] looks) : IRunLooks
{
    public void Look(StepLook look)
    {
        foreach (var one in looks)
        {
            one.Look(look);
        }
    }

    public void Ran(StepRun run)
    {
        foreach (var one in looks)
        {
            one.Ran(run);
        }
    }

    public void Reading(WindowInfo? window)
    {
        foreach (var one in looks)
        {
            one.Reading(window);
        }
    }
}
