using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // -------------------------------------------------------------------- process

    /// <summary>Starts another program and keeps what later steps need to reach it.</summary>
    private void StartProcess(ExecutableStep step, int depth)
    {
        var program = ReadValue(step, "programFile").AsText();
        var arguments = ReadValue(step, "commandArguments").AsText();
        var folder = ReadValue(step, "workingDirectory").AsText();
        var hidden = Flag(step, "hidden", false);
        var runAsAdmin = Flag(step, "runAsAdmin", false);

        var id = _devices.Processes.Start(
            new StartRequest(program, arguments, folder, hidden, runAsAdmin, EnvironmentOf(step)));

        if (Wants(step, "program"))
        {
            // A program that has already gone by the time this is asked still gets its id and the
            // name it was started from; the window title and handle stay empty because there is no
            // window to ask. A window that is still coming up is the ordinary case, and a later
            // window step is what waits for it.
            var details = _devices.Processes.Details(id);
            Give(step, "program", StartedProgramRecord(id, program, details));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.StartedProgram, program, id);
    }

    /// <summary>
    /// The program a start step just launched, in the words later steps use: its id, its process
    /// name, and the title and handle of its main window. The window parts are empty until the
    /// program has one, so the record also says how it can be recognised again: by its name. That
    /// is what lets a console program, which never has a window, be waited on as it stands.
    /// </summary>
    private static Value StartedProgramRecord(int id, string path, ProcessDetails? details)
    {
        var name = details?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(path);
        }

        return Value.FromRecord(
        [
            (FieldNames.Id, Value.FromNumber(id)),
            (FieldNames.Process, Value.FromText(name)),
            (FieldNames.Title, Value.FromText(details?.MainWindowTitle ?? string.Empty)),
            (FieldNames.Handle, Value.FromNumber(details?.MainWindowHandle ?? 0)),
            (FieldNames.How, Value.FromText(WindowHow.ProcessEquals)),
        ]);
    }

    /// <summary>
    /// One program as the detailed record <c>process.info</c> hands over: the file it was started
    /// from, its id, its name, and what it is costing.
    /// </summary>
    private static Value ProgramDetailsRecord(ProcessDetails details)
        => Value.FromRecord(
        [
            (FieldNames.Info, Value.FromText(details.Path)),
            (FieldNames.Id, Value.FromNumber(details.Id)),
            (FieldNames.Name, Value.FromText(details.Name)),
            (FieldNames.MemoryMb, Value.FromNumber(details.MemoryMb)),
            (FieldNames.CpuSeconds, Value.FromNumber(details.CpuSeconds)),
        ]);

    /// <summary>
    /// The environment variables a step hands a program, written one <c>NAME=value</c> per line.
    /// Blank lines and lines starting with <c>#</c> are skipped, so a macro can keep a setting it
    /// has switched off next to the one it uses. A line that is not a pair at all is reported
    /// rather than ignored: a program started with half the environment the macro meant to give it
    /// would be harder to explain than a step that says it could not read the list.
    /// </summary>
    private Dictionary<string, string>? EnvironmentOf(ExecutableStep step)
    {
        var text = ReadText(step, "environment");
        if (text.Trim().Length == 0)
        {
            return null;
        }

        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(text))
        {
            var pair = line.Trim();
            if (pair.Length == 0 || pair.StartsWith('#'))
            {
                continue;
            }

            var cut = pair.IndexOf('=');
            if (cut <= 0)
            {
                throw new StepFailure(RunKeys.BadEnvironment, pair);
            }

            wanted[pair[..cut].Trim()] = pair[(cut + 1)..].Trim();
        }

        return wanted.Count == 0 ? null : wanted;
    }

    /// <summary>
    /// What a step feeds the program on its standard input, or nothing at all when the field was
    /// left empty. The text is not trimmed: a program that reads lines wants the line ending the
    /// macro wrote. It goes out as UTF-8, the same encoding this side reads the answer back in.
    /// </summary>
    private string? StandardInputOf(ExecutableStep step)
    {
        var text = ReadText(step, "standardInput");
        return text.Trim().Length == 0 ? null : text;
    }

    /// <summary>
    /// The code page a step says the command prints in, or nothing at all to take this machine's
    /// own, which is what a command line on this machine writes in unless it was told otherwise.
    /// </summary>
    private static string? OutputEncodingOf(ExecutableStep step)
    {
        var chosen = step.Text("outputEncoding").Trim();
        return chosen.Length == 0 ? null : chosen;
    }

    /// <summary>Waits until the program this step names is running.</summary>
    private async Task WaitForProgram(ExecutableStep step, int depth, CancellationToken token)
    {
        // What the step names is worked out once, before the waiting starts: a rule that cannot be
        // read at all is a mistake to report, not something to wait for. Only the program not
        // being there yet is what the wait is about.
        var rule = ProgramRule(step);
        var timeout = step.Meta.TimeoutMs;

        var ids = await WaitForValueAsync(
            () => ProgramIds(rule) is { Count: > 0 } found ? found : null,
            timeout, 100, token);
        if (ids is null)
        {
            throw new StepFailure(RunKeys.ProgramNotFound, rule.MatchValue);
        }

        Give(step, "process", Value.FromNumber(ids[0]));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ProgramFound, rule.MatchValue, ids[0]);
    }

    /// <summary>
    /// Waits for the program this step names to finish, and keeps the exit code it returned. The
    /// program has to be found before the wait starts: a name that is not running is a mistake to
    /// report rather than a wait that ends the moment it begins.
    /// </summary>
    private async Task WaitForExit(ExecutableStep step, int depth, CancellationToken token)
    {
        var rule = ProgramRule(step);
        var id = ProgramIds(rule).FirstOrDefault();
        if (id == 0)
        {
            throw new StepFailure(RunKeys.NoSuchProcess, rule.MatchValue);
        }

        var timeout = step.Meta.TimeoutMs;

        if (!await WaitForFlagAsync(() => _devices.Processes.HasExited(id), timeout, 100, token))
        {
            throw new StepFailure(RunKeys.ProcessTimeout, rule.MatchValue);
        }

        var code = _devices.Processes.ExitCode(id) ?? 0;
        Give(step, "exitCode", Value.FromNumber(code));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ProcessExited, id, code);
    }

    /// <summary>
    /// Leaves true or false behind, depending on whether the program is running. A program named by
    /// its process is asked of the process table, so a window that has been closed while the
    /// program runs on still answers true.
    /// </summary>
    private void ProgramRunning(ExecutableStep step, int depth)
    {
        var running = ProgramIds(ProgramRule(step)).Count > 0;
        Store(step, "running", Value.FromBool(running), depth);
    }

    /// <summary>Collects the names of the running programs into a list.</summary>
    private void ListPrograms(ExecutableStep step, int depth)
    {
        var programs = _devices.Processes.List();
        Give(step, "processes", Value.FromList(programs.Select(Value.FromText)));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ListedPrograms, programs.Count);
    }

    /// <summary>
    /// Reads what is known about one running program into a variable and its parts, the way a
    /// match stores the place it found. The path is what a macro usually wants: it is how a
    /// window's program is started again, or told apart from another program of the same name.
    /// </summary>
    private void ProgramDetails(ExecutableStep step, int depth)
    {
        var rule = ProgramRule(step);
        var id = ProgramIds(rule).FirstOrDefault();
        var details = (id == 0 ? null : _devices.Processes.Details(id))
            ?? throw new StepFailure(RunKeys.NoSuchProcess, rule.MatchValue);

        // One program is one record, the way one window is: what it is called, what it is, and what
        // it is costing are answers to the same question and travel together.
        Give(step, "program", ProgramDetailsRecord(details));

        Log(LogLevel.Info, depth, step.Type, LogKeys.ProgramDetails, details.Name, details.Id);
    }

    /// <summary>
    /// Closes the program this step names. Naming it by a window or a handle closes that one
    /// program; naming it by process name closes every program of that name, which is the answer
    /// the question asked for. Nothing to close is not a failure: the program having gone already
    /// is what the step wanted.
    /// </summary>
    private void StopProgram(ExecutableStep step, int depth)
    {
        var rule = ProgramRule(step);
        var force = Flag(step, "force", false);

        var stopped = ProgramIds(rule).Count(id => _devices.Processes.StopById(id, force));

        Give(step, "stopped", Value.FromNumber(stopped));
        Log(LogLevel.Info, depth, step.Type, LogKeys.StoppedProgram, rule.MatchValue, stopped);
    }

    /// <summary>Runs a command line to the end and keeps what it printed.</summary>
    private void RunCommand(ExecutableStep step, int depth)
    {
        var program = ReadValue(step, "commandFile").AsText();
        var arguments = ReadValue(step, "commandArguments").AsText();
        var folder = ReadValue(step, "workingDirectory").AsText();
        // The process device takes zero to mean "do not wait at all"; the step's own zero means
        // "no limit", which is the largest wait it can be given.
        var timeout = step.Meta.TimeoutMs > 0 ? step.Meta.TimeoutMs : int.MaxValue;

        // A command that takes its time is easier to trust when what it prints turns up while it is
        // still running, but one that prints thousands of lines would bury the log, so this is
        // asked for rather than assumed.
        var watching = Flag(step, "streamOutput", false);
        if (watching)
        {
            Log(LogLevel.Info, depth, step.Type, LogKeys.CommandStarted, program);
        }

        var result = _devices.Processes.Run(new CommandRequest(program, arguments, folder, timeout,
            EnvironmentOf(step), StandardInputOf(step),
            watching ? line => Log(LogLevel.Info, depth, step.Type, LogKeys.CommandOutput, line) : null,
            watching ? line => Log(LogLevel.Info, depth, step.Type, LogKeys.CommandError, line) : null,
            OutputEncodingOf(step)));

        Store(step, "output", Value.FromText(result.StandardOutput), depth);
        Store(step, "error", Value.FromText(result.StandardError), depth);
        Store(step, "exitCode", Value.FromNumber(result.ExitCode), depth);

        Log(LogLevel.Info, depth, step.Type, LogKeys.CommandFinished, program, result.ExitCode);
    }


    /// <summary>
    /// Which program this step names. It is the same field the window actions use, so the picker,
    /// the list of windows and the ways of matching come with it. What it may not be is empty: a
    /// window action left blank means the window in front, which changes from moment to moment,
    /// where a program action left blank would mean guessing which program was meant.
    /// </summary>
    private WindowRule ProgramRule(ExecutableStep step)
        => WindowRuleOf(step) is { HasTarget: true } rule
            ? rule
            : throw new StepFailure(RunKeys.MissingProgram);

    /// <summary>
    /// The processes the rule names, in the order the system gives them. How the rule matches
    /// decides what is asked: a process name is answered by the process table itself, so a service
    /// or a console program that has no window is still found; a handle is that one program; a
    /// title or a window class names a window first, and the program behind it is the answer.
    /// </summary>
    private IReadOnlyList<int> ProgramIds(WindowRule rule)
    {
        switch (rule.Reading.Match)
        {
            case WindowMatch.Process:
                return _devices.Processes.Find(rule.Process, rule.Reading.Compare, rule.Options);

            case WindowMatch.Handle:
                return One(_devices.Windows.ProcessIdOf(rule.Handle));

            default:
                return _devices.Windows.Find(rule.MatchValue, rule.Reading.Match,
                    rule.Reading.Compare, rule.Options) is { } window
                    ? One(_devices.Windows.ProcessIdOf(window.Handle))
                    : [];
        }

        // A window whose program the system will not name is no program at all.
        static IReadOnlyList<int> One(int id) => id == 0 ? [] : [id];
    }

    /// <summary>
    /// The process actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchProcess(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "process.start":
                StartProcess(step, depth);
                return Signal.Normal;
            case "process.waitFor":
                await WaitForProgram(step, depth, token);
                return Signal.Normal;
            case "process.waitExit":
                await WaitForExit(step, depth, token);
                return Signal.Normal;
            case "process.exists":
                ProgramRunning(step, depth);
                return Signal.Normal;
            case "process.list":
                ListPrograms(step, depth);
                return Signal.Normal;
            case "process.info":
                ProgramDetails(step, depth);
                return Signal.Normal;
            case "process.kill":
                StopProgram(step, depth);
                return Signal.Normal;
            case "command.run":
                RunCommand(step, depth);
                return Signal.Normal;

            // ----------------------------------------------------------------- system
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
