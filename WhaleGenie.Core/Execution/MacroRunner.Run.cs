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

// One run: the constructor, the knobs a caller may set, and the loop that walks the top level.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The token a clean-up block runs on during a stop: one budget for the whole stop, so nested
    /// blocks spend what is left of it rather than each asking for five seconds of their own.
    /// </summary>
    private CancellationToken StopCleanupToken()
    {
        _cleanup ??= new CancellationTokenSource(CleanupBudget);
        return _cleanup.Token;
    }

    /// <summary>
    /// The background runs this run has started and not yet waited for. They keep the shared global
    /// table, but a local table of their own.
    /// </summary>


    /// <summary>
    /// How many browser steps of this run are inside the browser door right now. A block such as
    /// <c>browser.dialog</c> holds the door while its body runs; the body's own browser steps are
    /// the same turn at the page and must not wait for the door they already hold.
    /// </summary>
    private int _browserHolds;

    /// <summary>
    /// How many input-holding blocks are around the step being run. The block asked for the door so
    /// its steps would send their keys as one turn, so the steps inside must not ask for it again.
    /// </summary>
    private int _inputHolds;

    /// <summary>
    /// How many loops the run is inside right now. A break or a continue only means something
    /// while this is above zero; one that reaches the top of a macro has nothing to act on.
    /// </summary>
    private int _loops;

    /// <summary>
    /// How many switches the run is inside right now. A break inside a switch leaves that switch
    /// rather than the loop around it, so a switch on its own is a place a break can stand.
    /// </summary>
    private int _switches;

    /// <summary>
    /// Names a loop writes for its own rounds. They belong to the innermost loop that names them:
    /// leaving it puts back what the loop around it had, so an inner "for each" using "item" does
    /// not erase the outer one's item. A name no loop around it was using is the macro's own to
    /// read afterwards, and a counter stays readable while the round number does not.
    /// </summary>
    private LoopScope EnterLoop(params string[] names) => EnterLoop(false, names);

    private LoopScope EnterLoop(bool keepLast, params string[] names)
    {
        var taken = names.Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var saved = new List<(string Name, bool Had, Value? Value)>();
        foreach (var name in taken)
        {
            if (_activeLoopNames.ContainsKey(name))
            {
                var had = Variables.Local.TryGet(name, out var value);
                saved.Add((name, had, had ? value : null));
            }

            _activeLoopNames[name] = _activeLoopNames.GetValueOrDefault(name) + 1;
        }

        return new LoopScope(Variables.Local, this, taken, saved, keepLast);
    }

    /// <summary>
    /// Which loop names are in use right now, and how many loops deep. A name taken by an inner
    /// loop is put back when that loop ends; one the inner loop was the first to use is left alone.
    /// </summary>
    private readonly Dictionary<string, int> _activeLoopNames = new(StringComparer.OrdinalIgnoreCase);

    private readonly struct LoopScope : IDisposable
    {
        private readonly VariableBag _values;
        private readonly MacroRunner _runner;
        private readonly List<string> _names;
        private readonly List<(string Name, bool Had, Value? Value)> _saved;
        private readonly bool _keepLast;

        public LoopScope(VariableBag values, MacroRunner runner, List<string> names,
            List<(string Name, bool Had, Value? Value)> saved, bool keepLast)
        {
            _values = values;
            _runner = runner;
            _names = names;
            _saved = saved;
            _keepLast = keepLast;
        }

        public void Dispose()
        {
            foreach (var (name, had, value) in _saved)
            {
                if (had)
                {
                    _values.Set(name, value!);
                }
                else
                {
                    _values.Remove(name);
                }
            }

            foreach (var name in _names)
            {
                var left = _runner._activeLoopNames[name] - 1;
                if (left > 0)
                {
                    _runner._activeLoopNames[name] = left;
                }
                else
                {
                    _runner._activeLoopNames.Remove(name);
                    if (!_keepLast && !_saved.Exists(saved => saved.Name == name))
                    {
                        _values.Remove(name);
                    }
                }
            }
        }
    }

    private StepFailure? _failure;

    /// <summary>
    /// The keys and mouse buttons the macro has pressed and not yet let go, with the device each
    /// was pressed on so it is let go the same way — a key posted at one window has to be released
    /// at that window, not at whatever has the focus when the run ends.
    /// </summary>
    private readonly List<(IInputDevice Device, string Name)> _heldKeys = [];

    private readonly List<(IInputDevice Device, string Button, ScreenPoint Point)> _heldButtons = [];

    /// <summary>Whether a controller was driven, so the run lets go of it the way it does a key.</summary>
    private bool _gamepadUsed;

    /// <summary>How long a controller button stays down when the step asked for no time in particular.</summary>
    private const int ShortestPressMs = 50;

    /// <summary>
    /// The steps this run was handed, kept so that a script can name one of them. Nothing else reads
    /// it: what a step is found by is the identity the editor writes into the file, and the steps of
    /// a macro called from this one belong to that call rather than to this list.
    /// </summary>
    private IReadOnlyList<ExecutableStep> _steps = [];

    /// <summary>
    /// The lines this run has written so far, kept so that one which stops on a failure can leave
    /// the whole of them behind as a text file. A run the debugger is watching shows the same lines
    /// in its window, and one a trigger started has no window at all — there this file is the only
    /// account of what happened.
    /// </summary>
    private readonly List<LogEntry> _log = [];

    /// <summary>
    /// Whether a step a script ran asked for the whole macro to stop. A script runs to its own end —
    /// it is a program in a process of its own and there is no stopping it politely — so the answer
    /// is carried out of the step that ran it and ends the macro once that step is over.
    /// </summary>
    private bool _stoppedInsideScript;

    /// <summary>
    /// Whether a step a script ran asked to end its macro. Like a stop, the answer comes back
    /// through the script step and ends the macro once that step is over.
    /// </summary>
    private bool _returnedInsideScript;

    /// <summary>
    /// The failures the catch blocks now running have taken hold of, innermost last. A rethrow
    /// takes the last one, so a nested catch never reaches past the block it is written in. The
    /// list is swapped for a new one around a call to another macro: a macro may not rethrow
    /// something its caller caught.
    /// </summary>
    private List<StepFailure> _caught = [];

    /// <summary>
    /// How long what one tried step left behind is left alone before it is let go of, so a game
    /// that reads the controller on its own schedule has the chance to notice it.
    /// </summary>
    private const int TrialWatchMs = 300;

    public MacroRunner(VariableStore variables, IRunHost? host = null, IDeviceLayer? devices = null,
        double delayScale = 1, IMacroLibrary? macros = null, IRunLooks? looks = null)
    {
        Variables = variables;
        _host = host ?? new SilentRunHost();
        _devices = devices ?? NullDeviceLayer.Instance;
        _macros = macros ?? EmptyMacroLibrary.Instance;
        _looks = looks;
        DelayScale = Clamp(delayScale);
    }

    /// <summary>The variables the run reads and writes.</summary>
    public VariableStore Variables { get; }

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
    /// The name this run goes by on the run list. It is what <see cref="InputGate"/> reports while
    /// this run is inside, so "which macro is sending input right now" has an answer.
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

    /// <summary>
    /// How long a move borrows when it was written as an instant jump but asked to travel along a
    /// bent or hand-like path: there is no path to travel without a duration, and quietly going
    /// straight instead would make the style look broken.
    /// </summary>
    private const int BorrowedMoveMs = 200;

    private static double Clamp(double scale)
        => double.IsFinite(scale) ? Math.Clamp(scale, SmallestScale, LargestScale) : 1;

    /// <summary>A length of time the macro asked for, at this run's speed.</summary>
    private int Pace(int milliseconds)
        => milliseconds <= 0 ? 0 : (int)Math.Round(milliseconds * DelayScale);

    /// <summary>
    /// Does one step for real, on its own, and reports how it went. This is the "test" button
    /// beside a field in the action dialog, and the "run" button the dialog itself carries: the step
    /// is written, and the question is whether it really does what the user meant — a key spelled
    /// another way, a control that is not the one they had in mind — which is answered by sending it
    /// once and letting them watch.
    /// </summary>
    /// <remarks>
    /// The step goes through the same code a run puts it through, so what is tried is what would
    /// happen: where the input is delivered, how long a control is held, which device answers.
    /// Nothing else of the macro runs, and whatever the step leaves held is let go of a moment
    /// later, because the end of a run is not here to do it — a button tried out and left down
    /// would stay down in the game long after the dialog was closed. The macros and the variables a
    /// caller hands in are the ones the step is run with, so a step calling another macro or reading
    /// a system variable behaves here the way it will in the run.
    /// </remarks>
    public static async Task<RunResult> TryAsync(ExecutableStep step, IDeviceLayer devices,
        CancellationToken token = default, IRunLooks? looks = null, IMacroLibrary? macros = null,
        VariableStore? variables = null)
    {
        var runner = new MacroRunner(variables ?? new VariableStore(), devices: devices, looks: looks,
            macros: macros);
        try
        {
            await runner.ExecuteChecked(step, 0, token);

            // Long enough for what the step did to be seen before it is taken back.
            await Task.Delay(TrialWatchMs, token);
            return new RunResult(RunStatus.Completed, string.Empty, string.Empty, 1);
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

    /// <summary>Runs the steps and reports how the run ended.</summary>
    public async Task<RunResult> RunAsync(IReadOnlyList<ExecutableStep> steps,
        CancellationToken token = default)
    {
        _executed = 0;
        _loops = 0;
        _callDepth = InitialCallDepth;
        _cleanup?.Dispose();
        _cleanup = null;
        _runToken = token;
        _failure = null;
        _returnedInsideScript = false;
        _caught = [];
        _log.Clear();
        _images.Clear();
        _frames.Clear();
        _lastFrame = 0;
        _agreed.Clear();
        _steps = steps;

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
                && SeedStart(signature) is { } startFailure)
            {
                _failure = startFailure;
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
                _failure = new StepFailure(LoopControlKey(signal));
                Log(LogLevel.Error, 0, string.Empty, _failure.Key);
                signal = Signal.Failed;
            }

            var outcome = signal switch
            {
                Signal.Stop => new RunResult(RunStatus.Stopped, string.Empty, string.Empty, _executed),
                Signal.Failed => new RunResult(RunStatus.Failed, _failure?.Key ?? RunKeys.Failed,
                    _failure?.Detail ?? string.Empty, _executed),
                _ => new RunResult(RunStatus.Completed, string.Empty, string.Empty, _executed),
            };

            Log(LogLevel.Info, 0, string.Empty, SummaryKey(outcome.Status), _executed);

            if (outcome.Status is RunStatus.Failed)
            {
                LeaveFailureLog();
            }

            return outcome;
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warn, 0, string.Empty, LogKeys.Stopped, _executed);
            return new RunResult(RunStatus.Stopped, string.Empty, string.Empty, _executed);
        }
        finally
        {
            // Whatever the run decided, the machine is handed back the way it was found.
            LetGoOfHeldInput();

            _cleanup?.Dispose();
            _cleanup = null;
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

    /// <summary>How much of the macro's name a file name keeps before it is cut short.</summary>
    private const int LongestNameInFile = 40;

    /// <summary>
    /// Leaves the whole log of this run behind as a text file when it stops on a failure.
    /// </summary>
    /// <remarks>
    /// Everything the run wrote goes down, not only the line that failed: what went wrong is read
    /// next to the steps that led up to it. The file is written whether or not anything goes wrong
    /// while writing it — a run that has already failed must not be brought down by the act of
    /// recording it, so a failure here is only logged.
    /// </remarks>
    private void LeaveFailureLog()
    {
        try
        {
            var now = DateTimeOffset.Now;
            var stem = "failure-" + NameForFile()
                + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var relative = Path.Combine(FailureLogFolder, stem + ".log");
            for (var counter = 1; _devices.Files.Exists(relative); counter++)
            {
                relative = Path.Combine(FailureLogFolder,
                    string.Create(CultureInfo.InvariantCulture, $"{stem}-{counter}.log"));
            }

            _devices.Files.WriteText(relative, FailureLogText(now), append: false, TextEncoding.Utf8Bom);
            Log(LogLevel.Info, 0, string.Empty, LogKeys.FailureLog, _devices.Files.Resolve(relative));
        }
        catch (Exception failure)
        {
            Log(LogLevel.Warn, 0, string.Empty, LogKeys.FailureLogFailed, failure.Message);
        }
    }

    /// <summary>
    /// What the file a failed run leaves behind says: which macro it was and when, then every line
    /// the run wrote with the time it was written. The lines are indented by two spaces for each
    /// level, because a text file has no other way of saying that a step is inside a block.
    /// </summary>
    private string FailureLogText(DateTimeOffset when)
    {
        var text = new StringBuilder();
        text.Append(_host.Text(new LogEntry(when, LogLevel.Info, 0, string.Empty, LogKeys.LogFileHeader,
                [RunName, when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)])))
            .Append(Environment.NewLine)
            .Append(Environment.NewLine);

        foreach (var entry in _log)
        {
            text.Append(entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(' ', entry.Depth * 2)
                .Append(_host.Text(entry))
                .Append(Environment.NewLine);
        }

        return text.ToString();
    }

    /// <summary>
    /// The run's own name cut down to what a file name can carry, with a dash after it, or nothing
    /// at all when the name has nothing usable in it. The name is the user's, so anything a file
    /// name cannot hold becomes an underscore rather than stopping the file from being written.
    /// </summary>
    private string NameForFile()
    {
        var forbidden = Path.GetInvalidFileNameChars();
        var kept = new StringBuilder();
        foreach (var letter in RunName.Trim())
        {
            if (kept.Length == LongestNameInFile)
            {
                break;
            }

            kept.Append(forbidden.Contains(letter) ? '_' : letter);
        }

        // A name may not end in a dot or a space: Windows reads that as the end of the name.
        var name = kept.ToString().Trim(' ', '.');
        return name.Length == 0 ? string.Empty : name + "-";
    }
}
