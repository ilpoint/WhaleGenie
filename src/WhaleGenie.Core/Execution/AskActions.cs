using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The two actions that ask the person at the machine something: whether to go on, and what to type.
/// The words are the macro author's, so they travel as plain text rather than as message keys — the
/// engine holds no user-facing words of its own — and the answer is kept as the step's result.
/// </summary>
internal static class AskActions
{
    /// <summary>
    /// Puts the macro's own question to whoever is at the machine and keeps the answer.
    /// </summary>
    public static async Task<Signal> Confirm(IRunContext run, IRunHost host, ExecutableStep step,
        int depth, CancellationToken token)
    {
        var message = run.Args(step).ValueOf("message").AsText();
        var detail = run.Args(step).ValueOf("detail").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var question = detail.Length == 0 ? message : message + "\n\n" + detail;
        var answer = await host.AskUser(new AskQuestion(AskKind.Confirm, question, string.Empty),
            token);

        run.Store(step, "confirmed", Value.FromBool(answer.Confirmed), depth);
        run.Log(LogLevel.Info, depth, step.Type,
            answer.Confirmed ? LogKeys.AskedYes : LogKeys.AskedNo, question);
        return Signal.Normal;
    }

    /// <summary>
    /// Asks for a line of writing and keeps it. A question nobody answers hands over an empty line
    /// rather than failing: a macro that needs an answer says so by looking at what it got, and one
    /// that runs under a trigger never had anybody to ask.
    /// </summary>
    public static async Task<Signal> Text(IRunContext run, IRunHost host, ExecutableStep step,
        int depth, CancellationToken token)
    {
        var message = run.Args(step).ValueOf("message").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var suggestion = run.Args(step).ValueOf("default").AsText();
        var answer = await host.AskUser(new AskQuestion(AskKind.Text, message, suggestion), token);

        run.Store(step, "text", Value.FromText(answer.Text), depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.AskedText, answer.Text);
        return Signal.Normal;
    }
}
