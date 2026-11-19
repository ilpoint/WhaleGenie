using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The three languages a step can be written in — Python, C# and AutoHotkey — and the packages
/// those languages need. Each runs in a process of its own with the macro in reach: what the
/// script prints goes into the run log line by line, what it reads and writes is what the macro
/// holds, and what it hands back is this step's result.
/// </summary>
internal static class ScriptLanguages
{
    /// <summary>Runs a Python script with the macro in reach of it while it runs.</summary>
    public static Signal RunPython(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var script = Templates.Fill(run.Args(step).Text("script"), run.Variables);
        if (script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        var request = new PythonScript(script,
            Templates.Fill(run.Args(step).Text("scriptArguments"), run.Variables).Trim(),
            ScriptFiles.WorkingFolder(run, step), TimeoutOf(step), InterpreterOf(run, step));

        run.BeginScript();
        var outcome = run.Devices.Scripts.RunPython(request, Host(run, step, depth, token),
            line => run.Log(LogLevel.Info, depth, step.Type, LogKeys.ScriptOutput, line),
            line => run.Log(LogLevel.Error, depth, step.Type, LogKeys.ScriptError, line),
            token);

        HandOverScript(run, step, outcome, "value");
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, "Python",
            outcome.Output.Length);

        // A step the script ran may have asked for the whole macro to stop, and a macro that was
        // told to stop stops: the script still gets its answer, and the run ends there.
        return run.EndScript();
    }

    /// <summary>
    /// Compiles a C# script and runs it in a process of this very program, with the macro in reach
    /// of it while it runs. A script that does not compile is reported with what the compiler said
    /// about it.
    /// </summary>
    public static Signal RunCSharp(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var script = Templates.Fill(run.Args(step).Text("script"), run.Variables);
        if (script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        var request = new CSharpScript(script,
            Templates.Fill(run.Args(step).Text("scriptArguments"), run.Variables).Trim(),
            ScriptFiles.WorkingFolder(run, step), TimeoutOf(step));

        run.BeginScript();
        var outcome = run.Devices.Scripts.RunCSharp(request, Host(run, step, depth, token),
            line => run.Log(LogLevel.Info, depth, step.Type, LogKeys.ScriptOutput, line),
            line => run.Log(LogLevel.Error, depth, step.Type, LogKeys.ScriptError, line),
            token);

        HandOverScript(run, step, outcome, "value");
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, "C#", outcome.Output.Length);

        return run.EndScript();
    }

    /// <summary>
    /// Runs an AutoHotkey script with the macro in reach of it. Only the second version of
    /// AutoHotkey is what a script of this shape is written for.
    /// </summary>
    public static Signal RunAutoHotkey(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var script = Templates.Fill(run.Args(step).Text("script"), run.Variables);
        if (script.Trim().Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScript);
        }

        var request = new AutoHotkeyScript(script,
            Templates.Fill(run.Args(step).Text("scriptArguments"), run.Variables).Trim(),
            ScriptFiles.WorkingFolder(run, step), TimeoutOf(step),
            run.Args(step).Text("ahk").Trim());

        run.BeginScript();
        var outcome = run.Devices.Scripts.RunAutoHotkey(request, Host(run, step, depth, token),
            line => run.Log(LogLevel.Info, depth, step.Type, LogKeys.ScriptOutput, line),
            line => run.Log(LogLevel.Error, depth, step.Type, LogKeys.ScriptError, line),
            token);

        HandOverScript(run, step, outcome, "value");
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RanScript, "AutoHotkey",
            outcome.Output.Length);

        return run.EndScript();
    }

    /// <summary>
    /// Reads which PIP packages a step asks for and puts the ones that are not there yet in the
    /// folder scripts of this program import from. What went in is handed over as a list, so a
    /// macro can tell what this run of the step changed.
    /// </summary>
    public static void RunPip(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var wanted = Packages(run, step);
        if (wanted.Count == 0)
        {
            throw new StepFailure(RunKeys.NoPackages);
        }

        // How long a download may take is not a number this step has of its own: it is the step's
        // own timeout, and a step that says nothing about time is given as long as it takes.
        var outcome = run.Devices.Scripts.InstallPip(
            new PipRequest(wanted, InterpreterOf(run, step), Math.Max(0, step.Meta.TimeoutMs)),
            line => run.Log(LogLevel.Info, depth, step.Type, LogKeys.PackageOutput, line),
            line => run.Log(LogLevel.Error, depth, step.Type, LogKeys.PackageError, line), token);

        HandOverPackages(run, step, depth, outcome, RunKeys.PipFailed);
    }

    /// <summary>
    /// Brings down the NuGet packages a step names, with everything those packages need, so that a
    /// C# script can be compiled against them. A step whose packages are all there needs no network
    /// at all, and what this run really put there is handed over as a list.
    /// </summary>
    public static void RunNuGet(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var wanted = Packages(run, step);
        if (wanted.Count == 0)
        {
            throw new StepFailure(RunKeys.NoPackages);
        }

        var outcome = run.Devices.Scripts.InstallNuGet(
            new NuGetRequest(wanted, Math.Max(0, step.Meta.TimeoutMs)),
            line => run.Log(LogLevel.Info, depth, step.Type, LogKeys.PackageOutput, line),
            line => run.Log(LogLevel.Error, depth, step.Type, LogKeys.PackageError, line), token);

        HandOverPackages(run, step, depth, outcome, RunKeys.NuGetFailed);
    }

    /// <summary>
    /// Hands over what a package step came to and fails the step when it did not go well: which
    /// packages this run put there, what went wrong, and whether it worked. A run that ended badly
    /// hands the three over first, so a macro whose failure rule lets it carry on reads them.
    /// </summary>
    private static void HandOverPackages(IRunContext run, ExecutableStep step, int depth,
        ScriptResult outcome, string failed)
    {
        run.Give(step, "packages", outcome.Result);
        run.Give(step, "error", Value.FromText(outcome.Error.Trim()));
        run.Give(step, "exitCode", Value.FromNumber(outcome.ExitCode));
        run.Give(step, "success", Value.FromBool(
            outcome.FailureKey.Length == 0 && outcome.ExitCode == 0));

        if (outcome.FailureKey.Length > 0)
        {
            throw new StepFailure(outcome.FailureKey, ScriptFiles.Careful(outcome.Error));
        }

        if (outcome.ExitCode != 0)
        {
            throw new StepFailure(failed, ScriptFiles.Careful(outcome.Error));
        }

        foreach (var package in outcome.Result.AsList())
        {
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.PackageInstalled, package.AsText());
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.PackageReady,
            outcome.Result.AsList().Count);
    }

    /// <summary>
    /// Hands over what a script of one of the programming languages came to, and fails the step
    /// when it did not go well: the value it handed back, the message it failed with, and whether
    /// it ran at all. A script that never got as far as running has a reason of its own — a
    /// compiler that would not have it, an entry point that is not there — and that reason is what
    /// the step fails with rather than a number.
    /// </summary>
    private static void HandOverScript(IRunContext run, ExecutableStep step, ScriptResult outcome,
        string resultKey)
    {
        run.Give(step, resultKey, outcome.Result);
        run.Give(step, "error", Value.FromText(outcome.Error.Trim()));

        // 退出码单独交回一份，而不是只留一个「成功与否」：脚本常用不同的代码说不同的意思，
        // 失败规则放行之后，后面的步骤要按它分支就得看得见这个数。
        run.Give(step, "exitCode", Value.FromNumber(outcome.ExitCode));
        run.Give(step, "success", Value.FromBool(
            outcome.FailureKey.Length == 0 && outcome.ExitCode == 0));

        if (outcome.FailureKey.Length > 0)
        {
            throw new StepFailure(outcome.FailureKey, ScriptFiles.Careful(outcome.Error));
        }

        if (outcome.ExitCode != 0)
        {
            throw new StepFailure(RunKeys.ScriptFailed,
                Reason(outcome.ExitCode, outcome.Error));
        }
    }

    /// <summary>
    /// Why a script that stopped with a code that is not zero is being failed, as the exit code and
    /// — when the script said anything on its error output — what it said. A script that stopped
    /// without a word is left as the number alone rather than as a colon with nothing after it.
    /// </summary>
    public static string Reason(int exitCode, string said)
    {
        var word = ScriptFiles.Careful(said);
        return word.Length == 0 ? $"{exitCode}" : $"{exitCode}: {word}";
    }

    /// <summary>
    /// The packages a step names, one to a line. A line that is blank, or that begins with a hash,
    /// is left out: a list kept by hand is easier to read with a word about what is in it.
    /// </summary>
    private static IReadOnlyList<string> Packages(IRunContext run, ExecutableStep step)
        => [.. Templates.Fill(run.Args(step).Text("packages"), run.Variables).Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];

    /// <summary>The interpreter a Python step runs with, which is this machine's own by default.</summary>
    private static string InterpreterOf(IRunContext run, ExecutableStep step)
    {
        var named = run.Args(step).Text("python").Trim();
        return named.Length == 0 ? "python" : named;
    }

    /// <summary>How long a script may run, in milliseconds, or zero for as long as it takes.</summary>
    private static int TimeoutOf(ExecutableStep step) => Math.Max(0, step.Meta.TimeoutMs);

    /// <summary>The macro as the script being run sees it.</summary>
    private static IScriptHost Host(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token) => new ScriptHost(run, step.Type, depth, token);
}
