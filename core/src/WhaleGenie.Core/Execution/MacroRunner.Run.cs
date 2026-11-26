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

namespace WhaleGenie.Core.Execution;

// One run: the constructor, the knobs a caller may set, and the loop that walks the top level.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The token a clean-up block runs on during a stop: one budget for the whole stop, so nested
    /// blocks spend what is left of it rather than each asking for five seconds of their own.
    /// </summary>
    private CancellationToken StopCleanupToken()
    {
        _run.Cleanup ??= new CancellationTokenSource(CleanupBudget);
        return _run.Cleanup.Token;
    }

    /// <summary>How long a controller button stays down when the step asked for no time in particular.</summary>
    private const int ShortestPressMs = 50;

    // The shipped shape keeps its optional parameters, so the constructor that carries the clock is
    // the wider one without being optional-shaped itself. RS0027 wants the optional one to be the
    // widest, which cannot hold while both shapes exist; a call with fewer arguments binds to this
    // one, so there is nothing ambiguous about the pair.
#pragma warning disable RS0027
    public MacroRunner(VariableStore variables, IRunHost? host = null, IDeviceLayer? devices = null,
        double delayScale = 1, IMacroLibrary? macros = null, IRunLooks? looks = null)
        : this(variables, host, devices, delayScale, macros, looks, null)
    {
    }
#pragma warning restore RS0027

    /// <summary>
    /// The same run, against a clock and dice the caller hands in: every field of the run is worked
    /// out against them, so a step that reads "now" twice reads one moment and a check can pin the
    /// day it is about. Nothing else about the run changes, which is why this is the same
    /// constructor with one more answer rather than a second way of building one.
    /// </summary>
    public MacroRunner(VariableStore variables, IRunHost? host, IDeviceLayer? devices,
        double delayScale, IMacroLibrary? macros, IRunLooks? looks, EvaluationContext? evaluation)
    {
        Variables = variables;
        Evaluation = evaluation ?? EvaluationContext.System;
        _host = host ?? new SilentRunHost();
        _devices = devices ?? NullDeviceLayer.Instance;
        _macros = macros ?? EmptyMacroLibrary.Instance;
        _looks = looks;
        _pictures = new ReferencePictures(_devices, Variables, _run, Evaluation);
        _windows = new WindowFacts(_devices, Variables, _run, Evaluation);
        _sight = new ScreenSight(_devices, Variables, _run, _here, looks, _windows, Evaluation);
        _points = new ScreenPoints(_devices, Variables, _windows, Evaluation);
        _results = new StepResults(Variables, _here, _points, Log);
        _reads = new ScreenReads(_devices, Variables, _run, _here, _sight, Evaluation);
        _calls = new CallBinder(_devices, Variables, _macros, _run, Evaluation);
        _timeout = new StepTimeout(this, _windows);
        _values = new ValueActions(this, _run);
        DelayScale = Clamp(delayScale);
    }

    /// <summary>The variables the run reads and writes.</summary>
    public VariableStore Variables { get; }

    /// <summary>
    /// The clock, the dice and the notes every field of this run is worked out against. One run
    /// reading "now" twice reads one moment, and a check that hands in a fixed clock gets the same
    /// answers however long the run takes.
    /// </summary>
    public EvaluationContext Evaluation { get; }

    /// <summary>
    /// How much longer or shorter this run makes the timings the macro asked for. 1 leaves them
    /// exactly as written, 2 takes twice as long and 0.5 half as long. The macro's own numbers
    /// are never changed: this only bends them while the run is going on, so nothing has to be
    /// put back afterwards. Waiting timeouts are not scaled — giving up is not pacing.
    /// </summary>
    public double DelayScale { get; }

    /// <summary>The smallest and largest factor a run accepts, so a slip of the hand cannot stop it.</summary>
    private const double SmallestScale = 0.1;
    private const double LargestScale = 10;

    /// <summary>
    /// Where the logs of failed runs are written. Relative by default, which puts them beside the
    /// macro's own data; the application hands in an absolute folder so they land in the program's
    /// folder instead of inside a project somebody may well be sharing.
    /// </summary>
    public string FailureLogFolder { get; init; } = "logs";

    /// <summary>
    /// The name this run goes by on the run list. It is what <see cref="WhaleGenieInputGate"/>
    /// reports while this run is inside, so "which macro is sending input right now" has an answer.
    /// </summary>
    public string RunName { get; init; } = string.Empty;

    /// <summary>
    /// What the top-level macro of this run declares, when it was handed in. A call resolves its
    /// arguments in the caller's table; a run started from a trigger or the debug window has no
    /// caller, so the same declaration is applied to the run's own table instead.
    /// </summary>
    public MacroSignature? StartSignature { get; init; }

    /// <summary>The values supplied to a direct run, one per declared parameter that has one.</summary>
    public IReadOnlyList<MacroStartArgument>? StartArguments { get; init; }

    /// <summary>
    /// Events per second this run may send, or null to follow the program setting. It belongs to the
    /// run rather than to a macro: a call made by this run sends what the caller sends, and the same
    /// macro called from two runs keeps each of their limits.
    /// </summary>
    public int? InputRate { get; init; }

    /// <summary>
    /// How many calls deep this run already is. A background run carries the depth of the call that
    /// started it, so a chain that keeps starting itself in the background meets the same limit as
    /// one that calls itself in place.
    /// </summary>
    public int InitialCallDepth { get; init; }

    private static double Clamp(double scale)
        => double.IsFinite(scale) ? Math.Clamp(scale, SmallestScale, LargestScale) : 1;

    /// <summary>A length of time the macro asked for, at this run's speed.</summary>
    private int Pace(int milliseconds)
        => milliseconds <= 0 ? 0 : (int)Math.Round(milliseconds * DelayScale);

    /// <summary>Runs the steps and reports how the run ended.</summary>
    public async Task<RunResult> RunAsync(IReadOnlyList<ExecutableStep> steps,
        CancellationToken token = default)
    {
        _run.Begin(token, steps, InitialCallDepth);
        _here.Failure = null;

        // A declaration belongs to the macro that wrote it. A store the debug window keeps between
        // runs would otherwise still be holding the names of whatever ran last.
        Variables.Local.ResetDeclarations();
        Variables.Local.Capacity = ValueActions.LocalNameLimit;

        // A run that brought its own ceiling spaces its events along a line of its own; without one
        // it answers to the program setting exactly as before. Either way the answer is put back
        // when the run ends, so a stopped run leaves nothing behind for the next.
        using var limit = InputRate is { } rate ? InputLimit.Scope(rate) : null;

        // Which macro is running, for the steps that want to say so in a log line or a file name. It
        // is this run's own name, written into the macro's values so a called macro's own name — put
        // there by the call itself — takes over while that macro runs and this one is back afterwards.
        if (RunName.Trim().Length > 0)
        {
            Variables.Local.Set("sys.macroName", Value.FromText(RunName));
        }

        PrepareOutcomes(steps);
        Log(LogLevel.Info, 0, string.Empty, LogKeys.Start, steps.Count);
        if (Math.Abs(DelayScale - 1) > 0.001)
        {
            Log(LogLevel.Info, 0, string.Empty, LogKeys.Speed, DelayScale);
        }

        try
        {
            if (StartSignature is { } signature
                && _calls.SeedStart(signature, StartArguments) is { } startFailure)
            {
                _here.Failure = startFailure;
                Log(LogLevel.Error, 0, string.Empty, startFailure.Key, startFailure.Detail);
                LeaveFailureLog();
                return new RunResult(RunStatus.Failed, startFailure.Key, startFailure.Detail, 0);
            }

            var signal = await RunSteps(steps, 0, token);

            // A break or a continue that reaches the end of the macro had no loop to act on.
            // Reporting that as "finished" would be a lie the user cannot see through, so it
            // ends the run the same way a missing macro does.
            if (signal is Signal.Break or Signal.Continue)
            {
                _here.Failure = new StepFailure(LoopControlKey(signal));
                Log(LogLevel.Error, 0, string.Empty, _here.Failure.Key);
                signal = Signal.Failed;
            }

            var outcome = signal switch
            {
                Signal.Stop => new RunResult(RunStatus.Stopped, string.Empty, string.Empty, _run.Executed),
                Signal.Failed => new RunResult(RunStatus.Failed, _here.Failure?.Key ?? RunKeys.Failed,
                    _here.Failure?.Detail ?? string.Empty, _run.Executed),
                _ => new RunResult(RunStatus.Completed, string.Empty, string.Empty, _run.Executed),
            };

            Log(LogLevel.Info, 0, string.Empty, SummaryKey(outcome.Status), _run.Executed);

            if (outcome.Status is RunStatus.Failed)
            {
                LeaveFailureLog();
            }

            return outcome;
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warn, 0, string.Empty, LogKeys.Stopped, _run.Executed);
            return new RunResult(RunStatus.Stopped, string.Empty, string.Empty, _run.Executed);
        }
        finally
        {
            // Whatever the run decided, the machine is handed back the way it was found.
            LetGoOfHeldInput();

            _run.Cleanup?.Dispose();
            _run.Cleanup = null;
        }
    }

    /// <summary>The line written once a run is over.</summary>
    private static string SummaryKey(RunStatus status) => status switch
    {
        RunStatus.Stopped => LogKeys.Stopped,
        RunStatus.Failed => LogKeys.Aborted,
        _ => LogKeys.Finished,
    };

    /// <summary>The message a break or a continue gets when it turns out to have no loop.</summary>
    private static string LoopControlKey(Signal signal)
        => signal is Signal.Break ? RunKeys.BreakOutsideLoop : RunKeys.ContinueOutsideLoop;

    /// <summary>
    /// Leaves the whole log of this run behind as a text file when it stops on a failure.
    /// </summary>
    private void LeaveFailureLog()
        => new FailureLog(this, _host, _run.Log, FailureLogFolder, RunName).Write();
}
