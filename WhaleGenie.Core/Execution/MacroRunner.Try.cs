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

// Try, catch and the remembered failure the catch block reads.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Attempts a block, handles a failure in another block, and runs a third one either way.
    /// Catching the failure here is the whole point of the step, so it does not reach the
    /// failure rules and does not stop the macro.
    /// </summary>
    /// <remarks>
    /// A run stopped from outside still tidies up: the clean-up block runs even though the run is
    /// being cancelled, on a budget shared by every clean-up block of that stop. A clean-up that
    /// fails or runs out of that budget is logged and does not replace the stop — the run still
    /// ends as stopped, with the failure key it would have had.
    /// </remarks>
    private async Task<Signal> RunTry(ExecutableStep step, int depth, CancellationToken token)
    {
        var closing = step.Children("finally");
        Signal? closingSignal = null;
        var signal = Signal.Normal;
        try
        {
            signal = await RunSteps(step.Children("body"), depth + 1, token);

            if (signal is Signal.Failed)
            {
                var failure = _failure ?? new StepFailure(RunKeys.Failed);
                var before = RememberCaught(failure);
                try
                {
                    // The reason is asked under the names the catch block itself reads, so a macro
                    // that takes only one kind of failure says exactly which one and can compare the
                    // same key the step below will see.
                    if (string.IsNullOrWhiteSpace(step.Text("catchWhen"))
                        || Check(step, "catchWhen", token))
                    {
                        Remember(step, failure);
                        Log(LogLevel.Warn, depth, step.Type, LogKeys.Caught);
                        signal = await RunSteps(step.Children("catch"), depth + 1, token);
                    }
                    else
                    {
                        // Not this block's failure. The key and detail travel on untouched; the
                        // clean-up steps below still run before the caller hears about it.
                        _failure = failure;
                    }
                }
                finally
                {
                    RestoreCaught(before);
                }
            }
            else
            {
                Remember(step, null);
            }
        }
        finally
        {
            if (closing.Count > 0)
            {
                var stopped = token.IsCancellationRequested;
                var cleanup = stopped ? StopCleanupToken() : token;
                Log(LogLevel.Debug, depth, step.Type, LogKeys.Finally);
                try
                {
                    closingSignal = await RunSteps(closing, depth + 1, cleanup);

                    // A clean-up step that fails during a stop is not a second failure to report as
                    // the ending: the run is stopping either way, so it is only said in the log.
                    if (stopped && closingSignal is Signal.Failed)
                    {
                        Log(LogLevel.Warn, depth, step.Type, LogKeys.CleanupFailed,
                            _failure?.Key ?? RunKeys.Failed);
                    }
                }
                catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
                {
                    // The budget for this stop is spent. Whatever was left half done is left, and
                    // the stop itself is what the run reports.
                    Log(LogLevel.Warn, depth, step.Type, LogKeys.CleanupFailed, RunKeys.Timeout);
                }
                catch (StepFailure failure) when (stopped)
                {
                    Log(LogLevel.Warn, depth, step.Type, LogKeys.CleanupFailed, failure.Key);
                }
            }
        }

        // Whatever the clean-up decides — stop, fail, return, break or start the next round — is the
        // last word when the block ended on its own: it ran after the attempt, so its ending is the
        // one that stands.
        if (closingSignal is { } done && done is not Signal.Normal)
        {
            return done;
        }

        return signal;
    }

    /// <summary>
    /// The failure a rethrow hands on: the innermost catch block now running. A rethrow outside
    /// every catch has nothing to hand on, and says so rather than borrowing a failure from
    /// somewhere else.
    /// </summary>
    private StepFailure CaughtFailure()
        => _caught.Count > 0
            ? _caught[^1]
            : throw new StepFailure(RunKeys.NothingToRethrow);

    /// <summary>
    /// Writes the caught failure where the catch block can read it, and gives back what those two
    /// names held before so leaving the block puts the macro back the way it was found.
    /// </summary>
    private (Value? Key, Value? Detail) RememberCaught(StepFailure failure)
    {
        var before = (
            Key: Variables.Local.TryGet(CaughtKeyName, out var key) ? key : null,
            Detail: Variables.Local.TryGet(CaughtDetailName, out var detail) ? detail : null);

        Variables.Local.Set(CaughtKeyName, Value.FromText(failure.Key));
        Variables.Local.Set(CaughtDetailName, Value.FromText(failure.Detail));
        _caught.Add(failure);
        return before;
    }

    /// <summary>
    /// Takes the catch block's own names away again, restoring the values of an outer catch when
    /// there is one. A name the macro did not have before is removed rather than left empty, so it
    /// cannot be mistaken for a failure after the block is over.
    /// </summary>
    private void RestoreCaught((Value? Key, Value? Detail) before)
    {
        if (_caught.Count > 0)
        {
            _caught.RemoveAt(_caught.Count - 1);
        }

        if (before.Key is { } key)
        {
            Variables.Local.Set(CaughtKeyName, key);
        }
        else
        {
            Variables.Local.Remove(CaughtKeyName);
        }

        if (before.Detail is { } detail)
        {
            Variables.Local.Set(CaughtDetailName, detail);
        }
        else
        {
            Variables.Local.Remove(CaughtDetailName);
        }
    }

    /// <summary>
    /// Records the failure key and its detail separately, or empties both when the attempt did
    /// not fail. A macro branches on the key; the detail is what a person reads next to it.
    /// </summary>
    private void Remember(ExecutableStep step, StepFailure? failure)
    {
        Give(step, "error", Value.FromText(failure?.Key ?? string.Empty));
        Give(step, "detail", Value.FromText(failure?.Detail ?? string.Empty));
    }
}
