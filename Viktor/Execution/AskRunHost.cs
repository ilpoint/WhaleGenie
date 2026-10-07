using System;
using System.Threading;
using System.Threading.Tasks;
using Viktor.Core.Execution;

namespace Viktor.Execution;

/// <summary>
/// The host a macro runs under when a trigger started it rather than the debugger. Nobody is
/// watching a log there, but a step whose failure rule is "ask me" still has to reach the person,
/// so the question goes out through the callback the window installed. With no window to ask —
/// or with the run already cancelled — the answer is to stop, which is the one choice that
/// cannot make things worse on its own.
/// </summary>
public sealed class AskRunHost(Func<string, string, string, string, Task<StepErrorChoice>>? ask) : IRunHost
{
    /// <summary>A triggered run has no log window to write to, so the lines are dropped.</summary>
    public void Log(LogEntry entry)
    {
    }

    /// <summary>Nothing pauses a triggered run between its steps: only the debugger does that.</summary>
    public Task BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        => Task.CompletedTask;

    public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
        => ask is null || token.IsCancellationRequested
            ? Task.FromResult(StepErrorChoice.Stop)
            : ask(step, reason, detail, comment);
}
