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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// The three languages a step can be written in, and what each one needs to start.
public sealed partial class MacroRunner
{
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

        _run.StoppedInsideScript = false;
        _run.ReturnedInsideScript = false;
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

        _run.StoppedInsideScript = false;
        _run.ReturnedInsideScript = false;
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

        _run.StoppedInsideScript = false;
        _run.ReturnedInsideScript = false;
        var outcome = _devices.Scripts.RunAutoHotkey(request,
            new MacroHost(this, step.Type, depth, token),
            line => Log(LogLevel.Info, depth, step.Type, LogKeys.ScriptOutput, line),
            line => Log(LogLevel.Error, depth, step.Type, LogKeys.ScriptError, line),
            token);

        HandOverScript(step, outcome, "value");
        Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, "AutoHotkey", outcome.Output.Length);

        return ScriptSignal();
    }
}
