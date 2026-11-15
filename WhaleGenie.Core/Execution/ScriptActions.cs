using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Running a script the step carries: writing a copy where an interpreter can read it, starting
/// that interpreter, and handing over what the script came to. A command that keeps its window is
/// waited on through a mark the script writes rather than through the window the person is
/// reading.
/// </summary>
internal static class ScriptActions
{
    /// <summary>
    /// The script actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["script.run"] = (run, step, depth, _) =>
            {
                RunScript(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["script.python"] = (run, step, depth, token) =>
                Task.FromResult(ScriptLanguages.RunPython(run, step, depth, token)),

            ["script.pip"] = (run, step, depth, token) =>
            {
                ScriptLanguages.RunPip(run, step, depth, token);
                return Task.FromResult(Signal.Normal);
            },

            ["script.csharp"] = (run, step, depth, token) =>
                Task.FromResult(ScriptLanguages.RunCSharp(run, step, depth, token)),

            ["script.ahk"] = (run, step, depth, token) =>
                Task.FromResult(ScriptLanguages.RunAutoHotkey(run, step, depth, token)),

            ["script.nuget"] = (run, step, depth, token) =>
            {
                ScriptLanguages.RunNuGet(run, step, depth, token);
                return Task.FromResult(Signal.Normal);
            },
        };

    private static void RunScript(IRunContext run, ExecutableStep step, int depth)
    {
        var kind = ScriptFiles.KindOf(step);
        var args = run.Args(step);

        // A script may be written in the step or named as a file on disk. A file is run where it
        // lies: nothing of it is copied, so what it reads beside itself, what it includes and what
        // a variable written into it would have been are exactly what its author wrote.
        var fromFile = string.Equals(args.Text("scriptSource").Trim(), "file",
            StringComparison.OrdinalIgnoreCase);
        var chosen = fromFile ? args.ValueOf("scriptFile").AsText().Trim() : string.Empty;
        var script = fromFile ? string.Empty : Templates.Fill(args.Text("script"), run.Variables);
        if (chosen.Length == 0 && script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        if (fromFile && !run.Devices.Files.Exists(chosen))
        {
            throw new StepFailure(RunKeys.FileNotFound, chosen);
        }

        var stamp = Guid.NewGuid().ToString("N");
        var path = fromFile
            ? run.Devices.Files.Resolve(chosen)
            : Path.Combine(ScriptFiles.Folder(),
                $"script-{stamp}{ScriptFiles.ExtensionOf(run, step, kind)}");

        // The one kind whose window is left standing has to be told that the command is done: a
        // batch file sitting at its prompt says nothing to anyone but the person in front of it. A
        // line of its own at the end of the script writes the exit code where this side can read
        // it, and that is what lets the macro carry on when the command finishes rather than when
        // the window is closed.
        var sentinel = kind is ScriptKind.CommandKeepWindow
            ? Path.Combine(ScriptFiles.Folder(), $"done-{stamp}.txt")
            : string.Empty;

        // A script named as a file cannot have a line added to it, so the kind that needs the mark
        // gets a little batch file of its own to run it with: the mark still says when the command
        // is done, and the file that was named stays exactly as it was.
        var passed = Templates.Fill(args.Text("scriptArguments"), run.Variables).Trim();
        var wrapper = sentinel.Length > 0 && fromFile
            ? Path.Combine(ScriptFiles.Folder(), $"script-{stamp}.cmd")
            : string.Empty;

        // The command line is put together before anything is written: a step whose program or file
        // ending is missing fails without leaving a copy of a script behind.
        var (program, arguments) = ScriptFiles.CommandOf(run, step, kind,
            wrapper.Length > 0 ? wrapper : path, wrapper.Length > 0 ? string.Empty : passed);
        var folder = ScriptFiles.WorkingFolder(run, step);
        var timeout = Math.Max(0, step.Meta.TimeoutMs);

        if (wrapper.Length > 0)
        {
            run.Devices.Files.WriteText(wrapper, WithCaller(path, passed, sentinel), false,
                ScriptFiles.EncodingOf(step, kind));
        }
        else if (!fromFile)
        {
            run.Devices.Files.WriteText(path,
                sentinel.Length == 0 ? script : WithSentinel(script, sentinel),
                false, ScriptFiles.EncodingOf(step, kind));
        }

        var output = string.Empty;
        var error = string.Empty;
        var exitCode = 0;
        try
        {
            if (kind is ScriptKind.CommandKeepWindow or ScriptKind.CommandCloseWindow)
            {
                exitCode = RunInWindow(run, program, arguments, folder, sentinel, timeout);
            }
            else
            {
                var ran = run.Devices.Processes.Run(new CommandRequest(program, arguments, folder,
                    ScriptFiles.LimitOf(timeout), OutputEncoding: TextEncoding.System));
                output = ran.StandardOutput.Trim('\r', '\n');
                error = ran.StandardError.Trim();
                exitCode = ran.ExitCode;
            }
        }
        finally
        {
            // What is removed is what this step wrote: a script the macro named as a file is one of
            // the macro's own and is left where it is.
            ScriptFiles.Tidy(run, fromFile ? wrapper : path, sentinel, depth, step.Type);
        }

        // What the script came to is handed over before the step is failed for it: a macro whose
        // failure rule lets it carry on — "continue", or the user answering the question — reads
        // the message and the flag on the steps after this one.
        run.Give(step, "output", Value.FromText(output));
        run.Give(step, "error", Value.FromText(error));
        run.Give(step, "exitCode", Value.FromNumber(exitCode));
        run.Give(step, "success", Value.FromBool(exitCode == 0));

        if (exitCode != 0)
        {
            throw new StepFailure(RunKeys.ScriptFailed,
                ScriptLanguages.Reason(exitCode, error));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript,
            ScriptFiles.KindName(kind), output.Length);
    }

    /// <summary>
    /// Starts a command in a window of its own and waits for the command rather than for the
    /// window. The window is left for the person to read, which is the whole point of running one
    /// this way, so what ends the wait is either the process — a command run with /c, or a window
    /// the person closed — or the mark the script leaves behind when it is finished.
    /// </summary>
    private static int RunInWindow(IRunContext run, string program, string arguments, string folder,
        string sentinel, int timeoutMs)
    {
        var id = run.Devices.Processes.Start(new StartRequest(program, arguments, folder,
            Hidden: false));
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            if (run.Devices.Processes.HasExited(id))
            {
                return run.Devices.Processes.ExitCode(id) ?? 0;
            }

            if (sentinel.Length > 0 && run.Devices.Files.Exists(sentinel))
            {
                var written = run.Devices.Files.ReadText(sentinel, TextEncoding.Default).Trim();
                return int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var code)
                    ? code
                    : 0;
            }

            if (timeoutMs > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds > timeoutMs)
            {
                run.Devices.Processes.StopById(id, force: true);
                throw new StepFailure(RunKeys.CommandTimeout, program);
            }

            // A window a person may be reading is the one thing the engine waits on in steps of its
            // own: there is no picture to watch and no event to listen to, only a command that has
            // not finished yet.
            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// The script with a last line that writes the exit code where this side can read it. The line
    /// is ended the way the script already ends its own lines, so a file cmd.exe reads correctly
    /// goes on being read correctly with one more line on the end; a script written as a single
    /// line gets the ending a batch file is used to.
    /// </summary>
    private static string WithSentinel(string script, string sentinel)
    {
        var ending = script.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
            : script.EndsWith('\n') ? "\n"
            : "\r\n";
        var line = $"@echo %errorlevel% > \"{sentinel}\"{ending}";
        return script.EndsWith('\n') ? script + line : script + ending + line;
    }

    /// <summary>
    /// A small batch file that runs the file a macro named and then writes the exit code where this
    /// side reads it. It is what the kind whose window is left standing runs when its script is a
    /// file: the mark has to be written by something, and the file itself may not be touched, so
    /// something of this program's own is written to call it instead.
    /// </summary>
    private static string WithCaller(string script, string arguments, string sentinel)
    {
        var tail = arguments.Length == 0 ? string.Empty : $" {arguments}";
        return "@echo off" + Environment.NewLine
            + $"call \"{script}\"{tail}" + Environment.NewLine
            + $"@echo %errorlevel% > \"{sentinel}\"" + Environment.NewLine;
    }
}
