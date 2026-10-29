using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend list of running programs: what is running, what has stopped, and what it returned.
/// </summary>
/// <remarks>
/// <see cref="AppearsLater"/> covers the case a wait is about: a program that is not there yet but
/// turns up once the list has been read a few times, which a real machine does between two looks.
/// </remarks>
internal sealed class FakeProcesses(FakeDesk desk) : IProcessDevice
{
    /// <summary>The running programs the fake reports, by name.</summary>
    public Dictionary<string, List<int>> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process ids that have finished.</summary>
    public HashSet<int> Exited { get; } = [];

    /// <summary>What a finished process returned.</summary>
    public Dictionary<int, int> ExitCodes { get; } = [];

    /// <summary>A program name that only shows up once the process list has been read a few times.</summary>
    public string? AppearsLater { get; set; }

    /// <summary>How many reads happen before <see cref="AppearsLater"/> shows up.</summary>
    public int AppearsAfter { get; set; } = 1;

    /// <summary>The next process id a start hands out.</summary>
    public int NextProcessId { get; set; } = 1000;

    /// <summary>What a command line prints.</summary>
    public CommandResult Command { get; set; } = new(0, string.Empty, string.Empty);

    private int _reads;

    public int Start(StartRequest request)
    {
        desk.Note($"start {request.FileName}|{request.Arguments}|{request.WorkingDirectory}"
                  + $"|{request.Hidden}{(request.RunAsAdmin ? "|admin" : string.Empty)}"
                  + EnvironmentNote(request.Environment));
        return NextProcessId++;
    }

    public IReadOnlyList<int> Find(string name)
    {
        desk.Note($"find {name}");
        _reads++;
        if (AppearsLater is not null && _reads > AppearsAfter)
        {
            Running[AppearsLater] = [NextProcessId];
        }

        return Running.TryGetValue(name, out var ids) ? ids : [];
    }

    public IReadOnlyList<string> List()
    {
        desk.Note("listPrograms");
        return [.. Running.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    public ProcessDetails? Details(string target)
    {
        desk.Note($"processDetails {target}");
        return Running.TryGetValue(target, out var ids) && ids.Count > 0
            ? new ProcessDetails(ids[0], target, $@"C:\fake\{target}.exe", 12.5, 3.4)
            : null;
    }

    public bool HasExited(int id)
    {
        desk.Note($"hasExited {id}");
        return Exited.Contains(id);
    }

    public int? ExitCode(int id)
    {
        desk.Note($"exitCode {id}");
        return Exited.Contains(id) ? ExitCodes.TryGetValue(id, out var code) ? code : 0 : null;
    }

    public int StopByName(string name, bool force)
    {
        desk.Note($"stopName {name}|{force}");
        return Running.Remove(name) ? 1 : 0;
    }

    public bool StopById(int id, bool force)
    {
        desk.Note($"stopId {id}|{force}");
        Exited.Add(id);
        return true;
    }

    public CommandResult Run(CommandRequest request)
    {
        desk.Note($"run {request.FileName}|{request.Arguments}|{request.WorkingDirectory}"
                  + $"|{request.TimeoutMs}{EnvironmentNote(request.Environment)}"
                  + InputNote(request.StandardInput)
                  + $"|out {request.OutputEncoding ?? "default"}");

        // A real device hands each line over while the program is still running; the fake has all
        // of them already and hands them over the same way, blank lines included.
        foreach (var line in Lines(Command.StandardOutput))
        {
            request.OnOutput?.Invoke(line);
        }

        foreach (var line in Lines(Command.StandardError))
        {
            request.OnError?.Invoke(line);
        }

        return Command;
    }

    /// <summary>The environment a call carried, spelled out so a test can read it back.</summary>
    private static string EnvironmentNote(IReadOnlyDictionary<string, string>? values)
        => values is null or { Count: 0 }
            ? string.Empty
            : "|env " + string.Join(";",
                values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>What a call fed the program, with the line endings spelled out.</summary>
    private static string InputNote(string? text)
        => text is null ? string.Empty : "|in " + text.Replace("\r\n", "\n").Replace("\n", "\\n");

    /// <summary>The lines a stream holds, the way reading it a line at a time would find them.</summary>
    private static IEnumerable<string> Lines(string text)
        => text.Length == 0
            ? []
            : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
}
