using System;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Hands a run's words and questions to the interface thread through the dispatcher port, because
/// the run itself is not on it.
/// </summary>
internal sealed class PostedRunHost(IRunHost host, IUiPost ui) : IRunHost
{
    public void Log(LogEntry entry) => ui.Post(() => host.Log(entry));

    public string Text(LogEntry entry) => host.Text(entry);

    public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        => ui.InvokeAsync(() => host.BeforeStep(step, depth, token));

    public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
        => ui.InvokeAsync(() => host.Ask(step, reason, detail, comment, token));

    public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
        => ui.InvokeAsync(() => host.AskUser(question, token));

    public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
        => ui.InvokeAsync(() => host.ConfirmIrreversible(step, token));
}
