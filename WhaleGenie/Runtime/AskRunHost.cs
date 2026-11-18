using System;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

/// <summary>
/// The host a macro runs under when a trigger started it rather than the debugger. Nobody is
/// watching a log there, but a step whose failure rule is "ask me" still has to reach the person,
/// so the question goes out through the callback the window installed. With no window to ask —
/// or with the run already cancelled — the answer is to stop, which is the one choice that
/// cannot make things worse on its own.
/// </summary>
/// <remarks>
/// A step that asks the macro's own question reaches the same window through its own callback. A
/// macro that ran while the window was away reads the question as unanswered rather than stopping
/// the run: the question belongs to the macro, so what an empty answer means is the macro's to
/// decide.
/// </remarks>
public sealed class AskRunHost(
    Func<string, string, string, string, Task<StepErrorChoice>>? ask,
    Func<AskQuestion, Task<AskAnswer>>? askUser = null,
    Action<LogEntry, string>? log = null) : IRunHost
{
    /// <summary>
    /// A triggered run has no log window to write to, but its lines still belong to the run's
    /// history: the debug tools read them after the fact.
    /// </summary>
    public void Log(LogEntry entry) => log?.Invoke(entry, Text(entry));

    /// <summary>
    /// Wanted all the same: a triggered run that fails leaves its whole log behind as a file, and
    /// that file is worded in the language the interface is in.
    /// </summary>
    public string Text(LogEntry entry) => RunLogText.Of(entry);

    /// <summary>
    /// Nothing pauses a triggered run between its steps and nothing leaves a step out: only a
    /// debugger asks for either.
    /// </summary>
    public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        => Task.FromResult(true);

    public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
        => ask is null || token.IsCancellationRequested
            ? Task.FromResult(StepErrorChoice.Stop)
            : ask(step, reason, detail, comment);

    public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
        => askUser is null || token.IsCancellationRequested
            ? Task.FromResult(AskAnswer.Cancel)
            : askUser(question);
}
