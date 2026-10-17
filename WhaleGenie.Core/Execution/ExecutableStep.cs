using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Execution;

/// <summary>One parameter of a step, in the shape the runner needs it.</summary>
public sealed class ExecutableParameter
{
    public required string Name { get; init; }

    /// <summary>The value as written; the runner reads it as an expression when it needs to.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// How far <see cref="Text"/> may move each run, as a fraction of it: 0.2 means "somewhere
    /// from 80% to 120% of what was written". Zero uses the value as written. Only lengths of
    /// time are given one, which is what keeps a macro's waits from looking like a machine's.
    /// </summary>
    public decimal Jitter { get; init; }

    /// <summary>Child steps, for a parameter that holds a list of them.</summary>
    public IReadOnlyList<ExecutableStep> Steps { get; init; } = [];

    /// <summary>
    /// Rows of a parameter that holds a list of them, for example the rectangles a search looks
    /// in: what each column of one row says, by name. What the columns are is the action's own
    /// business, so this carries the names and the values and nothing else.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; init; } = [];

    /// <summary>Child step, for a parameter that holds a single condition.</summary>
    public ExecutableStep? Condition { get; init; }
}

/// <summary>
/// A step in the shape the runner walks. The editor keeps its own model; this is what the
/// engine sees, so the engine never depends on anything the interface needs.
/// </summary>
public sealed class ExecutableStep
{
    public required string Type { get; init; }

    /// <summary>Stable identity, used by breakpoints and by the debugger's current step.</summary>
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<ExecutableParameter> Parameters { get; init; } = [];

    public StepMeta Meta { get; init; } = StepMeta.Empty;

    /// <summary>The text of a named parameter, or an empty string when it has none.</summary>
    public string Text(string name) => Parameter(name)?.Text ?? string.Empty;

    /// <summary>The child steps held by a named parameter.</summary>
    public IReadOnlyList<ExecutableStep> Children(string name) => Parameter(name)?.Steps ?? [];

    /// <summary>The rows held by a named parameter, or nothing when it holds none.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string name)
        => Parameter(name)?.Rows ?? [];

    /// <summary>
    /// The name a step was told to hand one of its results over under, or nothing when it left that
    /// result unticked. A result nobody asked for is not written at all rather than written and left
    /// for the macro to avoid reading, so this is the one place that answers "does this step hand
    /// that over, and what is it called".
    /// </summary>
    public string Return(string key)
    {
        // The results may be written as more than one list — a step the editor saved carries one, a
        // step built by hand may carry a list per result — so every row of every one of them counts.
        foreach (var parameter in Parameters.Where(parameter => parameter.Name == "returns"))
        {
            foreach (var row in parameter.Rows)
            {
                if (row.TryGetValue(FieldNames.Key, out var written)
                    && string.Equals(written.Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return row.TryGetValue(FieldNames.Name, out var name)
                        ? name.Trim()
                        : string.Empty;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>The condition held by a named parameter.</summary>
    public ExecutableStep? Condition(string name) => Parameter(name)?.Condition;

    /// <summary>The slack a named parameter carries, as a fraction of it, or zero.</summary>
    public decimal Jitter(string name) => Parameter(name)?.Jitter ?? 0m;

    private ExecutableParameter? Parameter(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Name == name);
}

/// <summary>How a run ended.</summary>
public enum RunStatus
{
    /// <summary>Every step ran.</summary>
    Completed,

    /// <summary>A step failed and its rule was to stop.</summary>
    Failed,

    /// <summary>The macro stopped itself, or the run was cancelled.</summary>
    Stopped,
}

/// <summary>Severity of one line of the run log.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// One line of the run log. The message travels as a text key with its arguments so the
/// engine stays free of any language and the interface can translate every line.
/// </summary>
public sealed record LogEntry(
    DateTimeOffset Time,
    LogLevel Level,
    int Depth,
    string Step,
    string Key,
    IReadOnlyList<object> Arguments);

/// <summary>What the user chose when a step asked for help.</summary>
public enum StepErrorChoice
{
    Retry,
    Skip,
    Stop,
}

/// <summary>What a step that asks the person something is after.</summary>
public enum AskKind
{
    /// <summary>A yes or no: the step goes on when it is yes.</summary>
    Confirm,

    /// <summary>A line of writing, which the step reads back as it stands.</summary>
    Text,
}

/// <summary>
/// A question the macro itself puts to whoever is at the machine, worded by the step rather than
/// by the engine: the words are the macro's own, so they travel as plain text and stay in the
/// language the person who wrote the macro wrote them in. <paramref name="Default"/> is what the
/// answer box starts with, for a question that usually gets the same answer.
/// </summary>
public sealed record AskQuestion(AskKind Kind, string Message, string Default);

/// <summary>
/// What came back from a question the macro asked. Nobody answering is an answer of its own and
/// not a failure: <see cref="Cancel"/> is what a run nobody can reach — a triggered run, one whose
/// macro asked with no window open — reads, and a macro that needs to know the difference asks
/// once and looks at what it got.
/// </summary>
public sealed record AskAnswer(bool Confirmed, string Text)
{
    /// <summary>Nobody answered: no confirmation and no writing.</summary>
    public static AskAnswer Cancel { get; } = new(false, string.Empty);
}

/// <summary>What the runner needs from whatever is driving it.</summary>
public interface IRunHost
{
    /// <summary>Records one line of the run log.</summary>
    void Log(LogEntry entry);

    /// <summary>
    /// One line of the log worded for whoever reads it, in the language the interface is in. The
    /// engine has no words of its own: it records lines as a key and its arguments, and a run that
    /// fails asks here for the text of every line it wrote so the file it leaves behind reads the
    /// way the run window does.
    /// </summary>
    string Text(LogEntry entry);

    /// <summary>
    /// Called before every step runs, so a debugger can pause or look around. Nothing but the
    /// debugger ever says no: the answer is about this one step, and false means it is not to be
    /// carried out this time — the run leaves it out, answers for it the way it answers for a
    /// switched-off step, and goes on to the next one.
    /// </summary>
    Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token);

    /// <summary>
    /// Asked when a step's failure rule is <see cref="StepErrorAction.AskUser"/>. The reason is
    /// a message key and the detail is what the runner was doing, so the host can word the
    /// question in the reader's language. The comment is the note the user wrote on the step,
    /// which is where the reason this step was in the macro at all tends to live.
    /// </summary>
    Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token);

    /// <summary>
    /// Asked when a step of the macro's own puts a question to the person — "is it time yet?",
    /// "which order number?". A host with nobody to ask answers
    /// <see cref="AskAnswer.Cancel"/> rather than waiting, which is what a triggered run does.
    /// </summary>
    Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token);
}

/// <summary>A host that keeps the log and answers nothing, used when nobody is watching.</summary>
public sealed class SilentRunHost : IRunHost
{
    private readonly List<LogEntry> _entries = [];

    /// <summary>Everything the run wrote, in order.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    public void Log(LogEntry entry) => _entries.Add(entry);

    /// <summary>Nobody is translating, so the key and its arguments stand in for the wording.</summary>
    public string Text(LogEntry entry)
        => entry.Arguments.Count == 0
            ? entry.Key
            : entry.Key + " " + string.Join(" ", entry.Arguments);

    public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        => Task.FromResult(true);

    public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
        => Task.FromResult(StepErrorChoice.Stop);

    public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
        => Task.FromResult(AskAnswer.Cancel);
}

/// <summary>
/// The outcome of a whole run. <paramref name="Key"/> is the failure key and is empty unless the
/// run failed; how the run ended is <see cref="StatusKey"/>.
/// </summary>
public sealed record RunResult(RunStatus Status, string Key, string Detail, int Steps)
{
    public bool Succeeded => Status is RunStatus.Completed;

    public string StatusKey => Status switch
    {
        RunStatus.Completed => StatusKeys.Finished,
        RunStatus.Stopped => StatusKeys.Stopped,
        _ => StatusKeys.Aborted,
    };
}

/// <summary>One background run a macro started and has not yet waited for.</summary>
public sealed record ChildRunInfo(string Handle, string Macro, bool Running);
