using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests.Bench;

/// <summary>
/// The person behind a run: the bench answers every question with the same word and accepts every
/// step, so an action that asks something has an answer to work with instead of nobody at the
/// keyboard. That is what a macro author has when they run a macro by hand.
/// </summary>
internal sealed class BenchHost : IRunHost
{
    public void Log(LogEntry entry)
    {
    }

    public string Text(LogEntry entry) => entry.Key;

    public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        => Task.FromResult(true);

    public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
        => Task.FromResult(StepErrorChoice.Stop);

    public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
        => Task.FromResult(new AskAnswer(true, "bench"));

    /// <summary>
    /// The bench runs every action the catalogue has, including the ones that cannot be undone, so
    /// it agrees to all of them: what is being checked here is that the action runs and matches its
    /// declaration, not that somebody would have said yes.
    /// </summary>
    public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
        => Task.FromResult(true);
}
