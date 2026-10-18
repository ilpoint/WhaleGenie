using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Execution;

/// <summary>One line of a run, kept after the window that showed it has moved on.</summary>
public sealed record RunLogLine(DateTimeOffset Time, LogLevel Level, int Depth, string Text);

/// <summary>What a step came to, kept with the run that produced it.</summary>
public sealed record RunLogOutcome(string Name, string Label, string Value);

/// <summary>A finished or still-running run as the MCP debug tools see it.</summary>
public sealed record RunLogSnapshot(
    string Id,
    string Macro,
    string Source,
    DateTimeOffset Started,
    DateTimeOffset? Finished,
    string Status,
    string Key,
    string Detail,
    int Steps,
    string FailureLogPath,
    IReadOnlyList<RunLogLine> Lines,
    IReadOnlyList<RunLogOutcome> Outcomes);

/// <summary>
/// The log and outcome of one run. A debug run feeds it from the run window; a triggered run feeds
/// it through its host. It is deliberately independent of the window so MCP can read a run that
/// nobody is looking at.
/// </summary>
public sealed class RunLogCapture
{
    private readonly object _gate = new();
    private readonly List<RunLogLine> _lines = [];
    private readonly List<RunLogOutcome> _outcomes = [];
    private bool _finished;
    private DateTimeOffset? _finishedAt;
    private string _status = string.Empty;
    private string _key = string.Empty;
    private string _detail = string.Empty;
    private int _steps;
    private string _failureLogPath = string.Empty;

    internal RunLogCapture(string macro, string source)
    {
        Id = Guid.NewGuid().ToString("N")[..8];
        Macro = macro;
        Source = source;
        Started = DateTimeOffset.Now;
    }

    public string Id { get; }

    public string Macro { get; }

    public string Source { get; }

    public DateTimeOffset Started { get; }

    /// <summary>Records one line, worded by the host that owns the run.</summary>
    public void Add(LogEntry entry, string text)
    {
        lock (_gate)
        {
            _lines.Add(new RunLogLine(entry.Time, entry.Level, entry.Depth, text));
            if (entry.Key == LogKeys.FailureLog && entry.Arguments.Count > 0
                && entry.Arguments[0] is string path)
            {
                _failureLogPath = path;
            }
        }
    }

    /// <summary>Records how the run ended and what its steps came to.</summary>
    public void Finish(RunResult? result, string status, IEnumerable<RunLogOutcome>? outcomes = null)
    {
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _finishedAt = DateTimeOffset.Now;
            _status = status;
            _key = result?.Key ?? string.Empty;
            _detail = result?.Detail ?? string.Empty;
            _steps = result?.Steps ?? 0;
            _outcomes.Clear();
            if (outcomes is not null)
            {
                _outcomes.AddRange(outcomes);
            }
        }
    }

    public RunLogSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new RunLogSnapshot(
                Id, Macro, Source, Started, _finishedAt, _status, _key, _detail, _steps,
                _failureLogPath, [.. _lines], [.. _outcomes]);
        }
    }
}

/// <summary>
/// The last few runs, in memory only. The debugger's window owns its own copy of the lines; this
/// is the copy that outlives the window and lets MCP read what a person just ran. Nothing is
/// written to disk here: a failed run already has its own failure file, and a successful run
/// should not quietly become a second one.
/// </summary>
public static class RunLogStore
{
    private const int Keep = 20;
    private static readonly object Gate = new();
    private static readonly List<RunLogCapture> Captures = [];

    public static RunLogCapture Begin(string macro, string source)
    {
        var capture = new RunLogCapture(macro, source);
        lock (Gate)
        {
            Captures.Add(capture);
            while (Captures.Count > Keep)
            {
                Captures.RemoveAt(0);
            }
        }

        return capture;
    }

    /// <summary>The newest runs first; a run still in progress is included.</summary>
    public static IReadOnlyList<RunLogSnapshot> Recent(int limit = 10)
    {
        lock (Gate)
        {
            return [.. Captures
                .OrderByDescending(capture => capture.Started)
                .Take(Math.Clamp(limit, 1, Keep))
                .Select(capture => capture.Snapshot())];
        }
    }

    public static RunLogSnapshot? Find(string id)
    {
        lock (Gate)
        {
            return Captures.FirstOrDefault(capture =>
                string.Equals(capture.Id, id, StringComparison.OrdinalIgnoreCase))?.Snapshot();
        }
    }

    public static RunLogSnapshot? Latest()
    {
        lock (Gate)
        {
            return Captures.OrderByDescending(capture => capture.Started)
                .FirstOrDefault()?.Snapshot();
        }
    }

    /// <summary>Only for checks: forget the runs a previous test left behind.</summary>
    internal static void Clear()
    {
        lock (Gate)
        {
            Captures.Clear();
        }
    }
}
