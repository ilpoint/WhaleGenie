using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Execution;

/// <summary>
/// Hands a run's words and questions to the interface thread, because the run itself is not on it.
/// </summary>
/// <remarks>
/// A run is put on a background thread so that a step which waits — a device that is not answering,
/// a page that has not drawn the element yet — waits there. On the interface thread that wait is a
/// window that does not repaint and cannot be stopped, with the stop button inside it that the
/// person is trying to press. Everything the run says and asks comes back through here, so the host
/// goes on touching its controls and its collections exactly as it did when the run was on the
/// interface thread.
/// </remarks>
internal sealed class UiThreadRunHost(IRunHost host) : IRunHost
{
    /// <summary>
    /// Posted rather than waited on: a log line is worth nothing to the run, and waiting for the
    /// window to catch up would put the run's own pace in the window's hands. Posts keep their
    /// order, which is the order the lines were written in.
    /// </summary>
    public void Log(LogEntry entry) => Dispatcher.UIThread.Post(() => host.Log(entry));

    /// <summary>
    /// Answered here rather than posted, and on whichever thread asks: it is wording, not window
    /// state, and the run wants it while it is writing the file it leaves behind.
    /// </summary>
    public string Text(LogEntry entry) => host.Text(entry);

    /// <summary>
    /// Waited on, because the answer decides what the run does next: the step the debugger is
    /// looking at has to be the one it is about to run, whether to stop has to be asked before
    /// anything else happens, and the answer — carry on with this step or leave it out — is that
    /// decision.
    /// </summary>
    public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        => Dispatcher.UIThread.InvokeAsync(() => host.BeforeStep(step, depth, token));

    public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
        => Dispatcher.UIThread.InvokeAsync(() => host.Ask(step, reason, detail, comment, token));

    /// <summary>Waited on for the same reason a failure question is: the answer is what runs next.</summary>
    public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
        => Dispatcher.UIThread.InvokeAsync(() => host.AskUser(question, token));
}
