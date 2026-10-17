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

    /// <summary>Starts another program and keeps its process id.</summary>
    private void StartProcess(ExecutableStep step, int depth)
    {
        var program = ReadValue(step, "programFile").AsText();
        var arguments = ReadValue(step, "commandArguments").AsText();
        var folder = ReadValue(step, "workingDirectory").AsText();
        var hidden = Flag(step, "hidden", true);
        var runAsAdmin = Flag(step, "runAsAdmin", false);

        var id = _devices.Processes.Start(
            new StartRequest(program, arguments, folder, hidden, runAsAdmin, EnvironmentOf(step)));
        Give(step, "processId", Value.FromNumber(id));
        Log(LogLevel.Info, depth, step.Type, LogKeys.StartedProgram, program, id);
    }

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

    /// <summary>Waits until a program with the given name shows up in the process list.</summary>
    private async Task WaitForProgram(ExecutableStep step, int depth, CancellationToken token)
    {
        var name = ReadValue(step, "processName").AsText();
        var timeout = step.Meta.TimeoutMs;

        var ids = await WaitForValueAsync(
            () => _devices.Processes.Find(name) is { Count: > 0 } found ? found : null,
            timeout, 100, token);
        if (ids is null)
        {
            throw new StepFailure(RunKeys.ProgramNotFound, name);
        }

        Give(step, "process", Value.FromNumber(ids[0]));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ProgramFound, name, ids[0]);
    }

    /// <summary>Waits for a program to finish and keeps the exit code it returned.</summary>
    private async Task WaitForExit(ExecutableStep step, int depth, CancellationToken token)
    {
        var id = ProcessId(step);
        var timeout = step.Meta.TimeoutMs;

        if (!await WaitForFlagAsync(() => _devices.Processes.HasExited(id), timeout, 100, token))
        {
            throw new StepFailure(RunKeys.ProcessTimeout, id.ToString(CultureInfo.InvariantCulture));
        }

        var code = _devices.Processes.ExitCode(id) ?? 0;
        Give(step, "exitCode", Value.FromNumber(code));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ProcessExited, id, code);
    }

    /// <summary>Leaves true or false behind, depending on whether the program is running.</summary>
    private void ProgramRunning(ExecutableStep step, int depth)
    {
        var name = ReadValue(step, "processName").AsText();
        var running = _devices.Processes.Find(name).Count > 0;
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
        var target = ReadValue(step, "processTarget").AsText().Trim();
        var details = _devices.Processes.Details(target)
            ?? throw new StepFailure(RunKeys.NoSuchProcess, target);

        // One program is one record, the way one window is: what it is called, what it is, and what
        // it is costing are answers to the same question and travel together.
        Give(step, "program", Value.FromRecord(
        [
            (FieldNames.Info, Value.FromText(details.Path)),
            (FieldNames.Id, Value.FromNumber(details.Id)),
            (FieldNames.Name, Value.FromText(details.Name)),
            (FieldNames.MemoryMb, Value.FromNumber(details.MemoryMb)),
            (FieldNames.CpuSeconds, Value.FromNumber(details.CpuSeconds)),
        ]));

        Log(LogLevel.Info, depth, step.Type, LogKeys.ProgramDetails, details.Name, details.Id);
    }

    /// <summary>Closes a program, by name or by process id.</summary>
    private void StopProgram(ExecutableStep step, int depth)
    {
        var target = ReadValue(step, "processTarget").AsText().Trim();
        var force = Flag(step, "force", false);

        var stopped = int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? (_devices.Processes.StopById(id, force) ? 1 : 0)
            : _devices.Processes.StopByName(target, force);

        Give(step, "stopped", Value.FromNumber(stopped));
        Log(LogLevel.Info, depth, step.Type, LogKeys.StoppedProgram, target, stopped);
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
