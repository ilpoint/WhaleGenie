using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One step run on its own: the "run" button beside a field, and the same button the dialog a step
/// is written in carries. It answers whether the step really does what the person meant — a key
/// spelled another way, a control that is not the one they had in mind — by sending it once and
/// letting them watch, rather than by describing what it would do.
/// </summary>
/// <remarks>
/// The step goes through the same code a run puts it through, so what is tried is what would
/// happen: where the input is delivered, how long a control is held, which device answers, and what
/// the step's own clock makes of it. Nothing else of the macro runs, and whatever the step leaves
/// held is let go of a moment later, because the end of a run is not here to do it — a button tried
/// out and left down would stay down in the game long after the dialog was closed. The macros and
/// the variables a caller hands in are the ones the step is run with, so a step calling another
/// macro or reading a system variable behaves here the way it will in the run.
/// </remarks>
public static class StepTrial
{
    /// <summary>
    /// How long what the step did is left alone before it is taken back, so a click or a key is
    /// seen rather than undone in the same frame.
    /// </summary>
    private const int WatchMs = 300;

    /// <summary>Runs one step once and reports how it went.</summary>
    public static async Task<RunResult> TryAsync(ExecutableStep step, IDeviceLayer devices,
        CancellationToken token = default, IRunLooks? looks = null, IMacroLibrary? macros = null,
        VariableStore? variables = null)
    {
        var runner = new MacroRunner(variables ?? new VariableStore(), devices: devices, looks: looks,
            macros: macros);

        // The step's clock bounds the trial the way it bounds the step's attempts in a run: a wait
        // tried out here waits for as long as the step allows and no longer, and it ends saying what
        // it was waiting for. Without it, a step whose picture never turns up would hold the dialog
        // — and with it, every window of this program that the trial put out of the way — open until
        // the person closed it, which is not the end a run gives the same step.
        var budget = step.Meta.TimeoutMs;
        using var limit = budget > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(token)
            : null;
        limit?.CancelAfter(budget);
        var started = Stopwatch.GetTimestamp();
        try
        {
            await runner.ExecuteChecked(step, 0, limit?.Token ?? token);

            // The clock catches what the token cannot: a device call that blocks until it is
            // finished never looks at a cancellation, so it is still told off for going over.
            if (budget > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budget)
            {
                return new RunResult(RunStatus.Failed, RunKeys.Timeout,
                    budget.ToString(CultureInfo.InvariantCulture), 0);
            }

            // Long enough for what the step did to be seen before it is taken back.
            await Task.Delay(WatchMs, token);
            return new RunResult(RunStatus.Completed, string.Empty, string.Empty, 1);
        }
        catch (OperationCanceledException)
            when (limit is { IsCancellationRequested: true } && !token.IsCancellationRequested)
        {
            // The step's own clock ran out rather than the trial being stopped, so the dialog is
            // answered with the failure a run would have reported.
            var failure = runner.TimeoutOf(step);
            return new RunResult(RunStatus.Failed, failure.Key, failure.Detail, 0);
        }
        catch (StepFailure failure)
        {
            return new RunResult(RunStatus.Failed, failure.Key, failure.Detail, 0);
        }
        finally
        {
            runner.LetGoOfHeldInput();
        }
    }
}
