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

// Running one step: the attempts, the wait before a retry, the failure a timeout leaves, and the report.
public sealed partial class MacroRunner
{
    private async Task<Signal> RunStep(ExecutableStep step, int depth, CancellationToken token)
    {
        if (!step.Meta.IsEnabled)
        {
            Log(LogLevel.Debug, depth, step.Type, LogKeys.Skipped);
            Report(step, OutcomeSkipped, 0, null, [], []);
            return Signal.Normal;
        }

        // The host may leave this one step out, which is what "skip this step once" is: the step
        // did nothing, so it is answered the way a switched-off step is and the run goes on.
        if (!await _host.BeforeStep(step, depth, token))
        {
            Log(LogLevel.Debug, depth, step.Type, LogKeys.Skipped);
            Report(step, OutcomeSkipped, 0, null, [], []);
            return Signal.Normal;
        }

        await Pause(Pace(step.Meta.DelayBeforeMs), token);

        // What the step works out while it runs is kept for whoever is watching it, and nothing is
        // kept when nobody is: a macro a trigger started is not made to turn every result into text
        // so that a window which is not there can ignore it.
        var outerWorked = _here.Worked;
        _here.Worked = _looks is null ? null : [];
        var startedStep = Stopwatch.GetTimestamp();
        Expression.BeginDiagnostics();
        try
        {
            return await RunAttempts(step, depth, token, startedStep);
        }
        finally
        {
            _here.Worked = outerWorked;
            foreach (var note in Expression.TakeDiagnostics())
            {
                Log(LogLevel.Warn, depth, step.Type, note.Key, [.. note.Arguments]);
            }
        }
    }

    /// <summary>
    /// One step's attempts at its work, from the first try to whichever ending it comes to. The
    /// clock covers all of them, because "how long did this step take" is answered by the step
    /// rather than by the last try of it.
    /// </summary>
    /// <summary>
    /// What makes one step the same step as itself later on: the identity written into the file
    /// where it has one, and the instance where it has none, which is what makes a step inside a
    /// loop the same step on the next pass.
    /// </summary>
    private static object Key(ExecutableStep step) => step.Id.Length > 0 ? step.Id : step;

    /// <summary>
    /// Whether this step may go ahead: true for everything that can be undone, and true for one that
    /// cannot once the person has said so. Asked once per run per step, so a loop that repeats it
    /// asks once and a step reached again after a refusal asks again.
    /// </summary>
    private async Task<bool> Agreed(ExecutableStep step, CancellationToken token)
    {
        if (_run.Agreed.Contains(Key(step)) || !ActionSemanticsCatalog.CannotBeUndone(step))
        {
            return true;
        }

        if (!await _host.ConfirmIrreversible(step, token))
        {
            return false;
        }

        _run.Agreed.Add(Key(step));
        return true;
    }

    /// <summary>
    /// Empties the variables this step was told to hand over, before it is tried. A name with no
    /// value is what a reader sees for a result the step did not work out this time.
    /// </summary>
    private void ClearResults(ExecutableStep step)
    {
        foreach (var row in step.Rows("returns"))
        {
            var name = Word(row, FieldNames.Name);
            if (name.Length > 0)
            {
                Variables.Set(name, Value.Null);
            }
        }
    }

    private async Task<Signal> RunAttempts(ExecutableStep step, int depth, CancellationToken token,
        long startedStep)
    {
        // The budget belongs to the step rather than to one try of it: a retry spends what is left
        // of the same clock instead of asking for another full one. The token is what ends a wait
        // that is still going; a device call that has already started cannot be interrupted and is
        // judged when it returns.
        var budget = step.Meta.TimeoutMs;
        var startedBudget = Stopwatch.GetTimestamp();
        using var limit = budget > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(token)
            : null;
        limit?.CancelAfter(budget);
        var attemptToken = limit?.Token ?? token;
        var attempts = new List<StepAttempt>();

        double Used() => Stopwatch.GetElapsedTime(startedBudget).TotalMilliseconds;

        double? Remaining() => budget > 0 ? Math.Max(0, budget - Used()) : null;

        bool OutOfTime() => budget > 0 && Used() >= budget;

        StepAttempt Took(int number, long started, string outcome, StepFailure? failure, bool overran)
            => new()
            {
                Number = number,
                ElapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Outcome = outcome,
                FailureKey = failure?.Key ?? string.Empty,
                FailureDetail = failure?.Detail ?? string.Empty,
                RemainingMs = Remaining(),
                Overran = overran,
            };

        async Task<Signal> Spent(ExecutableStep stepToFail, CancellationToken stopToken)
        {
            _here.Failure = WaitedTooLong(stepToFail);
            Log(LogLevel.Error, depth, step.Type, _here.Failure.Key, _here.Failure.Detail);

            // The time is gone, so another try cannot be paid for. Everything else the step's
            // failure rule says — carry on, next round, stop, ask — still applies.
            var decided = await Decide(stepToFail, stopToken);
            return StepFailed(stepToFail, decided is Decision.Retry ? Decision.Stop : decided,
                Elapsed(startedStep), attempts);
        }

        // A step fails two ways: a block whose child failed comes back as a signal, a step that
        // failed itself throws. What happens next is the same either way — retry while the count
        // allows, then let the step's own rule decide — so it is written once. Two copies meant a
        // change to the retry rule had to be made twice, and half a change reads as "a failure that
        // is thrown retries and a failure that is signalled does not".
        async Task<Signal?> RetryOrStop(ExecutableStep failed, int attempt,
            CancellationToken stopToken)
        {
            var decided = attempt < failed.Meta.RetryCount
                ? Decision.Retry
                : await Decide(failed, stopToken);
            if (decided is not Decision.Retry)
            {
                return StepFailed(failed, decided, Elapsed(startedStep), attempts);
            }

            if (!await WaitBeforeRetry(failed, attempt, Remaining(), attemptToken, stopToken))
            {
                return await Spent(failed, stopToken);
            }

            Log(LogLevel.Warn, depth, failed.Type, LogKeys.Retry, attempt + 1);
            return null;
        }

        for (var attempt = 0; ; attempt++)
        {
            // A result is this attempt's answer: one this attempt does not write reads as
            // empty rather than as what the previous attempt or the previous round left.
            ClearResults(step);
            var started = Stopwatch.GetTimestamp();
            try
            {
                // A step that cannot be undone is put to the person before it is reached, once per
                // run. A refusal is this step's own failure, so the rule on the step decides what
                // happens next, and a retry asks again because nobody has agreed to anything yet.
                // The question is asked on the run's token rather than the step's: hesitating is
                // not the step taking too long.
                if (!await Agreed(step, token))
                {
                    throw new StepFailure(RunKeys.NotConfirmed);
                }

                var signal = await ExecuteChecked(step, depth, attemptToken);

                // The clock catches what the token cannot: a device call that blocks until it is
                // finished never looks at a cancellation, so it is still told off for going over.
                if (OutOfTime() && !token.IsCancellationRequested)
                {
                    throw new StepFailure(RunKeys.Timeout, budget.ToString(CultureInfo.InvariantCulture));
                }

                _run.Executed++;

                // A step that holds steps fails when one of them fails. That arrives as a signal
                // rather than as an exception — the step that failed has already said what it
                // wants done about it — so the settings on this step are what decide what a
                // failure it did not handle means: try the whole block again, leave the rest of
                // it out, go on to the next round of the loop it sits in, or stop. A step that
                // handled its own failure never gets this far, which is what makes the two
                // levels work together: the inside decides first, the block decides what is left.
                if (signal is Signal.Failed)
                {
                    var failure = _here.Failure ?? new StepFailure(RunKeys.Failed);
                    _here.Failure = failure;
                    attempts.Add(Took(attempt + 1, started, OutcomeFailed, failure, overran: false));

                    var stopped = await RetryOrStop(step, attempt, token);
                    if (stopped is { } stop)
                    {
                        return stop;
                    }

                    continue;
                }

                attempts.Add(Took(attempt + 1, started, OutcomeOk, null, overran: false));
                await Pause(Pace(step.Meta.DelayAfterMs), token);
                WriteOutcome(step, OutcomeOk);
                Report(step, OutcomeOk, Elapsed(startedStep), null, Worked(), attempts);
                return signal;
            }
            catch (OperationCanceledException)
                when (limit is { IsCancellationRequested: true } && !token.IsCancellationRequested)
            {
                var failure = WaitedTooLong(step);
                attempts.Add(Took(attempt + 1, started, OutcomeFailed, failure, overran: true));
                _here.Failure = failure;
                Log(LogLevel.Error, depth, step.Type, failure.Key, failure.Detail);
                var decided = await Decide(step, token);
                return StepFailed(step, decided is Decision.Retry ? Decision.Stop : decided,
                    Elapsed(startedStep), attempts);
            }
            catch (StepFailure caught)
            {
                var overran = OutOfTime() && !token.IsCancellationRequested;
                var failure = overran && !SaysTimedOut(caught.Key)
                    ? new StepFailure(RunKeys.Timeout, budget.ToString(CultureInfo.InvariantCulture))
                    : caught;
                attempts.Add(Took(attempt + 1, started, OutcomeFailed, failure, overran));
                _here.Failure = failure;
                Log(LogLevel.Error, depth, step.Type, failure.Key, failure.Detail);

                // What a thrown failure does next is the same as what a signalled one does; the
                // answer comes back from one place.
                var stopped = await RetryOrStop(step, attempt, token);
                if (stopped is { } stop)
                {
                    return stop;
                }

                continue;
            }
        }
    }

    /// <summary>
    /// Waits out the pause before another try without starting one the remaining budget cannot pay
    /// for. False means the budget has no room left for a try, so the step has run out of time.
    /// </summary>
    private async Task<bool> WaitBeforeRetry(ExecutableStep step, int attempt,
        double? remaining, CancellationToken token, CancellationToken outer)
    {
        if (remaining is null)
        {
            await Pause(RetryPause(step, attempt + 1), token);
            return true;
        }

        if (remaining <= 0)
        {
            return false;
        }

        var pause = RetryPause(step, attempt + 1);
        if (pause >= remaining)
        {
            return false;
        }

        try
        {
            await Pause(pause, token);
            return true;
        }
        catch (OperationCanceledException) when (!outer.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// True for the failure keys a wait or a device raises when its own clock runs out. Those say
    /// what was being waited for, so running a little past the step's clock must not replace them
    /// with the bare "too slow": the caller needs the picture, element or program that never came.
    /// </summary>
    private static bool SaysTimedOut(string key)
        => key is RunKeys.Timeout
            or RunKeys.WaitColorTimeout or RunKeys.WaitTimeout
            or RunKeys.ImageNotFound or RunKeys.DetectNotFound or RunKeys.ScreenNotStable
            or RunKeys.TextNotFound or RunKeys.ElementNotFound or RunKeys.ClipboardTimeout
            or RunKeys.ColourNotFound
            or RunKeys.ProcessTimeout or RunKeys.ProgramNotFound or RunKeys.WindowTimeout
            or RunKeys.CommandTimeout or RunKeys.BrowserNotFound or RunKeys.BrowserNoNewTab;

    /// <summary>
    /// The failure a step reports when its own clock runs out. A step that was waiting for
    /// something knows what it was waiting for, so it says that rather than only that it was slow:
    /// "the picture never turned up" points at the picture, where "too slow" points at the number.
    /// Every other step has nothing more to say than that it ran out of time.
    /// </summary>
    private StepFailure WaitedTooLong(ExecutableStep step)
    {
        var patience = step.Meta.TimeoutMs.ToString(CultureInfo.InvariantCulture);
        try
        {
            return step.Type switch
            {
                "vision.waitColor" => new StepFailure(RunKeys.WaitColorTimeout,
                    PixelColor.Parse(Args(step).ValueOf("color").AsText()).ToHex()),
                "vision.clickColor" => new StepFailure(RunKeys.ColourNotFound,
                    PixelColor.Parse(Args(step).ValueOf("color").AsText()).ToHex()),
                "vision.waitImage" or "vision.clickImage" => new StepFailure(RunKeys.ImageNotFound,
                    Written(step.Rows("images"))),
                "vision.waitDetect" or "vision.clickDetect" => new StepFailure(RunKeys.DetectNotFound,
                    Looking(step)),
                "vision.waitStable" => new StepFailure(RunKeys.ScreenNotStable, patience),
                "ocr.clickText" => new StepFailure(RunKeys.TextNotFound,
                    Args(step).ValueOf("queryText").AsText()),
                "control.waitUntil" => new StepFailure(RunKeys.WaitTimeout, WaitingFor(step, patience)),
                "clipboard.waitChange" or "clipboard.copy" =>
                    new StepFailure(RunKeys.ClipboardTimeout),
                "process.waitFor" => new StepFailure(RunKeys.ProgramNotFound, WindowText(step)),
                "process.waitExit" => new StepFailure(RunKeys.ProcessTimeout, WindowText(step)),
                "window.waitFor" => new StepFailure(RunKeys.WindowTimeout, WindowText(step)),
                "uia.waitElement" or "uia.click" or "uia.invoke" =>
                    new StepFailure(RunKeys.ElementNotFound, Args(step).Text("uiSelector")),
                "command.run" => new StepFailure(RunKeys.CommandTimeout,
                    Args(step).ValueOf("commandFile").AsText()),
                _ => new StepFailure(RunKeys.Timeout, patience),
            };
        }
        catch (ExpressionException)
        {
            // A field that cannot be read at all is the business of another failure; the only
            // thing this one has to say is that the step ran out of time.
            return new StepFailure(RunKeys.Timeout, patience);
        }
    }

    /// <summary>
    /// What a wait-until step was waiting for, so its failure names the question rather than only
    /// that it was slow. A step with no condition left in it — which the editor does not write —
    /// has nothing to name, and is left with the length of time.
    /// </summary>
    private static string WaitingFor(ExecutableStep step, string patience)
        => step.Text("condition").Trim() is { Length: > 0 } asked ? asked : patience;

    /// <summary>
    /// The step is over and it did not work: writes that down and hands back what its failure rule
    /// decided the run should do next. Both the failure that arrived as a signal from the steps
    /// inside this one and the one that arrived as an exception come through here, so a condition
    /// written after a step always has an answer by the time it is asked.
    /// </summary>
    private Signal StepFailed(ExecutableStep step, Decision decided, double elapsedMs,
        IReadOnlyList<StepAttempt> attempts)
    {
        WriteOutcome(step, OutcomeFailed);
        Report(step, OutcomeFailed, elapsedMs, _here.Failure, Worked(), attempts);
        return decided switch
        {
            Decision.Skip => Signal.Normal,
            Decision.NextIteration => Signal.Continue,
            _ => Signal.Failed,
        };
    }

    /// <summary>
    /// Hands over what one step came to. The values travel whether or not the macro keeps them:
    /// ticking a result decides whether it becomes a variable of its own, and never whether it can
    /// be read afterwards — which is the whole point of the panel a debugger puts this in.
    /// </summary>
    private void Report(ExecutableStep step, string outcome, double elapsedMs, StepFailure? failure,
        IReadOnlyList<StepResult> results, IReadOnlyList<StepAttempt> attempts)
    {
        if (_looks is not { } looks)
        {
            return;
        }

        looks.Ran(new StepRun
        {
            StepId = step.Id,
            StepType = step.Type,
            Outcome = outcome,
            ElapsedMs = elapsedMs,
            FailureKey = failure?.Key ?? string.Empty,
            FailureDetail = failure?.Detail ?? string.Empty,
            Results = results,
            Attempts = attempts,
            TimeoutMs = step.Meta.TimeoutMs,
        });
    }

    /// <summary>Everything the step being run has worked out, as it stands.</summary>
    private IReadOnlyList<StepResult> Worked()
        => _here.Worked is { Count: > 0 } worked ? [.. worked] : [];

    /// <summary>
    /// What the step's failure rule says to do about the failure that just happened. The failure
    /// the rule is asked about is the last one the run saw: a step that failed on its own set it,
    /// and one that failed because of the steps inside it was given it by the step that failed.
    /// </summary>
    private async Task<Decision> Decide(ExecutableStep step, CancellationToken token)
        => step.Meta.OnError switch
        {
            StepErrorAction.Continue => Decision.Skip,
            StepErrorAction.NextIteration => Decision.NextIteration,
            StepErrorAction.AskUser => await Ask(step, _here.Failure ?? new StepFailure(RunKeys.Failed), token),
            _ => Decision.Stop,
        };

    private async Task<Decision> Ask(ExecutableStep step, StepFailure failure, CancellationToken token)
    {
        var choice = await _host.Ask(step.Type, failure.Key, failure.Detail, step.Meta.Comment, token);
        return choice switch
        {
            StepErrorChoice.Retry => Decision.Retry,
            StepErrorChoice.Skip => Decision.Skip,
            _ => Decision.Stop,
        };
    }

    /// <summary>
    /// How long to wait before a retry, at this run's speed. The step's settings decide the
    /// pause, so a step can back off further with every attempt instead of hammering away.
    /// </summary>
    private int RetryPause(ExecutableStep step, int attempt)
        => Pace(step.Meta.RetryDelayFor(attempt));

    /// <summary>
    /// Runs one step, turning a broken expression into a failure the step can report and
    /// the failure rules can act on, instead of letting it bring the whole run down.
    /// </summary>
    private async Task<Signal> ExecuteChecked(ExecutableStep step, int depth, CancellationToken token)
    {
        try
        {
            return await Execute(step, depth, token);
        }
        catch (ExpressionException failure)
        {
            throw new StepFailure(RunKeys.BadExpression, failure.Detail);
        }
        catch (VariableTypeException failure)
        {
            throw new StepFailure(RunKeys.VariableTypeMismatch, failure.Name);
        }
        catch (VariableTableFullException failure)
        {
            throw new StepFailure(RunKeys.VariableTableFull,
                failure.Capacity.ToString(CultureInfo.InvariantCulture));
        }
        catch (RegexTimeoutException failure)
        {
            throw new StepFailure(RunKeys.RegexTimeout, failure.Pattern);
        }
        catch (DeviceUnavailableException failure)
        {
            throw new StepFailure(RunKeys.NoDevice, failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            throw new StepFailure(failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not StepFailure)
        {
            // Whatever a device driver throws, the run reports it as a failed step rather
            // than letting it escape and take the whole run down.
            throw new StepFailure(RunKeys.DeviceFailed, failure.Message);
        }
    }

    private async Task<Signal> Execute(ExecutableStep step, int depth, CancellationToken token)
    {
        ValidateAnswers(step);
        // Whatever the step before this one read is not what this one read. A step that looks at a
        // picture it was handed rather than taking one of its own says nothing about a window,
        // which is truer than repeating what the step before it was looking at.
        _here.TakenWindow = string.Empty;
        _here.TakenMethod = null;

        // The shared browser is its own piece of state. Every browser step takes this door, whether
        // or not the macro asked to take turns sending input: another run must not switch the page
        // between this step's click and its next read.
        if (UsesBrowser(step.Type))
        {
            if (_run.BrowserHolds > 0)
            {
                _run.BrowserHolds++;
                try
                {
                    return await Dispatch(step, depth, token);
                }
                finally
                {
                    _run.BrowserHolds--;
                }
            }

            using var page = await SharedDoors.Browser.Take(RunName, token);
            _run.BrowserHolds++;
            try
            {
                return await Dispatch(step, depth, token);
            }
            finally
            {
                _run.BrowserHolds--;
            }
        }

        // Sending input is not what makes a run wait: the one input door is taken by a
        // control.holdInput block, and by nothing else. Two macros running at once are expected to
        // send side by side; a stretch that must not be interrupted says so where it is written.
        return await Dispatch(step, depth, token);
    }
}
