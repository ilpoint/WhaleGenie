using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Execution;

// The one thing a script host needs that only the interpreter can answer: running a step of the
// macro by the identity the script names.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Runs a step of the macro being run, found by the identity written in the step list, and says
    /// whether it ended well. A step that fails is the step's own failure — its rule decides what
    /// happens to the rest of the macro — while a step that asks for the whole macro to stop stops
    /// it, one that returns ends it, and one that leaves a loop it was never inside is the mistake
    /// it always was.
    /// </summary>
    private bool RunStepById(string id, string type, int depth, CancellationToken token)
    {
        var wanted = id.Trim();
        var step = FindStep(_run.Steps, wanted) ?? throw new StepFailure(RunKeys.NoSuchStep, wanted);
        Log(LogLevel.Info, depth, type, LogKeys.ScriptStep, wanted);

        var signal = RunSteps([step], depth + 1, token).GetAwaiter().GetResult();
        if (signal is Signal.Break or Signal.Continue)
        {
            throw new StepFailure(LoopControlKey(signal), wanted);
        }

        if (signal is Signal.Stop)
        {
            _run.StoppedInsideScript = true;
        }

        if (signal is Signal.Return)
        {
            _run.ReturnedInsideScript = true;
        }

        return signal is not Signal.Failed;
    }

    /// <summary>The step with this identity, looking inside the blocks that hold steps as well.</summary>
    private static ExecutableStep? FindStep(IReadOnlyList<ExecutableStep> steps, string id)
    {
        foreach (var step in steps)
        {
            if (string.Equals(step.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return step;
            }

            foreach (var parameter in step.Parameters)
            {
                if (FindStep(parameter.Steps, id) is { } inside)
                {
                    return inside;
                }
            }
        }

        return null;
    }
}
