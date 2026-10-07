using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;

namespace WhaleGenie.Tests;

/// <summary>
/// A run is put on a background thread so that a step which waits — a device that is not answering,
/// a page that has not drawn the element yet — waits there instead of holding the window. Everything
/// it says and asks therefore has to be carried back, which is what these read.
/// </summary>
public class RunThreadTests
{
    [Fact]
    public void What_a_run_says_and_asks_reaches_the_window_on_the_windows_own_thread()
    {
        var landed = Ui.RunAsync(async () =>
        {
            var threads = new List<bool>();
            var host = new UiThreadRunHost(new Watching(threads));

            // Each of these is called the way the runner calls it: from a thread that is not the
            // window's own.
            await Task.Run(() => host.Log(new LogEntry(
                DateTimeOffset.Now, LogLevel.Info, 0, "control.delay", "Run.Ready", [])));
            Dispatcher.UIThread.RunJobs();

            await Task.Run(() => host.BeforeStep(Step(), 0, CancellationToken.None));
            await Task.Run(() => host.Ask(
                "control.delay", "Run.Failed", "what went wrong", "", CancellationToken.None));

            return threads;
        });

        Assert.Equal(3, landed.Count);
        Assert.All(landed, onWindowThread => Assert.True(
            onWindowThread, "a run's callback did not reach the window's own thread"));
    }

    private static ExecutableStep Step() => new() { Type = "control.delay", Id = "one" };

    /// <summary>A host that only writes down which thread each callback arrived on.</summary>
    private sealed class Watching(List<bool> threads) : IRunHost
    {
        public void Log(LogEntry entry) => threads.Add(Dispatcher.UIThread.CheckAccess());

        public Task BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        {
            threads.Add(Dispatcher.UIThread.CheckAccess());
            return Task.CompletedTask;
        }

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
        {
            threads.Add(Dispatcher.UIThread.CheckAccess());
            return Task.FromResult(StepErrorChoice.Stop);
        }
    }
}
