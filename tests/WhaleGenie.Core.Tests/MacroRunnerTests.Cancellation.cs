using System;
using System.Threading;
using System.Threading.Tasks;

using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What a cancellation means. One that came from the run's own token is the person stopping the
/// macro; one that came from somewhere else is a device or a library giving up, and the run says
/// so instead of pretending the person asked it to stop.
/// </summary>
public partial class MacroRunnerTests
{
    [Fact]
    public async Task The_run_token_still_reads_as_a_stop()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var result = await RunAsync([Set("a", "1")], token: cancel.Token);

        Assert.Equal(RunStatus.Stopped, result.Result.Status);
        Assert.Equal(string.Empty, result.Result.Key);
    }

    [Fact]
    public async Task A_cancellation_from_somewhere_else_is_a_failure()
    {
        var host = new CancellingHost();
        var runner = new MacroRunner(new VariableStore(), host, null, 1);

        var result = await runner.RunAsync([Set("a", "1")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.Cancelled, result.Key);
        Assert.Contains("device", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>A host whose first step is cancelled by a token of its own, as a device may be.</summary>
    private sealed class CancellingHost : IRunHost
    {
        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromException<bool>(new OperationCanceledException("the device gave up"));

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);

        public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
            => Task.FromResult(true);
    }
}
