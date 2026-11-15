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

// Packages a script may install, and what the run hands back when it ends.
public sealed partial class MacroRunner
{
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
        Give(step, "exitCode", Value.FromNumber(outcome.ExitCode));
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

        // 退出码单独交回一份，而不是只留一个「成功与否」：脚本常用不同的代码说不同的意思，
        // 失败规则放行之后，后面的步骤要按它分支就得看得见这个数。
        Give(step, "exitCode", Value.FromNumber(outcome.ExitCode));
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
}
