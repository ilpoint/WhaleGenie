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

// Running a list of steps: the signal a step leaves, the outcomes written down, and a held body.
public sealed partial class MacroRunner
{
    /// <summary>
    /// How the name of a step's outcome is built: <c>k3f9</c> is answered by
    /// <c>step.k3f9.outcome</c>. It travels as a name the run reads rather than as something the
    /// interface keeps, because a macro that asks about a step has to mean the same thing however
    /// the macro was started.
    /// </summary>
    private const string OutcomePrefix = "step.";

    /// <summary>The other end of an outcome's name.</summary>
    private const string OutcomeSuffix = ".outcome";

    /// <summary>
    /// Where the outcome of the step with this name is written. Anything that lists the run's own
    /// values asks here rather than spelling the name out a second time.
    /// </summary>
    /// <remarks>
    /// One value per step, not two: how it ended is what a macro branches on, and the reason it
    /// failed is only ever read inside a <c>try</c>, where <see cref="CaughtKeyName"/> already holds
    /// it. A second name for every step would double a list that is already one row per step.
    /// </remarks>
    public static string OutcomeName(string stepId) => OutcomePrefix + stepId + OutcomeSuffix;

    /// <summary>
    /// The failure key a catch block reads, set when the block is entered and put back when it is
    /// left. A condition and a step see the same name, so "catch only this reason" and "rethrow
    /// it" are written against one answer rather than a remembered one.
    /// </summary>
    public const string CaughtKeyName = "caught.key";

    /// <summary>The detail that goes with <see cref="CaughtKeyName"/>.</summary>
    public const string CaughtDetailName = "caught.detail";

    /// <summary>
    /// True when a value is one of the names the run keeps about a step — how it ended — rather than
    /// something the macro made for itself. A macro of any length leaves one behind for every step,
    /// so a list that showed them all mixed in with the macro's own values would be mostly these.
    /// </summary>
    public static bool IsRunStepName(string name)
        => name.StartsWith(OutcomePrefix, StringComparison.Ordinal)
           && name.EndsWith(OutcomeSuffix, StringComparison.Ordinal);

    /// <summary>
    /// True when a macro may not go by this name: the program keeps everything under
    /// <c>sys.</c>, and the run keeps one ending per step. A declaration that took one of those
    /// would read as the built-in or the ending it shadows.
    /// </summary>
    public static bool IsReservedName(string name)
        => VariableName.IsSystem(name) || IsRunStepName(name);

    /// <summary>This step did what it was asked to.</summary>
    private const string OutcomeOk = "ok";

    /// <summary>This step did not work, whether it stopped the run or its rule let the run go on.</summary>
    private const string OutcomeFailed = "failed";

    /// <summary>This step was not carried out: switched off, or the run never reached it.</summary>
    private const string OutcomeSkipped = "skipped";

    /// <summary>
    /// Says "not carried out yet" for every step of a macro before any of it runs, so a step the
    /// run never got to can be told apart from a name that is not a step at all — a condition
    /// about a step further down has nothing to read otherwise, and nothing reads the same as a
    /// name typed wrong.
    /// </summary>
    /// <remarks>
    /// A called macro is prepared too, on the values it was given: its steps are not in the
    /// caller's list, and its names have no business in the caller's scope.
    /// </remarks>
    private void PrepareOutcomes(IReadOnlyList<ExecutableStep> steps)
    {
        foreach (var step in steps)
        {
            WriteOutcome(step, OutcomeSkipped);
            foreach (var parameter in step.Parameters)
            {
                PrepareOutcomes(parameter.Steps);
            }
        }
    }

    /// <summary>
    /// Writes down how one step ended and, when it did not work, why — for the conditions written
    /// after it.
    /// </summary>
    private void WriteOutcome(ExecutableStep step, string outcome)
    {
        // A step without a name cannot be pointed at, so there is nothing to write. The editor
        // gives every step one; this is for a macro built by hand around the engine.
        if (step.Id.Length == 0)
        {
            return;
        }

        Variables.Set(OutcomeName(step.Id), Value.FromText(outcome));
    }

    /// <summary>What the failure rule decided for one step.</summary>
    private enum Decision
    {
        Retry,
        Skip,
        NextIteration,
        Stop,
    }

    private async Task<Signal> RunSteps(IReadOnlyList<ExecutableStep> steps, int depth,
        CancellationToken token)
    {
        foreach (var step in steps)
        {
            token.ThrowIfCancellationRequested();
            var signal = await RunStep(step, depth, token);

            if (signal is not Signal.Normal)
            {
                return signal;
            }
        }

        return Signal.Normal;
    }

    /// <summary>
    /// Runs a body while holding one of the two doors. The steps inside are the reason the block
    /// exists: another macro must not put its own keys or page work between them. A hold inside the
    /// same door only counts, so a block nested in another — or a browser step's own door — does not
    /// wait for a door it already holds.
    /// </summary>
    private async Task<Signal> RunHold(ExecutableStep step, int depth, CancellationToken token,
        bool input)
    {
        if (input)
        {
            if (_run.InputHolds > 0)
            {
                return await HeldBody(step, depth, token, input: true);
            }

            using var held = await SharedDoors.Input.Take(RunName, token);
            return await HeldBody(step, depth, token, input: true);
        }

        if (_run.BrowserHolds > 0)
        {
            return await HeldBody(step, depth, token, input: false);
        }

        using var page = await SharedDoors.Browser.Take(RunName, token);
        return await HeldBody(step, depth, token, input: false);
    }

    /// <summary>The body of a hold, with its door counted for the steps inside it.</summary>
    private async Task<Signal> HeldBody(ExecutableStep step, int depth, CancellationToken token,
        bool input)
    {
        if (input)
        {
            _run.InputHolds++;
            try
            {
                return await RunSteps(step.Children("steps"), depth + 1, token);
            }
            finally
            {
                _run.InputHolds--;
            }
        }

        _run.BrowserHolds++;
        try
        {
            return await RunSteps(step.Children("steps"), depth + 1, token);
        }
        finally
        {
            _run.BrowserHolds--;
        }
    }

    /// <summary>
    /// Runs another macro's steps as though they had been written here, on values of its own:
    /// what the caller passes is what it can read, and what it leaves behind comes back through
    /// the names the call asked for. The caller waits for it, and its own step timeout bounds the
    /// wait.
    /// </summary>
    private async Task<Signal> RunOtherMacro(ExecutableStep step, int depth, CancellationToken token)
    {
        var (name, steps, signature, wanted, called) = _calls.Resolve(step);

        Variables.PushLocal(called);
        _run.CallDepth++;
        PrepareOutcomes(steps);

        // The called macro starts outside every loop, whatever the caller is inside. A break in
        // there belongs to a loop of its own or to nothing; it must never reach out and cut a
        // loop in the caller short.
        var outerLoops = _run.Loops;
        _run.Loops = 0;

        // A catch block belongs to the macro it is written in. The called macro starts with none
        // of the caller's failures in reach, so a rethrow inside it can only mean one of its own.
        var outerCaught = _run.Caught;
        _run.Caught = [];
        var finished = false;
        try
        {
            // Seeded after the swap, not before: an initial value that reads a name has to read it
            // in the macro that declared it. Read before the swap, `total` would be the caller's
            // total and the called macro would start from a value it never declared.
            _calls.SeedLocals(signature);

            Log(LogLevel.Info, depth, step.Type, LogKeys.CalledMacro, name, steps.Count);
            var signal = await RunSteps(steps, depth + 1, token);

            // A break or a continue that got this far had no loop of its own to act on. That is
            // a mistake in the called macro, and the caller gets told rather than being handed
            // back what looks like a finished call. Only "start the next round" can arrive here
            // as a continue: a break or a continue written in the macro is caught where it is.
            if (signal is Signal.Break or Signal.Continue)
            {
                throw new StepFailure(LoopControlKey(signal), name);
            }

            // A return ends the macro it was written in; the caller carries on with the step
            // after the call. What the called macro had already put into its declared results is
            // taken back below — but only a call that finished. A run that failed or was stopped
            // hands back nothing: half of its results are not its answer.
            finished = signal is Signal.Normal or Signal.Return;
            return signal is Signal.Return ? Signal.Normal : signal;
        }
        finally
        {
            _run.Caught = outerCaught;
            _run.Loops = outerLoops;
            _run.CallDepth--;
            var produced = Variables.PopLocal();
            if (finished)
            {
                CallBinder.CheckResults(signature, wanted, produced);
                _calls.Take(wanted, produced);
            }
        }
    }
}
