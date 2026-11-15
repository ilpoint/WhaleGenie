using System.Threading;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

// Asking a condition: the question a branch, a loop, a wait and a breakpoint all put.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Answers one condition. Nothing here waits or hands work to another thread, so a condition
    /// can be asked again and again without holding anything up — which is what the wait block
    /// does with it.
    /// </summary>
    private bool Check(ExecutableStep step, string name, CancellationToken token)
    {
        StepChecks.RequireCondition(step, name);
        StepChecks.ValidateAnswers(step);
        token.ThrowIfCancellationRequested();

        // A condition is the shape it was built from. Words are words, a comparison is a tree, and
        // nothing is read out of a line in between them.
        return Args(step).ValueOf(name).AsBool();
    }
}
