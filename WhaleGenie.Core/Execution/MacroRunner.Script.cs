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
    private void RunScript(ExecutableStep step, int depth)
    {
        var kind = ScriptKindOf(step);

        // A script may be written in the step or named as a file on disk. A file is run where it
        // lies: nothing of it is copied, so what it reads beside itself, what it includes and what
        // a variable written into it would have been are exactly what its author wrote.
        var fromFile = string.Equals(ReadText(step, "scriptSource").Trim(), "file",
            StringComparison.OrdinalIgnoreCase);
        var chosen = fromFile ? ReadValue(step, "scriptFile").AsText().Trim() : string.Empty;
        var script = fromFile ? string.Empty : Template(ReadText(step, "script"));
        if (chosen.Length == 0 && script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        if (fromFile && !_devices.Files.Exists(chosen))
        {
            throw new StepFailure(RunKeys.FileNotFound, chosen);
        }

        var stamp = Guid.NewGuid().ToString("N");
        var path = fromFile
            ? _devices.Files.Resolve(chosen)
            : Path.Combine(ScriptFolder(), $"script-{stamp}{ScriptExtensionOf(step, kind)}");

        // The one kind whose window is left standing has to be told that the command is done: a
        // batch file sitting at its prompt says nothing to anyone but the person in front of it. A
        // line of its own at the end of the script writes the exit code where this side can read
        // it, and that is what lets the macro carry on when the command finishes rather than when
        // the window is closed.
        var sentinel = kind is ScriptKind.CommandKeepWindow
            ? Path.Combine(ScriptFolder(), $"done-{stamp}.txt")
            : string.Empty;

        // A script named as a file cannot have a line added to it, so the kind that needs the mark
        // gets a little batch file of its own to run it with: the mark still says when the command
        // is done, and the file that was named stays exactly as it was.
        var passed = Template(ReadText(step, "scriptArguments")).Trim();
        var wrapper = sentinel.Length > 0 && fromFile
            ? Path.Combine(ScriptFolder(), $"script-{stamp}.cmd")
            : string.Empty;

        // The command line is put together before anything is written: a step whose program or file
        // ending is missing fails without leaving a copy of a script behind.
        var (program, arguments) = ScriptCommandOf(step, kind, wrapper.Length > 0 ? wrapper : path,
            wrapper.Length > 0 ? string.Empty : passed);
        var folder = ScriptWorkingFolder(step);
        var timeout = Math.Max(0, step.Meta.TimeoutMs);

        if (wrapper.Length > 0)
        {
            _devices.Files.WriteText(wrapper, WithCaller(path, passed, sentinel), false,
                ScriptEncodingOf(step, kind));
        }
        else if (!fromFile)
        {
            _devices.Files.WriteText(path,
                sentinel.Length == 0 ? script : WithSentinel(script, sentinel),
                false, ScriptEncodingOf(step, kind));
        }

        var output = string.Empty;
        var error = string.Empty;
        var exitCode = 0;
        try
        {
            if (kind is ScriptKind.CommandKeepWindow or ScriptKind.CommandCloseWindow)
            {
                exitCode = RunInWindow(program, arguments, folder, sentinel, timeout);
            }
            else
            {
                var ran = _devices.Processes.Run(new CommandRequest(program, arguments, folder,
                    ScriptLimitOf(timeout), OutputEncoding: TextEncoding.System));
                output = ran.StandardOutput.Trim('\r', '\n');
                error = ran.StandardError.Trim();
                exitCode = ran.ExitCode;
            }
        }
        finally
        {
            // What is removed is what this step wrote: a script the macro named as a file is one of
            // the macro's own and is left where it is.
            TidyScript(fromFile ? wrapper : path, sentinel, depth, step.Type);
        }

        // What the script came to is handed over before the step is failed for it: a macro whose
        // failure rule lets it carry on — "continue", or the user answering the question — reads
        // the message and the flag on the steps after this one.
        Give(step, "output", Value.FromText(output));
        Give(step, "error", Value.FromText(error));
        Give(step, "success", Value.FromBool(exitCode == 0));

        if (exitCode != 0)
        {
            throw new StepFailure(RunKeys.ScriptFailed, Reason(exitCode, error));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, ScriptKindName(kind), output.Length);
    }

    /// <summary>
    /// Starts a command in a window of its own and waits for the command rather than for the
    /// window. The window is left for the person to read, which is the whole point of running one
    /// this way, so what ends the wait is either the process — a command run with /c, or a window
    /// the person closed — or the mark the script leaves behind when it is finished.
    /// </summary>
    private int RunInWindow(string program, string arguments, string folder, string sentinel,
        int timeoutMs)
    {
        var id = _devices.Processes.Start(new StartRequest(program, arguments, folder, Hidden: false));
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            if (_devices.Processes.HasExited(id))
            {
                return _devices.Processes.ExitCode(id) ?? 0;
            }

            if (sentinel.Length > 0 && _devices.Files.Exists(sentinel))
            {
                var written = _devices.Files.ReadText(sentinel, TextEncoding.Default).Trim();
                return int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var code)
                    ? code
                    : 0;
            }

            if (timeoutMs > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds > timeoutMs)
            {
                _devices.Processes.StopById(id, force: true);
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

    /// <summary>
    /// Runs a Python script in a process of its own with the macro in reach of it while it runs:
    /// what the script prints goes into the run log line by line, what it reads and writes is what
    /// the macro holds, and what it hands back with SetResult is this step's result.
    /// </summary>
    private Signal RunPython(ExecutableStep step, int depth, CancellationToken token)
    {
        var script = Template(ReadText(step, "script"));
        if (script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        var request = new PythonScript(script, Template(ReadText(step, "scriptArguments")).Trim(),
            ScriptWorkingFolder(step), ScriptTimeoutOf(step), ScriptInterpreter(step));

        _stoppedInsideScript = false;
        _returnedInsideScript = false;
        var outcome = _devices.Scripts.RunPython(request, new MacroHost(this, step.Type, depth, token),
            line => Log(LogLevel.Info, depth, step.Type, LogKeys.ScriptOutput, line),
            line => Log(LogLevel.Error, depth, step.Type, LogKeys.ScriptError, line),
            token);

        HandOverScript(step, outcome, "value");
        Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, "Python", outcome.Output.Length);

        // A step the script ran may have asked for the whole macro to stop, and a macro that was
        // told to stop stops: the script still gets its answer, and the run ends there.
        return ScriptSignal();
    }

    /// <summary>
    /// Compiles a C# script and runs it in a process of this very program, with the macro in reach
    /// of it while it runs. What it prints goes into the run log line by line, what it reads and
    /// writes is what the macro holds, and what it hands back with SetResult is this step's result.
    /// A script that does not compile is reported with what the compiler said about it.
    /// </summary>
    private Signal RunCSharp(ExecutableStep step, int depth, CancellationToken token)
    {
        var script = Template(ReadText(step, "script"));
        if (script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        var request = new CSharpScript(script, Template(ReadText(step, "scriptArguments")).Trim(),
            ScriptWorkingFolder(step), ScriptTimeoutOf(step));

        _stoppedInsideScript = false;
        _returnedInsideScript = false;
        var outcome = _devices.Scripts.RunCSharp(request, new MacroHost(this, step.Type, depth, token),
            line => Log(LogLevel.Info, depth, step.Type, LogKeys.ScriptOutput, line),
            line => Log(LogLevel.Error, depth, step.Type, LogKeys.ScriptError, line),
            token);

        HandOverScript(step, outcome, "value");
        Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, "C#", outcome.Output.Length);

        return ScriptSignal();
    }

    /// <summary>
    /// Runs an AutoHotkey script with the macro in reach of it: what the script prints goes into the
    /// run log line by line, what it reads and writes is what the macro holds, the steps it names are
    /// run by the macro, and what it hands back with SetResult is this step's result. Only the second
    /// version of AutoHotkey is what a script of this shape is written for.
    /// </summary>
    private Signal RunAutoHotkey(ExecutableStep step, int depth, CancellationToken token)
    {
        var script = Template(ReadText(step, "script"));
        if (script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        var request = new AutoHotkeyScript(script, Template(ReadText(step, "scriptArguments")).Trim(),
            ScriptWorkingFolder(step), ScriptTimeoutOf(step), ReadText(step, "ahk").Trim());

        _stoppedInsideScript = false;
        _returnedInsideScript = false;
        var outcome = _devices.Scripts.RunAutoHotkey(request,
            new MacroHost(this, step.Type, depth, token),
            line => Log(LogLevel.Info, depth, step.Type, LogKeys.ScriptOutput, line),
            line => Log(LogLevel.Error, depth, step.Type, LogKeys.ScriptError, line),
            token);

        HandOverScript(step, outcome, "value");
        Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, "AutoHotkey", outcome.Output.Length);

        return ScriptSignal();
    }

    /// <summary>
    /// Reads which PIP packages a step asks for and puts the ones that are not there yet in the
    /// folder scripts of this program import from. What went in is handed over as a list, so a
    /// macro can tell what this run of the step changed.
    /// </summary>
    private void RunPip(ExecutableStep step, int depth, CancellationToken token)
    {
        var wanted = ScriptPackages(step);
        if (wanted.Count == 0)
        {
            throw new StepFailure(RunKeys.NoPackages);
        }

        // How long a download may take is not a number this step has of its own: it is the step's
        // own timeout, and a step that says nothing about time is given as long as it takes.
        var outcome = _devices.Scripts.InstallPip(
            new PipRequest(wanted, ScriptInterpreter(step), Math.Max(0, step.Meta.TimeoutMs)),
            line => Log(LogLevel.Info, depth, step.Type, LogKeys.PackageOutput, line),
            line => Log(LogLevel.Error, depth, step.Type, LogKeys.PackageError, line), token);

        HandOverPackages(step, depth, outcome, RunKeys.PipFailed);
    }

    /// <summary>
    /// Brings down the NuGet packages a step names, with everything those packages need, so that a
    /// C# script can be compiled against them. A step whose packages are all there needs no network
    /// at all, and what this run really put there is handed over as a list.
    /// </summary>
    private void RunNuGet(ExecutableStep step, int depth, CancellationToken token)
    {
        var wanted = ScriptPackages(step);
        if (wanted.Count == 0)
        {
            throw new StepFailure(RunKeys.NoPackages);
        }

        var outcome = _devices.Scripts.InstallNuGet(
            new NuGetRequest(wanted, Math.Max(0, step.Meta.TimeoutMs)),
            line => Log(LogLevel.Info, depth, step.Type, LogKeys.PackageOutput, line),
            line => Log(LogLevel.Error, depth, step.Type, LogKeys.PackageError, line), token);

        HandOverPackages(step, depth, outcome, RunKeys.NuGetFailed);
    }

    /// <summary>
    /// Hands over what a package step came to and fails the step when it did not go well: which
    /// packages this run put there, what went wrong, and whether it worked. A run that ended badly
    /// hands the three over first, so a macro whose failure rule lets it carry on reads them.
    /// </summary>
    private void HandOverPackages(ExecutableStep step, int depth, ScriptResult outcome,
        string failed)
    {
        Give(step, "packages", outcome.Result);
        Give(step, "error", Value.FromText(outcome.Error.Trim()));
        Give(step, "success", Value.FromBool(
            outcome.FailureKey.Length == 0 && outcome.ExitCode == 0));

        if (outcome.FailureKey.Length > 0)
        {
            throw new StepFailure(outcome.FailureKey, Careful(outcome.Error));
        }

        if (outcome.ExitCode != 0)
        {
            throw new StepFailure(failed, Careful(outcome.Error));
        }

        foreach (var package in outcome.Result.AsList())
        {
            Log(LogLevel.Info, depth, step.Type, LogKeys.PackageInstalled, package.AsText());
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.PackageReady, outcome.Result.AsList().Count);
    }

    /// <summary>
    /// Hands over what a script of one of the programming languages came to, and fails the step
    /// when it did not go well: the value it handed back, the message it failed with, and whether
    /// it ran at all. A script that never got as far as running has a reason of its own — a
    /// compiler that would not have it, an entry point that is not there — and that reason is what
    /// the step fails with rather than a number.
    /// </summary>
    private void HandOverScript(ExecutableStep step, ScriptResult outcome, string resultKey)
    {
        Give(step, resultKey, outcome.Result);
        Give(step, "error", Value.FromText(outcome.Error.Trim()));
        Give(step, "success", Value.FromBool(
            outcome.FailureKey.Length == 0 && outcome.ExitCode == 0));

        if (outcome.FailureKey.Length > 0)
        {
            throw new StepFailure(outcome.FailureKey, Careful(outcome.Error));
        }

        if (outcome.ExitCode != 0)
        {
            throw new StepFailure(RunKeys.ScriptFailed, Reason(outcome.ExitCode, outcome.Error));
        }
    }

    /// <summary>
    /// Why a script that stopped with a code that is not zero is being failed, as the exit code and
    /// — when the script said anything on its error output — what it said. A script that stopped
    /// without a word is left as the number alone rather than as a colon with nothing after it.
    /// </summary>
    private static string Reason(int exitCode, string said)
    {
        var word = Careful(said);
        return word.Length == 0 ? $"{exitCode}" : $"{exitCode}: {word}";
    }

    /// <summary>
    /// The packages a step names, one to a line. A line that is blank, or that begins with a hash,
    /// is left out: a list kept by hand is easier to read with a word about what is in it.
    /// </summary>
    private IReadOnlyList<string> ScriptPackages(ExecutableStep step)
        => [.. Template(ReadText(step, "packages")).Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];

    /// <summary>The interpreter a Python step runs with, which is this machine's own by default.</summary>
    private string ScriptInterpreter(ExecutableStep step)
    {
        var named = ReadText(step, "python").Trim();
        return named.Length == 0 ? "python" : named;
    }

    /// <summary>How long a script may run, in milliseconds, or zero for as long as it takes.</summary>
    private int ScriptTimeoutOf(ExecutableStep step)
        => Math.Max(0, step.Meta.TimeoutMs);

    /// <summary>
    /// What a script step tells the run after the script is over. A step the script ran can ask
    /// the macro to stop or to return; either answer belongs to the macro rather than to the
    /// script, so it is carried out once the script has finished and its own result is filed.
    /// </summary>
    private Signal ScriptSignal()
        => _stoppedInsideScript ? Signal.Stop
            : _returnedInsideScript ? Signal.Return
            : Signal.Normal;

    /// <summary>
    /// Runs a step of the macro being run, found by the identity written in the step list, and says
    /// whether it ended well. A step that fails is the step's own failure — its rule decides what
    /// happens to the rest of the macro — while a step that asks for the whole macro to stop stops
    /// it, one that returns ends it, and one that leaves a loop it was never inside is the mistake
    /// it always was.
    /// </summary>
    private bool RunStepById(string id, string type, int depth, CancellationToken token)
    {
        var wanted = id.Trim();
        var step = FindStep(_steps, wanted) ?? throw new StepFailure(RunKeys.NoSuchStep, wanted);
        Log(LogLevel.Info, depth, type, LogKeys.ScriptStep, wanted);

        var signal = RunSteps([step], depth + 1, token).GetAwaiter().GetResult();
        if (signal is Signal.Break or Signal.Continue)
        {
            throw new StepFailure(LoopControlKey(signal), wanted);
        }

        if (signal is Signal.Stop)
        {
            _stoppedInsideScript = true;
        }

        if (signal is Signal.Return)
        {
            _returnedInsideScript = true;
        }

        return signal is not Signal.Failed;
    }

    /// <summary>The step with this identity, looking inside the blocks that hold steps as well.</summary>
    private static ExecutableStep? FindStep(IReadOnlyList<ExecutableStep> steps, string id)
    {
        foreach (var step in steps)
        {
            if (string.Equals(step.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return step;
            }

            foreach (var parameter in step.Parameters)
            {
                if (FindStep(parameter.Steps, id) is { } inside)
                {
                    return inside;
                }

                if (parameter.Condition is { } condition
                    && string.Equals(condition.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return condition;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The macro as a script sees it: the values it may read and write, the steps it may run, and
    /// somewhere to say something. It is the run the script is happening inside, so what a script
    /// reads is what the macro holds at that moment and what it writes is what the next step sees.
    /// </summary>
    private sealed class MacroHost(MacroRunner runner, string type, int depth, CancellationToken token)
        : IScriptHost
    {
        public Value? Read(string name) => runner.Variables.TryGet(name, out var value) ? value : null;

        public void Write(string name, Value value) => runner.Variables.Set(name, value);

        public bool RunStep(string id) => runner.RunStepById(id, type, depth, token);

        public void Print(string text) => runner.Log(LogLevel.Info, depth, type, LogKeys.ScriptSaid, text);
    }

    /// <summary>The kind of script a step asks for, said in the step's <c>kind</c> field.</summary>
    private static ScriptKind ScriptKindOf(ExecutableStep step)
        => step.Text("kind").Trim().ToLowerInvariant() switch
        {
            "" or "ps1" => ScriptKind.PowerShell,
            "cmdkeep" => ScriptKind.CommandKeepWindow,
            "cmdclose" => ScriptKind.CommandCloseWindow,
            "cmdhidden" => ScriptKind.CommandHidden,
            "bat" => ScriptKind.BatchFile,
            "cmdfile" => ScriptKind.CommandFile,
            "ahk" => ScriptKind.AutoHotkey,
            "custom" => ScriptKind.Custom,
            // A word the catalogue does not offer is refused rather than quietly read as one of the
            // kinds: a step that says something unknown is a step whose author meant something
            // else, and running it as PowerShell would hide that.
            _ => throw new StepFailure(RunKeys.UnknownScriptKind, step.Text("kind")),
        };

    /// <summary>
    /// A name for the kind of script, for the line the run log keeps. It is the name of the thing
    /// that ran rather than the value the field holds, because the log is read by a person.
    /// </summary>
    private static string ScriptKindName(ScriptKind kind) => kind switch
    {
        ScriptKind.CommandKeepWindow or ScriptKind.CommandCloseWindow or ScriptKind.CommandHidden
            or ScriptKind.CommandFile => "CMD",
        ScriptKind.BatchFile => "BAT",
        ScriptKind.AutoHotkey => "AutoHotkey",
        ScriptKind.Custom => "custom",
        _ => "PowerShell",
    };

    /// <summary>
    /// The command line that runs a script of this kind. PowerShell is given the flags that stop it
    /// from loading a profile or asking anything, so a script that runs inside a macro behaves the
    /// same way every time it runs. A program of the macro's own choosing gets the script's path
    /// after whatever the macro wrote, which is where an interpreter expects to find it and where
    /// a person typing the same command line would put it.
    /// </summary>
    private (string Program, string Arguments) ScriptCommandOf(ExecutableStep step,
        ScriptKind kind, string path, string extra)
    {
        var quoted = $"\"{path}\"";
        var tail = extra.Length == 0 ? string.Empty : $" {extra}";

        if (kind is ScriptKind.Custom)
        {
            var named = ReadText(step, "interpreter").Trim();
            if (named.Length == 0)
            {
                throw new StepFailure(RunKeys.MissingInterpreter);
            }

            var (program, flags) = CommandLine.Split(named);
            var start = flags.Length == 0 ? string.Empty : $"{flags} ";
            return (program, $"{start}{quoted}{tail}");
        }

        if (kind is ScriptKind.AutoHotkey)
        {
            return (AutoHotkey.Program() ?? throw new StepFailure(RunKeys.NoAutoHotkey),
                $"{quoted}{tail}");
        }

        return kind switch
        {
            ScriptKind.CommandKeepWindow => ("cmd.exe", $"/k {quoted}{tail}"),
            ScriptKind.CommandCloseWindow or ScriptKind.CommandHidden
                or ScriptKind.BatchFile or ScriptKind.CommandFile => ("cmd.exe", $"/c {quoted}{tail}"),
            _ => ("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {quoted}{tail}"),
        };
    }

    /// <summary>
    /// The file name ending the program recognises, which is how it knows what it is reading. The
    /// kinds that name a program by itself are each one ending; a program of the macro's own
    /// choosing has to be told.
    /// </summary>
    private string ScriptExtensionOf(ExecutableStep step, ScriptKind kind)
    {
        switch (kind)
        {
            case ScriptKind.CommandKeepWindow:
            case ScriptKind.CommandCloseWindow:
            case ScriptKind.CommandHidden:
            case ScriptKind.CommandFile:
                return ".cmd";
            case ScriptKind.BatchFile:
                return ".bat";
            case ScriptKind.PowerShell:
                return ".ps1";
            case ScriptKind.AutoHotkey:
                return ".ahk";
        }

        var wanted = ReadText(step, "extension").Trim();
        if (wanted.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScriptExtension);
        }

        return wanted.StartsWith('.') ? wanted : $".{wanted}";
    }

    /// <summary>
    /// How the copy of the script handed over is written. Windows PowerShell and AutoHotkey read a
    /// script as the system code page unless the file starts with a byte-order mark, so a script
    /// with Chinese text in it needs one; cmd.exe reads in this machine's own code page and a batch
    /// file with a mark on it does not start at all, so the command kinds get that one. A program
    /// of the macro's own choosing gets plain UTF-8, which is what a program from this decade
    /// reads. A macro that knows better can say so, and the reason it can is cscript: that one
    /// reads the system code page by design, so Chinese text in a .vbs has to be GBK.
    /// </summary>
    private static string ScriptEncodingOf(ExecutableStep step, ScriptKind kind)
    {
        var chosen = step.Text("encoding").Trim();
        if (chosen.Length > 0 && !string.Equals(chosen, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return chosen;
        }

        return kind switch
        {
            ScriptKind.PowerShell or ScriptKind.AutoHotkey => "utf8bom",
            ScriptKind.Custom => TextEncoding.Default,
            _ => TextEncoding.System,
        };
    }

    /// <summary>
    /// The folder a script runs in. A script that reads or writes files next to the macro should
    /// not have to be told where that is, so the macros folder is what an empty working folder
    /// means — but only when that folder is really there: a program cannot be started in a folder
    /// that does not exist, and an empty folder field is better than a script that never runs.
    /// </summary>
    private string ScriptWorkingFolder(ExecutableStep step)
    {
        var folder = ReadValue(step, "folder").AsText().Trim();
        return folder.Length == 0 && _devices.Files.Exists(_devices.Files.BaseFolder)
            ? _devices.Files.BaseFolder
            : folder;
    }

    /// <summary>
    /// Removes the copies of the script the run left on disk: the script itself, and the mark the
    /// one kind that needs one writes. The script lives in the macro, so what is out there was only
    /// ever there for as long as the program needed to read it, and a tidy-up that fails is not
    /// worth failing the step over.
    /// </summary>
    private void TidyScript(string path, string sentinel, int depth, string type)
    {
        foreach (var file in new[] { path, sentinel })
        {
            if (file.Length == 0)
            {
                continue;
            }

            try
            {
                _devices.Files.Delete(file, false);
            }
            catch (Exception)
            {
                Log(LogLevel.Debug, depth, type, LogKeys.ScriptNotTidied, file);
            }
        }
    }

    /// <summary>Where the copies of a script are written while it runs.</summary>
    private static string ScriptFolder() => Path.Combine(Path.GetTempPath(), "WhaleGenie");

    /// <summary>
    /// The time a command is given, turned into one the process API understands: a step that asked
    /// for no limit is given one long enough that nobody reading the number is waiting for it.
    /// </summary>
    private static int ScriptLimitOf(int timeoutMs) => timeoutMs <= 0 ? int.MaxValue : timeoutMs;

    /// <summary>
    /// Fills the <c>{{name}}</c> placeholders with what those variables hold. A script is written in
    /// a language of its own — PowerShell, JavaScript, a batch file — and every one of them gives
    /// its own meaning to <c>$</c> and <c>%</c>, so a macro value is spelled differently in here on
    /// purpose. A name the macro does not know is left as it was written, where it is easy to see
    /// rather than quietly replaced by nothing.
    /// </summary>
    private string Template(string text)
        => Regex.Replace(text, @"\{\{(.+?)\}\}", match =>
        {
            var name = match.Groups[1].Value.Trim();
            return Variables.TryGet(name, out var value) ? value.AsText() : match.Value;
        });

    /// <summary>What a script said on its error output, kept short enough to read in a message.</summary>
    private static string Careful(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }

    /// <summary>
    /// Reads a selector such as <c>Button[name='Save']</c> or <c>Edit[automationId='input']</c>,
    /// together with the window it is looked for in. That window is recognised by the same rule
    /// every other step recognises one with, and the search is narrowed by it rather than by a title
    /// alone: a step that aims at a window by its program or by a pattern finds the same window a
    /// window action would.
    /// </summary>
    private UiQuery Query(ExecutableStep step)
    {
        var rule = WindowRuleOf(step, "window", "how");
        return UiQuery.Parse(ReadText(step, "uiSelector"), rule.Text, Index(step, "matchIndex", 1),
            rule.Match, rule.Compare, rule.Options);
    }

    /// <summary>
    /// The same, for the control a step measures its coordinates from. A step that is anchored to a
    /// control keeps its own pair of fields, so the two readings never take each other's values.
    /// </summary>
    private UiQuery AnchorQuery(ExecutableStep step)
    {
        var rule = WindowRuleOf(step, "anchorWindow", "anchorHow");
        return UiQuery.Parse(ReadText(step, "anchorSelector"), rule.Text, 1, rule.Match,
            rule.Compare, rule.Options);
    }

    /// <summary>
    /// A yes/no setting. A step that says nothing takes the catalogue's own default, so a
    /// missing switch behaves the way the action editor showed it rather than always being on.
    /// </summary>
    private static bool Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static LogLevel Level(string level) => level.ToLowerInvariant() switch
    {
        "warn" => LogLevel.Warn,
        "error" => LogLevel.Error,
        "debug" => LogLevel.Debug,
        _ => LogLevel.Info,
    };

    private static async Task Pause(int milliseconds, CancellationToken token)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking again every <paramref name="interval"/> and giving
    /// up after <paramref name="timeoutMs"/>. Waiting is the same thing whether it is an element, a
    /// window, a program or a condition the macro wrote itself, so the pacing and the way a timeout
    /// is counted are written once. The question is asked before the clock is read, so a wait that
    /// already holds costs nothing. Zero means no limit of its own: the step's cancellation token is
    /// then what ends the wait, including a step that was never given a timeout and runs until the
    /// macro is stopped.
    /// </summary>
    /// <summary>
    /// The beat a step that looks at the screen keeps: what the step wrote, or one refresh of the
    /// screen it is running on when it wrote nothing. A picture that has not been drawn again
    /// cannot differ from the one just read, so a look that comes sooner reads the same frame
    /// twice. That is 16 ms on a 60 Hz display and 4 ms on a 240 Hz one, which is why an empty
    /// cell follows the screen rather than the machine the macro was written on.
    /// </summary>
    private int LookBeat(ExecutableStep step) => Math.Max(1, Number(step, "intervalMs"));

    private async Task<bool> WaitForFlagAsync(Func<bool> ready, int timeoutMs, int interval,
        CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (ready())
            {
                return true;
            }

            if (timeoutMs > 0
                && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return false;
            }

            await Pause(interval, token);
        }
    }

    /// <summary>The same wait, for something that turns up rather than becomes true.</summary>
    private async Task<T?> WaitForValueAsync<T>(Func<T?> probe, int timeoutMs, int interval,
        CancellationToken token)
        where T : class
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            if (timeoutMs > 0
                && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return null;
            }

            await Pause(interval, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking again every <paramref name="interval"/> until it
    /// does. How long that may take is not the wait's to say: the step's own timeout is what ends
    /// it, which is what makes the length of a wait a setting of the step rather than a field that
    /// says the same thing a second time.
    /// </summary>
    private static async Task WaitForFlagAsync(Func<bool> ready, int interval, CancellationToken token)
    {
        while (!ready())
        {
            await Pause(interval, token);
        }
    }

    /// <summary>
    /// The script actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchScript(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "script.run":
                RunScript(step, depth);
                return Signal.Normal;
            case "script.python":
                return RunPython(step, depth, token);
            case "script.pip":
                RunPip(step, depth, token);
                return Signal.Normal;
            case "script.csharp":
                return RunCSharp(step, depth, token);
            case "script.ahk":
                return RunAutoHotkey(step, depth, token);
            case "script.nuget":
                RunNuGet(step, depth, token);
                return Signal.Normal;
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
