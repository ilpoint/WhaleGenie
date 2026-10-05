using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Viktor.Core.Execution;

/// <summary>One parameter of a step, in the shape the runner needs it.</summary>
public sealed class ExecutableParameter
{
    public required string Name { get; init; }

    /// <summary>The value as written; the runner reads it as an expression when it needs to.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Child steps, for a parameter that holds a list of them.</summary>
    public IReadOnlyList<ExecutableStep> Steps { get; init; } = [];

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

    /// <summary>The condition held by a named parameter.</summary>
    public ExecutableStep? Condition(string name) => Parameter(name)?.Condition;

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

/// <summary>What the runner needs from whatever is driving it.</summary>
public interface IRunHost
{
    /// <summary>Records one line of the run log.</summary>
    void Log(LogEntry entry);

    /// <summary>Called before every step runs, so a debugger can pause or look around.</summary>
    Task BeforeStep(ExecutableStep step, int depth, CancellationToken token);

    /// <summary>
    /// Asked when a step's failure rule is <see cref="StepErrorAction.AskUser"/>. The reason is
    /// a message key and the detail is what the runner was doing, so the host can word the
    /// question in the reader's language.
    /// </summary>
    Task<StepErrorChoice> Ask(string step, string reason, string detail, CancellationToken token);
}

/// <summary>A host that keeps the log and answers nothing, used when nobody is watching.</summary>
public sealed class SilentRunHost : IRunHost
{
    private readonly List<LogEntry> _entries = [];

    /// <summary>Everything the run wrote, in order.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    public void Log(LogEntry entry) => _entries.Add(entry);

    public Task BeforeStep(ExecutableStep step, int depth, CancellationToken token) => Task.CompletedTask;

    public Task<StepErrorChoice> Ask(string step, string reason, string detail, CancellationToken token)
        => Task.FromResult(StepErrorChoice.Stop);
}

/// <summary>The outcome of a whole run.</summary>
public sealed record RunResult(RunStatus Status, string Key, string Detail, int Steps)
{
    public bool Succeeded => Status is RunStatus.Completed;
}
