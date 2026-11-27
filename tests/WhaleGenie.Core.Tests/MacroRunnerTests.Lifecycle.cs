using System;
using System.Threading;
using System.Threading.Tasks;

using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A runner carries the state of one run, so it is for one run at a time. A second run started on
/// it before the first is over is refused, and the runner is usable again once the first ends.
/// </summary>
public partial class MacroRunnerTests
{
    [Fact]
    public async Task A_runner_refuses_a_second_run_while_one_is_going()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new MacroRunner(new VariableStore(), new HoldingHost(entered, release), null, 1);
        var steps = new[] { Set("a", "1") };

        var first = runner.RunAsync(steps);
        await entered.Task;

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(steps));
        Assert.Contains("one macro at a time", refusal.Message, StringComparison.Ordinal);

        release.SetResult();
        Assert.True((await first).Succeeded);

        // Once the run is over the same runner takes the next one, as a window that stays open does.
        Assert.True((await runner.RunAsync(steps)).Succeeded);
    }

    /// <summary>A host that holds the run at its first step until the test lets it go.</summary>
    private sealed class HoldingHost(TaskCompletionSource entered, TaskCompletionSource release)
        : IRunHost
    {
        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public async Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        {
            entered.TrySetResult();
            await release.Task;
            return true;
        }

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);

        public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
            => Task.FromResult(true);
    }
}
