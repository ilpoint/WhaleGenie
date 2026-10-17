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

/// <summary>
/// Walks a macro's steps and carries them out. Everything that is not a device action — the
/// control flow, the variables, the expressions, the lists, and the bookkeeping the step
/// settings ask for — happens here, which is what makes the engine testable on its own.
/// </summary>
/// <remarks>
/// Actions from <c>input</c>, <c>vision</c>, <c>ocr</c> and <c>uia</c> need the device layer
/// and are reported as unsupported until that is wired up.
/// </remarks>
public sealed partial class MacroRunner
{
    // What this class holds, by lifetime. Program-wide: the shared global table, the two gates
    // and the device layer. One run: _host, _devices, _macros, _looks, Variables, _log, _steps,
    // _children, _frames, _references, _looked and _runToken. One step: _failure, _worked, _saw,
    // _read, _takenWindow, _takenMethod, _heldKeys, _heldButtons, _gamepadUsed and _browserHolds.
    // The family methods live beside this file in MacroRunner.<Family>.cs.

    private readonly IRunHost _host;
    private readonly IDeviceLayer _devices;
    private readonly IMacroLibrary _macros;
    private readonly IRunLooks? _looks;

    /// <summary>
    /// What the step being run has worked out so far, newest last, or null when nobody is watching
    /// this run. A step that holds steps hands its own list back to the one around it once the steps
    /// inside are done, so every result is filed under the step that worked it out.
    /// </summary>
    private List<StepResult>? _worked;

    /// <summary>Pictures captured during the run, kept under the name they were saved to.</summary>
    private readonly Dictionary<string, ImageFrame> _images = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the step going on right now has just seen, kept until the step is done so that one
    /// step hands over one look however many times it had to look to get there.
    /// </summary>
    private StepLook? _saw;

    /// <summary>The areas the last reading of the screen was taken from, so a look can draw them.</summary>
    private IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> _read = [];

    /// <summary>
    /// What the last picture taken for the step going on right now was taken of: which window, and
    /// by which means. It travels with the result the step leaves behind, because a step that found
    /// nothing is answered by asking whether it was even looking at the right window.
    /// </summary>
    private string _takenWindow = string.Empty;

    private CaptureWay? _takenMethod;

    /// <summary>
    /// The last picture taken of each source, for the steps that say a picture a little older will
    /// do. A macro that looks at the same window several times in a row then reads it once, which is
    /// what a per-second budget of looks is really about. It lives for one run: what a macro sees is
    /// what it saw while it was running, and the next run starts by looking again.
    /// </summary>
    private readonly Dictionary<(long Source, CaptureWay Method),
        (ImageFrame Frame, ScreenPoint Origin, CaptureWay Method, long TakenAt)> _frames = [];

    /// <summary>
    /// When each step last looked at the screen, for the steps that say how long to leave between
    /// two looks. A step that reads the same place on every pass of a loop spends most of its time
    /// reading a picture that has not changed, and this is what keeps it from asking the machine
    /// for more looks than the macro can use. A step is recognised by the identity written into the
    /// file where it has one, and by itself where it has none, which is what makes the same step in
    /// a loop the same step here.
    /// </summary>
    private readonly Dictionary<object, long> _looked = [];

    /// <summary>
    /// Reference pictures read from disk, kept beside the time the file was last written. A macro
    /// that looks for the same picture in a loop — or every two hundred milliseconds while it
    /// waits — reads and decodes it once instead of every time round. A picture that has been
    /// changed on disk no longer matches the stamp and is read again.
    /// </summary>
    private readonly Dictionary<string, (DateTime Written, ImageFrame Frame)> _references =
        new(StringComparer.OrdinalIgnoreCase);

    private int _executed;
    private int _callDepth;

    /// <summary>
    /// The cancellation token of the run as a whole, kept so a background run started by a step
    /// stops with the macro rather than with that one step's timeout.
    /// </summary>
    private CancellationToken _runToken;

    /// <summary>
    /// How long the clean-up blocks of one stopped run may take together. A stop that waited for
    /// every clean-up block to finish on its own could be held up by the very step the user is
    /// trying to get out of, so the budget is a stop's own, shared by all of its clean-up blocks.
    /// </summary>
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(5);

    /// <summary>The one budget a stop's clean-up blocks spend, made when the first one runs.</summary>
    private CancellationTokenSource? _cleanup;

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
    private readonly List<ChildRun> _children = [];

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
    /// True when this run waits for the one input door that the runs asking for it share, so two
    /// macros set up that way do not send their keys and clicks through each other. Off by default:
    /// a single macro never has anything to wait for, and one whose author has not thought about
    /// running two at once is left doing exactly what it did before.
    /// </summary>
    /// <remarks>
    /// A call runs with the called macro's own answer while it is in there — or with that answer
    /// or the caller's, so a caller that promised to take turns does not drop that promise just
    /// because the macro it calls says nothing.
    /// </remarks>
    public bool ExclusiveInput
    {
        get => _exclusiveInput;
        init => _exclusiveInput = value;
    }

    private bool _exclusiveInput;

    /// <summary>What a call to this macro runs with: its own answer, and never less than the caller's.</summary>
    private bool CallExclusiveInput(string name) => _macros.ExclusiveInput(name) || _exclusiveInput;

    /// <summary>
    /// How many calls deep this run already is. A background run carries the depth of the call that
    /// started it, so a chain that keeps starting itself in the background meets the same limit as
    /// one that calls itself in place.
    /// </summary>
    public int InitialCallDepth { get; init; }

    /// <summary>Raised when a background run is started, finishes, or is waited for.</summary>
    public event Action? ChildrenChanged;

    /// <summary>
    /// The background runs this run has started and not yet waited for, oldest first. A watcher
    /// shows these so "what is still going on behind this macro" has an answer.
    /// </summary>
    public IReadOnlyList<ChildRunInfo> Children
        => [.. _children.Select(child =>
            new ChildRunInfo(child.Handle, child.Macro, !child.Task.IsCompleted))];

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
        _looked.Clear();
        _steps = steps;

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

            // A background run belongs to the macro that started it. Once that macro is over there
            // is nobody left to wait for it, so it is stopped rather than left running unseen.
            await StopChildrenAsync();

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

    /// <summary>
    /// Stops every background run this macro started and never waited for. A device call already
    /// under way may not look at a cancellation until it returns, so the wait for them is bounded:
    /// the macro is over, and refusing to end until a driver lets go would leave the whole run
    /// hanging.
    /// </summary>
    private async Task StopChildrenAsync()
    {
        var running = _children.Where(child => !child.Task.IsCompleted).ToList();
        if (running.Count == 0)
        {
            return;
        }

        foreach (var child in running)
        {
            child.Stop.Cancel();
        }

        ChildrenChanged?.Invoke();
        await Task.WhenAny(
            Task.WhenAll(running.Select(child => child.Task)),
            Task.Delay(5000));
    }

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

    /// <summary>What a step tells the runner to do next.</summary>
    private enum Signal
    {
        Normal,
        Break,
        Continue,
        Return,
        Stop,
        Failed,
    }

    /// <summary>
    /// How the name of a step's outcome is built: <c>k3f9</c> is answered by
    /// <c>step.k3f9.outcome</c>. It travels as a name the run reads rather than as something the
    /// interface keeps, because a macro that asks about a step has to mean the same thing however
    /// the macro was started.
    /// </summary>
    private const string OutcomePrefix = "step.";

    /// <summary>The other end of an outcome's name.</summary>
    private const string OutcomeSuffix = ".outcome";

    /// <summary>The other end of a failure's name, answered by the same step id.</summary>
    private const string ErrorSuffix = ".error";

    /// <summary>
    /// Where the outcome of the step with this name is written. Anything that lists the run's own
    /// values asks here rather than spelling the name out a second time.
    /// </summary>
    public static string OutcomeName(string stepId) => OutcomePrefix + stepId + OutcomeSuffix;

    /// <summary>
    /// Where the reason a step failed is written, in the same shape and under the same id as its
    /// outcome: <c>k3f9</c> is answered by <c>step.k3f9.error</c>. It carries the engine's own name
    /// for the failure rather than the words on screen, so a macro that branches on the reason
    /// reads the same thing whatever language the interface is in.
    /// </summary>
    public static string ErrorName(string stepId) => OutcomePrefix + stepId + ErrorSuffix;

    /// <summary>
    /// The failure key a catch block reads, set when the block is entered and put back when it is
    /// left. A condition and a step see the same name, so "catch only this reason" and "rethrow
    /// it" are written against one answer rather than a remembered one.
    /// </summary>
    public const string CaughtKeyName = "caught.key";

    /// <summary>The detail that goes with <see cref="CaughtKeyName"/>.</summary>
    public const string CaughtDetailName = "caught.detail";

    /// <summary>
    /// True when a value is one of the names the run keeps about a step — how it ended, and why
    /// when it did not — rather than something the macro made for itself. A macro of any length
    /// leaves these behind for every step, so a list that showed them all mixed in with the macro's
    /// own values would be mostly these.
    /// </summary>
    public static bool IsRunStepName(string name)
        => name.StartsWith(OutcomePrefix, StringComparison.Ordinal)
           && (name.EndsWith(OutcomeSuffix, StringComparison.Ordinal)
               || name.EndsWith(ErrorSuffix, StringComparison.Ordinal));

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
    /// Conditions are left out on purpose, and so are the steps inside them: a condition is not a
    /// step of the macro, it belongs to the step it is written on, so it has no ending of its own
    /// and a name for it could only ever answer "not carried out".
    ///
    /// A called macro is prepared too, on the values it was given: its steps are not in the
    /// caller's list, and its names have no business in the caller's scope.
    /// </remarks>
    private void PrepareOutcomes(IReadOnlyList<ExecutableStep> steps)
    {
        foreach (var step in steps)
        {
            if (IsCondition(step))
            {
                continue;
            }

            WriteOutcome(step, OutcomeSkipped);
            foreach (var parameter in step.Parameters)
            {
                PrepareOutcomes(parameter.Steps);
            }
        }
    }

    /// <summary>A condition rather than a step: it is worked out by the step it is written on.</summary>
    private static bool IsCondition(ExecutableStep step)
        => ActionSemanticsCatalog.Find(step.Type)?.IsCondition ?? false;

    /// <summary>
    /// Writes down how one step ended and, when it did not work, why — for the conditions written
    /// after it. A step that worked or was left out says nothing about a reason: what the run did
    /// not fail at is not a failure to branch on.
    /// </summary>
    private void WriteOutcome(ExecutableStep step, string outcome, StepFailure? failure = null)
    {
        // A step without a name cannot be pointed at, so there is nothing to write. The editor
        // gives every step one; this is for a macro built by hand around the engine.
        if (step.Id.Length == 0)
        {
            return;
        }

        Variables.Set(OutcomeName(step.Id), Value.FromText(outcome));
        Variables.Set(ErrorName(step.Id), Value.FromText(failure?.Key ?? string.Empty));
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

    /// <summary>How many macros deep a run may go before it is called a mistake.</summary>
    private const int CallDepthLimit = 16;

    /// <summary>
    /// The most background runs one macro may have going at once. A loop that starts a run per
    /// item can otherwise fill the machine with runs faster than they end.
    /// </summary>
    public const int MaxBackgroundRuns = 32;

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
            if (_inputHolds > 0)
            {
                return await HeldBody(step, depth, token, input: true);
            }

            using var held = await InputGate.Take(RunName, token);
            return await HeldBody(step, depth, token, input: true);
        }

        if (_browserHolds > 0)
        {
            return await HeldBody(step, depth, token, input: false);
        }

        using var page = await BrowserGate.Take(RunName, token);
        return await HeldBody(step, depth, token, input: false);
    }

    /// <summary>The body of a hold, with its door counted for the steps inside it.</summary>
    private async Task<Signal> HeldBody(ExecutableStep step, int depth, CancellationToken token,
        bool input)
    {
        if (input)
        {
            _inputHolds++;
            try
            {
                return await RunSteps(step.Children("steps"), depth + 1, token);
            }
            finally
            {
                _inputHolds--;
            }
        }

        _browserHolds++;
        try
        {
            return await RunSteps(step.Children("steps"), depth + 1, token);
        }
        finally
        {
            _browserHolds--;
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
        var (name, steps, _, wanted, called) = ResolveCall(step);

        var caller = Variables.SwapLocal(called);
        _callDepth++;
        PrepareOutcomes(steps);

        // Inside the call, the question "does this run take turns sending input" is the called
        // macro's to answer — and the caller's answer is not dropped while its steps run in place.
        var outerExclusive = _exclusiveInput;
        _exclusiveInput = CallExclusiveInput(name);

        // The called macro starts outside every loop, whatever the caller is inside. A break in
        // there belongs to a loop of its own or to nothing; it must never reach out and cut a
        // loop in the caller short.
        var outerLoops = _loops;
        _loops = 0;

        // A catch block belongs to the macro it is written in. The called macro starts with none
        // of the caller's failures in reach, so a rethrow inside it can only mean one of its own.
        var outerCaught = _caught;
        _caught = [];
        var finished = false;
        try
        {
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
            _caught = outerCaught;
            _loops = outerLoops;
            _callDepth--;
            _exclusiveInput = outerExclusive;
            var produced = Variables.SwapLocal(caller);
            if (finished)
            {
                Take(wanted, produced);
            }
        }
    }

    /// <summary>
    /// What a call names, what the called macro declares, and the values it is handed. The
    /// arguments are read while the caller's values are still the ones in force, and the names the
    /// call asks to have back are checked against the called macro's declaration before anything
    /// runs: a name it never offered is a mistake in the call, said now rather than after a
    /// half-finished run.
    /// </summary>
    private (string Name, IReadOnlyList<ExecutableStep> Steps, MacroSignature Signature,
        List<(string Result, string Name)> Wanted, VariableBag Called) ResolveCall(ExecutableStep step)
    {
        var name = ReadValue(step, "macro").AsText().Trim();
        var steps = name.Length == 0 ? null : _macros.Steps(name);
        if (steps is null)
        {
            throw new StepFailure(RunKeys.MacroNotFound, name);
        }

        if (_callDepth >= CallDepthLimit)
        {
            throw new StepFailure(RunKeys.MacroTooDeep, name);
        }

        var signature = _macros.Signature(name) ?? MacroSignature.Empty;
        var wanted = WantedResults(step, signature);

        // A macro's own values are its own, so a call says what goes in and what comes back rather
        // than leaning on whatever happened to be left lying around.
        var called = new VariableBag();
        foreach (var argument in signature.Arguments)
        {
            called.Set(argument.Name, ArgumentValue(step, argument));
        }

        // The name of the macro that is running right now, which is what a macro writing a log line
        // or naming a file means by it.
        called.Set("sys.macroName", Value.FromText(name));
        return (name, steps, signature, wanted, called);
    }

    /// <summary>
    /// Starts a call in the background and leaves its handle in the caller's local table. The child
    /// reads and writes the same global table as its caller, but its local table is the one built
    /// from the call's arguments, so neither macro can see the other's local names. The handle says
    /// which run it is, what macro it runs, how it is doing, what went wrong, and which names its
    /// results will be written under once a wait takes them back.
    /// </summary>
    private Signal StartOtherMacro(ExecutableStep step, int depth)
    {
        var handleName = ReadText(step, "runHandleName").Trim();
        if (handleName.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingHandle);
        }

        if (_children.Count(child => !child.Task.IsCompleted) >= MaxBackgroundRuns)
        {
            throw new StepFailure(RunKeys.TooManyRuns,
                MaxBackgroundRuns.ToString(CultureInfo.InvariantCulture));
        }

        var (name, steps, signature, wanted, called) = ResolveCall(step);

        // A run that is inside a browser step holding the one page cannot start another run that
        // needs that page: it would wait for a door its own caller is holding, and wait forever.
        if (_browserHolds > 0 && UsesBrowser(steps, _macros))
        {
            throw new StepFailure(RunKeys.BrowserRunWhileHeld, name);
        }

        var childStore = new VariableStore(Variables.Global);
        childStore.SwapLocal(called);

        var runner = new MacroRunner(childStore, new ChildRunHost(_host), _devices, DelayScale,
            _macros)
        {
            RunName = name,
            ExclusiveInput = CallExclusiveInput(name),
            FailureLogFolder = FailureLogFolder,
            InitialCallDepth = _callDepth + 1,
        };

        var stop = CancellationTokenSource.CreateLinkedTokenSource(_runToken);
        var task = Task.Run(() => runner.RunAsync(steps, stop.Token));
        var child = new ChildRun
        {
            Handle = "run-" + Guid.NewGuid().ToString("N")[..12],
            Macro = name,
            Signature = signature,
            Store = childStore,
            Stop = stop,
            Wanted = wanted,
            Task = task,
            HandleName = handleName,
        };

        // The watcher shows that the run is over as well as that it started, which is what makes
        // "still running" something the window can answer rather than guess.
        _ = task.ContinueWith(_ => ChildrenChanged?.Invoke(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        _children.Add(child);
        Variables.Local.Set(handleName, HandleValue(child));
        Log(LogLevel.Info, depth, step.Type, LogKeys.StartedRun, name, child.Handle);
        ChildrenChanged?.Invoke();
        return Signal.Normal;
    }

    /// <summary>
    /// Waits for one background run and takes back what the call that started it asked for. The
    /// step's own timeout is the wait's clock; a timeout leaves the run going, so the macro may
    /// catch it and wait again.
    /// </summary>
    private async Task<Signal> WaitOtherMacro(ExecutableStep step, int depth, CancellationToken token)
    {
        var child = FindChild(ReadValue(step, "runHandle"));
        var result = await child.Task.WaitAsync(token);

        Finish(child);
        if (result.Status is RunStatus.Failed)
        {
            throw new StepFailure(result.Key, result.Detail);
        }

        if (result.Status is RunStatus.Stopped)
        {
            throw new StepFailure(RunKeys.ChildStopped, child.Macro);
        }

        Take(child.Wanted, child.Store.Local);
        Log(LogLevel.Info, depth, step.Type, LogKeys.WaitedRun, child.Macro);
        return Signal.Normal;
    }

    /// <summary>
    /// Looks at a background run without waiting for it: what it is doing or how it ended, and why
    /// if it failed. The handle variable is brought up to date, so a macro that polls can read the
    /// handle itself as well as these answers.
    /// </summary>
    private Signal RunStatusCheck(ExecutableStep step, int depth)
    {
        var handle = ReadValue(step, "runHandle");
        if (!handle.IsRecord || handle.Field(FieldNames.Id) is not { } identifier)
        {
            throw new StepFailure(RunKeys.NoSuchRun, handle.AsText());
        }

        var id = identifier.AsText();
        var status = handle.Field(FieldNames.Status)?.AsText() ?? string.Empty;
        var error = handle.Field(FieldNames.Error)?.AsText() ?? string.Empty;
        var child = _children.FirstOrDefault(candidate =>
            string.Equals(candidate.Handle, id, StringComparison.Ordinal));
        if (child is not null)
        {
            var current = HandleValue(child);
            if (Variables.Local.Contains(child.HandleName))
            {
                Variables.Local.Set(child.HandleName, current);
            }

            status = current.Field(FieldNames.Status)?.AsText() ?? status;
            error = current.Field(FieldNames.Error)?.AsText() ?? error;
        }
        else if (status == RunHandleStatus.Running || status.Length == 0)
        {
            // The run is no longer in this macro's table: it was waited for already, or the handle
            // belongs to another run. A snapshot that still says "running" would be a lie.
            throw new StepFailure(RunKeys.NoSuchRun, id);
        }

        Give(step, "status", Value.FromText(status));
        Give(step, "running", Value.FromBool(status == RunHandleStatus.Running));
        Give(step, "error", Value.FromText(error));
        return Signal.Normal;
    }
    /// <summary>
    /// Asks one background run to stop and waits for it to agree. A run that cannot stop within the
    /// wait's clock is reported as still running rather than being called over: the macro may catch
    /// that, and the run is still in the table of runs it can cancel or wait for again.
    /// </summary>
    private async Task<Signal> CancelOtherMacro(ExecutableStep step, int depth,
        CancellationToken token)
    {
        var child = FindChild(ReadValue(step, "runHandle"));
        child.Stop.Cancel();

        RunResult result;
        try
        {
            result = await child.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested
                                                && !_runToken.IsCancellationRequested)
        {
            throw new StepFailure(RunKeys.ChildStillRunning, child.Macro);
        }

        Finish(child);
        Give(step, "stopped", Value.FromBool(result.Status is RunStatus.Stopped));
        Log(LogLevel.Info, depth, step.Type, LogKeys.CancelledRun, child.Macro);
        return Signal.Normal;
    }

    /// <summary>
    /// Waits for every background run in a list and takes back each one's results. The call that
    /// started a run chose the names its results go under, so a list of handles is enough to say
    /// where every result lands.
    /// </summary>
    private async Task<Signal> WaitAllMacros(ExecutableStep step, int depth, CancellationToken token)
    {
        var children = ReadValue(step, "runHandles").AsList().Select(FindChild).Distinct().ToList();
        var results = await Task.WhenAll(children.Select(child => child.Task.WaitAsync(token)));

        // Every ending is looked at before any result is written: a list that failed partway
        // through hands back none of its answers rather than the ones it happened to reach first.
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var result = results[index];
            Finish(child);

            if (result.Status is RunStatus.Failed)
            {
                throw new StepFailure(result.Key, result.Detail);
            }

            if (result.Status is RunStatus.Stopped)
            {
                throw new StepFailure(RunKeys.ChildStopped, child.Macro);
            }
        }

        foreach (var child in children)
        {
            Take(child.Wanted, child.Store.Local);
        }

        Give(step, "count", Value.FromNumber(children.Count));
        return Signal.Normal;
    }

    /// <summary>
    /// The run a handle names, or a refusal: a handle from another macro, a handle already waited
    /// for, or something that is not a handle at all is not a run this macro can act on.
    /// </summary>
    private ChildRun FindChild(Value handle)
    {
        if (!handle.IsRecord || handle.Field(FieldNames.Id) is not { } id)
        {
            throw new StepFailure(RunKeys.NoSuchRun, handle.AsText());
        }

        var child = _children.FirstOrDefault(candidate =>
            string.Equals(candidate.Handle, id.AsText(), StringComparison.Ordinal));
        return child ?? throw new StepFailure(RunKeys.NoSuchRun, id.AsText());
    }

    /// <summary>
    /// Takes a finished run out of the table of runs this macro is owed, writes its handle back
    /// with how it ended — so reading the variable later says more than "running" — and lets go of
    /// the token that stopped it.
    /// </summary>
    private void Finish(ChildRun child)
    {
        if (!_children.Remove(child))
        {
            return;
        }

        child.Stop.Dispose();
        if (Variables.Local.Contains(child.HandleName))
        {
            Variables.Local.Set(child.HandleName, HandleValue(child));
        }

        ChildrenChanged?.Invoke();
    }

    /// <summary>
    /// What a run's handle says now: which run it is, what macro it runs, whether it is running or
    /// how it ended, the failure key if it failed, and the names its results will land under.
    /// </summary>
    private static Value HandleValue(ChildRun child)
    {
        var status = !child.Task.IsCompleted
            ? RunHandleStatus.Running
            : child.Task.Result.Status switch
            {
                RunStatus.Failed => RunHandleStatus.Failed,
                RunStatus.Stopped => RunHandleStatus.Stopped,
                _ => RunHandleStatus.Done,
            };

        return Value.FromRecord(
        [
            (FieldNames.Id, Value.FromText(child.Handle)),
            (FieldNames.Macro, Value.FromText(child.Macro)),
            (FieldNames.Status, Value.FromText(status)),
            (FieldNames.Error, Value.FromText(
                child.Task.IsCompleted ? child.Task.Result.Key : string.Empty)),
            (FieldNames.Results, Value.FromList(
                [.. child.Wanted.Select(wanted => Value.FromText(wanted.Name))])),
        ]);
    }

    /// <summary>
    /// What one argument of a call says, read the way every other value field is: <c>$n + 1</c>
    /// passes a number and <c>$list</c> passes the list itself rather than its text. A call that
    /// leaves the field empty gets the argument's default; a required argument with no default is
    /// the mistake the declaration exists to catch, so it stops the call before the called macro
    /// runs at all.
    /// </summary>
    private Value ArgumentValue(ExecutableStep step, MacroArgument argument)
    {
        var written = step.Text(MacroCallFields.Argument(argument.Name)).Trim();
        if (written.Length > 0)
        {
            return Read(written);
        }

        if (argument.Required)
        {
            throw new StepFailure(RunKeys.MissingArgument, argument.Name);
        }

        return argument.Default.Trim().Length > 0 ? Read(argument.Default) : Value.Null;
    }

    /// <summary>
    /// The results a call asks to have back, in the order it asked: one per row of the step's
    /// <c>returns</c>, each row carrying the called macro's own name for the value and the name the
    /// caller wants it under. A name the called macro does not offer is a mistake in the call.
    /// </summary>
    private static List<(string Result, string Name)> WantedResults(ExecutableStep step,
        MacroSignature signature)
    {
        var wanted = new List<(string Result, string Name)>();
        foreach (var row in step.Rows("returns"))
        {
            var result = row.TryGetValue(FieldNames.Key, out var key) ? key.Trim() : string.Empty;
            var name = row.TryGetValue(FieldNames.Name, out var written)
                ? written.Trim()
                : string.Empty;
            if (result.Length == 0 || name.Length == 0)
            {
                continue;
            }

            if (!signature.Offers(result))
            {
                throw new StepFailure(RunKeys.UndeclaredResult, result);
            }

            wanted.Add((result, name));
        }

        return wanted;
    }

    /// <summary>
    /// Copies the values a call asked to get back into the caller's own table. A name the called
    /// macro offers but never set comes back empty rather than missing, so the step after the call
    /// reads this call's answer and not whatever an earlier pass left behind.
    /// </summary>
    private void Take(List<(string Result, string Name)> wanted, VariableBag produced)
    {
        foreach (var (result, name) in wanted)
        {
            Variables.Local.Set(name,
                produced.TryGet(result, out var value) && value is not null ? value : Value.Null);
        }
    }

    /// <summary>The entries of a field written one to a line, whichever line ending was used.</summary>
    private static string[] Lines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

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
        var outerWorked = _worked;
        _worked = _looks is null ? null : [];
        var startedStep = Stopwatch.GetTimestamp();
        Expression.BeginDiagnostics();
        try
        {
            return await RunAttempts(step, depth, token, startedStep);
        }
        finally
        {
            _worked = outerWorked;
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
    /// Empties the variables this step was told to hand over, before it is tried. A name with no
    /// value is what a reader sees for a result the step did not work out this time.
    /// </summary>
    private void ClearResults(ExecutableStep step)
    {
        foreach (var row in step.Rows("returns"))
        {
            var name = row.TryGetValue(FieldNames.Name, out var written)
                ? written.Trim()
                : string.Empty;
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
            _failure = WaitedTooLong(stepToFail);
            Log(LogLevel.Error, depth, step.Type, _failure.Key, _failure.Detail);

            // The time is gone, so another try cannot be paid for. Everything else the step's
            // failure rule says — carry on, next round, stop, ask — still applies.
            var decided = await Decide(stepToFail, stopToken);
            return StepFailed(stepToFail, decided is Decision.Retry ? Decision.Stop : decided,
                Elapsed(startedStep), attempts);
        }

        for (var attempt = 0; ; attempt++)
        {
            // A result is this attempt's answer: one this attempt does not write reads as
            // empty rather than as what the previous attempt or the previous round left.
            ClearResults(step);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var signal = await ExecuteChecked(step, depth, attemptToken);

                // The clock catches what the token cannot: a device call that blocks until it is
                // finished never looks at a cancellation, so it is still told off for going over.
                if (OutOfTime() && !token.IsCancellationRequested)
                {
                    throw new StepFailure(RunKeys.Timeout, budget.ToString(CultureInfo.InvariantCulture));
                }

                _executed++;

                // A step that holds steps fails when one of them fails. That arrives as a signal
                // rather than as an exception — the step that failed has already said what it
                // wants done about it — so the settings on this step are what decide what a
                // failure it did not handle means: try the whole block again, leave the rest of
                // it out, go on to the next round of the loop it sits in, or stop. A step that
                // handled its own failure never gets this far, which is what makes the two
                // levels work together: the inside decides first, the block decides what is left.
                if (signal is Signal.Failed)
                {
                    var failure = _failure ?? new StepFailure(RunKeys.Failed);
                    _failure = failure;
                    attempts.Add(Took(attempt + 1, started, OutcomeFailed, failure, overran: false));

                    var decided = attempt < step.Meta.RetryCount
                        ? Decision.Retry
                        : await Decide(step, token);
                    if (decided is Decision.Retry)
                    {
                        if (!await WaitBeforeRetry(step, attempt, Remaining(), attemptToken, token))
                        {
                            return await Spent(step, token);
                        }

                        Log(LogLevel.Warn, depth, step.Type, LogKeys.Retry, attempt + 1);
                        continue;
                    }

                    return StepFailed(step, decided, Elapsed(startedStep), attempts);
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
                _failure = failure;
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
                _failure = failure;
                Log(LogLevel.Error, depth, step.Type, failure.Key, failure.Detail);

                var decided = attempt < step.Meta.RetryCount
                    ? Decision.Retry
                    : await Decide(step, token);
                if (decided is Decision.Retry)
                {
                    if (!await WaitBeforeRetry(step, attempt, Remaining(), attemptToken, token))
                    {
                        return await Spent(step, token);
                    }

                    Log(LogLevel.Warn, depth, step.Type, LogKeys.Retry, attempt + 1);
                    continue;
                }

                return StepFailed(step, decided, Elapsed(startedStep), attempts);
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
                    PixelColor.Parse(ReadValue(step, "color").AsText()).ToHex()),
                "vision.waitImage" or "vision.clickImage" => new StepFailure(RunKeys.ImageNotFound,
                    Written(step.Rows("images"))),
                "vision.waitDetect" or "vision.clickDetect" => new StepFailure(RunKeys.DetectNotFound,
                    Looking(step)),
                "vision.waitStable" => new StepFailure(RunKeys.ScreenNotStable, patience),
                "ocr.clickText" => new StepFailure(RunKeys.TextNotFound,
                    ReadValue(step, "queryText").AsText()),
                "control.waitUntil" => new StepFailure(RunKeys.WaitTimeout, WaitingFor(step, patience)),
                "clipboard.waitChange" or "clipboard.copy" =>
                    new StepFailure(RunKeys.ClipboardTimeout),
                "process.waitFor" => new StepFailure(RunKeys.ProgramNotFound,
                    ReadValue(step, "processName").AsText()),
                "process.waitExit" => new StepFailure(RunKeys.ProcessTimeout,
                    ProcessId(step).ToString(CultureInfo.InvariantCulture)),
                "window.waitFor" => new StepFailure(RunKeys.WindowTimeout, WindowText(step)),
                "uia.waitElement" or "uia.click" or "uia.invoke" =>
                    new StepFailure(RunKeys.ElementNotFound, ReadText(step, "uiSelector")),
                "command.run" => new StepFailure(RunKeys.CommandTimeout,
                    ReadValue(step, "commandFile").AsText()),
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
        => step.Condition("condition")?.Type is { Length: > 0 } asked ? asked : patience;

    /// <summary>
    /// The step is over and it did not work: writes that down and hands back what its failure rule
    /// decided the run should do next. Both the failure that arrived as a signal from the steps
    /// inside this one and the one that arrived as an exception come through here, so a condition
    /// written after a step always has an answer by the time it is asked.
    /// </summary>
    private Signal StepFailed(ExecutableStep step, Decision decided, double elapsedMs,
        IReadOnlyList<StepAttempt> attempts)
    {
        WriteOutcome(step, OutcomeFailed, _failure);
        Report(step, OutcomeFailed, elapsedMs, _failure, Worked(), attempts);
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
        => _worked is { Count: > 0 } worked ? [.. worked] : [];

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
            StepErrorAction.AskUser => await Ask(step, _failure ?? new StepFailure(RunKeys.Failed), token),
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
        _takenWindow = string.Empty;
        _takenMethod = null;

        // The shared browser is its own piece of state. Every browser step takes this door, whether
        // or not the macro asked to take turns sending input: another run must not switch the page
        // between this step's click and its next read.
        if (UsesBrowser(step.Type))
        {
            if (_browserHolds > 0)
            {
                _browserHolds++;
                try
                {
                    return await Dispatch(step, depth, token);
                }
                finally
                {
                    _browserHolds--;
                }
            }

            using var page = await BrowserGate.Take(RunName, token);
            _browserHolds++;
            try
            {
                return await Dispatch(step, depth, token);
            }
            finally
            {
                _browserHolds--;
            }
        }

        // A step that sends keys or clicks goes through the input door when this run asked to share
        // it. What is kept apart is the input, so the door is taken and given up around one step
        // rather than held for the whole macro.
        // A step that reads the screen may spend most of its time waiting for something to turn
        // up. It takes the input door only around the click itself, so a macro waiting for an
        // image does not keep another macro's keys out while it waits.
        if (!ExclusiveInput || _inputHolds > 0 || !SendsInput(step.Type) || LooksAtScreen(step.Type))
        {
            return await Dispatch(step, depth, token);
        }

        using var inside = await InputGate.Take(RunName, token);
        return await Dispatch(step, depth, token);
    }

    /// <summary>
    /// True for the actions that put something into the machine through the keyboard, the mouse or
    /// a controller. Everything else — looking at the screen, reading a file, asking a window what
    /// it is — reads rather than writes and has nothing to keep apart.
    /// </summary>
    public static bool SendsInput(string type)
        => ActionSemanticsCatalog.Find(type)?.SendsInput ?? false;

    /// <summary>
    /// True for the actions that work through the one shared browser. They are kept apart from
    /// each other rather than from the keyboard: the browser's current page is shared state that
    /// every run reads, while sending input is a different door.
    /// </summary>
    public static bool UsesBrowser(string type)
        => ActionSemanticsCatalog.Find(type)?.Category is ActionCategory.Browser;

    /// <summary>
    /// True when any of these steps, or a macro they call, works through the one shared browser.
    /// The walk follows calls with the names it is inside, so a macro that calls itself does not
    /// turn the answer into a stack overflow.
    /// </summary>
    public static bool UsesBrowser(IReadOnlyList<ExecutableStep> steps, IMacroLibrary macros)
        => ReachesCalls(steps, macros, UsesBrowser, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The same question for sending input, asked of a step tree and the macros it calls.</summary>
    public static bool SendsInput(IReadOnlyList<ExecutableStep> steps, IMacroLibrary macros)
        => ReachesCalls(steps, macros, SendsInput, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static bool ReachesCalls(IReadOnlyList<ExecutableStep> steps, IMacroLibrary macros,
        Func<string, bool> direct, HashSet<string> walking)
        => steps.Any(step => ReachesCalls(step, macros, direct, walking));

    private static bool ReachesCalls(ExecutableStep step, IMacroLibrary macros,
        Func<string, bool> direct, HashSet<string> walking)
    {
        if (direct(step.Type))
        {
            return true;
        }

        foreach (var parameter in step.Parameters)
        {
            if (ReachesCalls(parameter.Steps, macros, direct, walking))
            {
                return true;
            }
        }

        if (step.Type is not ("control.runMacro" or "control.startMacro"))
        {
            return false;
        }

        var name = step.Text("macro").Trim();
        if (name.Length == 0 || !walking.Add(name))
        {
            return false;
        }

        try
        {
            return macros.Steps(name) is { } called
                && ReachesCalls(called, macros, direct, walking);
        }
        finally
        {
            walking.Remove(name);
        }
    }

    /// <summary>
    /// Answers one step by handing it to the family it belongs to. Which family an action is in
    /// is the catalogue's answer, so this switch does not know any action by name — adding one
    /// is a matter of writing its method and its family's dispatch, not of touching this.
    /// </summary>
    private Task<Signal> Dispatch(ExecutableStep step, int depth, CancellationToken token)
        => ActionSemanticsCatalog.Find(step.Type)?.Category switch
        {
            ActionCategory.Control => DispatchControl(step, depth, token),
            ActionCategory.File => DispatchFile(step, depth, token),
            ActionCategory.Data => DispatchData(step, depth, token),
            ActionCategory.Clipboard => DispatchClipboard(step, depth, token),
            ActionCategory.Process => DispatchProcess(step, depth, token),
            ActionCategory.System => DispatchSystem(step, depth, token),
            ActionCategory.Window => DispatchWindow(step, depth, token),
            ActionCategory.Input => DispatchInput(step, depth, token),
            ActionCategory.Vision => DispatchVision(step, depth, token),
            ActionCategory.Ocr => DispatchOcr(step, depth, token),
            ActionCategory.Uia => DispatchUia(step, depth, token),
            ActionCategory.Browser => DispatchBrowser(step, depth, token),
            ActionCategory.Script => DispatchScript(step, depth, token),
            _ => throw new StepFailure(RunKeys.Unsupported, step.Type),
        };
    /// <summary>
    /// The control actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchControl(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "control.sequence":
                return await RunSteps(step.Children("steps"), depth + 1, token);
            case "control.holdInput":
                return await RunHold(step, depth, token, input: true);
            case "control.holdBrowser":
                return await RunHold(step, depth, token, input: false);
            case "control.runMacro":
                return await RunOtherMacro(step, depth, token);
            case "control.startMacro":
                return StartOtherMacro(step, depth);
            case "control.waitMacro":
                return await WaitOtherMacro(step, depth, token);
            case "control.runStatus":
                return RunStatusCheck(step, depth);
            case "control.cancelMacro":
                return await CancelOtherMacro(step, depth, token);
            case "control.waitAll":
                return await WaitAllMacros(step, depth, token);
            case "control.delay":
                await Pause(Pace(Number(step, "ms")), token);
                return Signal.Normal;
            case "control.delayRandom":
                await Pause(Pace(System.Random.Shared.Next(
                    Number(step, "minMs"), Number(step, "maxMs") + 1)), token);
                return Signal.Normal;
            case "control.waitUntil":
                return await RunWaitUntil(step, depth, token);
            case "control.repeat":
                return await RunRepeat(step, depth, token);
            case "control.while":
                return await RunWhile(step, depth, token);
            case "control.forEach":
                return await RunForEach(step, depth, token);
            case "control.for":
                return await RunFor(step, depth, token);
            case "control.switch":
                return await RunSwitch(step, depth, token);
            case "control.if":
                return await RunIf(step, depth, token);
            case "control.try":
                return await RunTry(step, depth, token);
            case "control.break":
                return LeaveLoop(Signal.Break, step);
            case "control.continue":
                return LeaveLoop(Signal.Continue, step);
            case "control.return":
                return Signal.Return;
            case "control.rethrow":
                throw CaughtFailure();
            case "control.stop":
                Log(LogLevel.Warn, depth, step.Type, LogKeys.StopRequested,
                    ReadValue(step, "reason").AsText());
                return Signal.Stop;
            case "control.fail":
                // The macro says this step did not work. It travels the same way a device failure
                // does, so whatever the step's failure rule says — retry, carry on, ask — holds for
                // a step the macro itself called failed.
                throw new StepFailure(RunKeys.StepFailedByMacro, ReadValue(step, "reason").AsText());
            case "control.note":
                // A note is for whoever reads the macro. There is nothing to do and nothing to say.
                return Signal.Normal;
            case "control.setVariable":
                SetVariable(step, depth);
                return Signal.Normal;
            case "control.calculate":
                Calculate(step, depth);
                return Signal.Normal;
            case "control.incrementGlobal":
                IncrementGlobal(step, depth);
                return Signal.Normal;
            case "control.listCreate":
                CreateList(step, depth);
                return Signal.Normal;
            case "control.listAdd":
            case "control.listInsert":
            case "control.listSet":
            case "control.listRemoveAt":
            case "control.listClear":
                ChangeList(step, depth);
                return Signal.Normal;
            case "control.log":
                Log(Level(ReadText(step, "level")), depth, step.Type, LogKeys.Message, ReadValue(step, "message").AsText());
                return Signal.Normal;
            case "control.ask":
                return await AskPerson(step, depth, token);
            case "control.input":
                return await AskForText(step, depth, token);

            // ------------------------------------------------------------------- file
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }

    /// <summary>
    /// Waits for a condition of the macro's own choosing: any of the conditions an if or a while
    /// can use, asked again and again until it holds. This is the general shape of every other
    /// wait in the catalogue — waiting for a colour, a picture or an element is this block with
    /// the question already written in. How long it may wait is the step's own timeout rather than
    /// a number of its own, so "give up after a while" is answered in one place for every wait.
    /// </summary>
    private async Task<Signal> RunWaitUntil(ExecutableStep step, int depth, CancellationToken token)
    {
        var condition = step.Condition("condition")
                        ?? throw new StepFailure(RunKeys.MissingCondition);
        var poll = Number(step, "intervalMs");
        if (poll <= 0)
        {
            poll = 200;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await WaitForFlagAsync(() => Check(condition, depth, token), poll, token);
        }
        catch (OperationCanceledException)
        {
            // The step's own clock ran out rather than the condition holding. How long it waited is
            // worth keeping all the same: that number is what makes a macro that is too slow to
            // catch something diagnosable, and the waiting is over either way. Which failure this
            // becomes is not this step's to decide — the timeout rule reads the step's type.
            Give(step, "elapsed", Value.FromNumber(Elapsed(started)));
            throw;
        }

        var waited = Elapsed(started);
        Give(step, "elapsed", Value.FromNumber(waited));
        Log(LogLevel.Info, depth, step.Type, LogKeys.WaitedFor, waited, condition.Type);
        return Signal.Normal;
    }

    /// <summary>How long something has been going, in whole milliseconds.</summary>
    private static int Elapsed(long started)
        => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private async Task<Signal> RunRepeat(ExecutableStep step, int depth, CancellationToken token)
    {
        var times = Math.Max(0, Number(step, "times"));
        var interval = Pace(Number(step, "intervalMs"));
        using var scope = EnterLoop(VariableStore.LoopIndexName);

        for (var round = 0; round < times; round++)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set(VariableStore.LoopIndexName, Value.FromNumber(round));
            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Return or Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                Give(step, "ended", Value.FromText("break"));
                return Signal.Normal;
            }

            await Pause(interval, token);
        }

        Give(step, "ended", Value.FromText("count"));
        return Signal.Normal;
    }

    private async Task<Signal> RunWhile(ExecutableStep step, int depth, CancellationToken token)
    {
        var condition = step.Condition("condition")
                        ?? throw new StepFailure(RunKeys.MissingCondition);
        var limit = Math.Max(1, Number(step, "maxIterations"));
        using var scope = EnterLoop(VariableStore.LoopIndexName);

        for (var round = 0; round < limit; round++)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set(VariableStore.LoopIndexName, Value.FromNumber(round));
            if (!Check(condition, depth, token))
            {
                Give(step, "ended", Value.FromText("condition"));
                return Signal.Normal;
            }

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Return or Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                Give(step, "ended", Value.FromText("break"));
                return Signal.Normal;
            }
        }

        Give(step, "ended", Value.FromText("limit"));
        Log(LogLevel.Warn, depth, step.Type, LogKeys.LoopLimit, limit);
        return Signal.Normal;
    }

    /// <summary>
    /// Runs one round of a loop's body. Every loop goes through here so a break or a continue
    /// written inside a body finds a loop above it; the depth is what tells the two apart from
    /// one written where no loop is in reach.
    /// </summary>
    private async Task<Signal> RunBody(ExecutableStep step, int depth, CancellationToken token)
    {
        // The body is this round's: a step left out of it this time reads as skipped rather
        // than as whatever it came to in the round before.
        PrepareOutcomes(step.Children("body"));
        _loops++;
        try
        {
            return await RunSteps(step.Children("body"), depth + 1, token);
        }
        finally
        {
            _loops--;
        }
    }

    /// <summary>
    /// What a break or a continue asks the loop it sits in to do. With no loop above it there is
    /// nothing to ask, so the step fails instead of quietly ending the macro where it stands: a
    /// run that reports success after skipping the rest of a macro is a lie the user cannot see.
    /// </summary>
    private Signal LeaveLoop(Signal signal, ExecutableStep step)
    {
        // A break also has somewhere to go inside a switch; a continue only ever means a loop.
        var reachable = signal is Signal.Break ? _loops > 0 || _switches > 0 : _loops > 0;
        return reachable ? signal : throw new StepFailure(LoopControlKey(signal), step.Type);
    }

    private async Task<Signal> RunForEach(ExecutableStep step, int depth, CancellationToken token)
    {
        if (!ListText.TryParse(ReadText(step, "items"), Variables, out var items, out var error))
        {
            throw new StepFailure(RunKeys.BadList, error?.Detail ?? ReadText(step, "items"));
        }

        var itemName = ReadText(step, "itemVariable").Trim();
        if (itemName.Length == 0)
        {
            itemName = "item";
        }

        var order = string.Equals(ReadText(step, "reverse"), "true", StringComparison.OrdinalIgnoreCase)
            ? items.Reverse()
            : items;

        // The round number is only worth having when the step asked for it by name: a variable
        // nobody asked for is one more thing to puzzle over in the variable list.
        var indexName = ReadText(step, "indexVariable").Trim();
        var interval = Pace(Number(step, "intervalMs"));
        var index = 0;
        using var indexScope = EnterLoop(VariableStore.LoopIndexName);
        using var nameScope = EnterLoop(true, itemName, indexName);
        foreach (var item in order)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set(VariableStore.LoopIndexName, Value.FromNumber(index));
            Variables.Set(itemName, item);
            if (indexName.Length > 0)
            {
                Variables.Set(indexName, Value.FromNumber(index));
            }

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Return or Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                Give(step, "ended", Value.FromText("break"));
                return Signal.Normal;
            }

            index++;
            await Pause(interval, token);
        }

        Give(step, "ended", Value.FromText("items"));
        return Signal.Normal;
    }

    /// <summary>
    /// Counts from one number to another, running the body once per value. The counter is a variable
    /// the body can read, which is the difference between this and "repeat": repeating counts the
    /// rounds in the runner's head, while counting hands the number to the macro.
    /// </summary>
    private async Task<Signal> RunFor(ExecutableStep step, int depth, CancellationToken token)
    {
        var from = Number(step, "rangeFrom");
        var to = Number(step, "rangeTo");
        var stride = Number(step, "stepSize");
        if (stride == 0)
        {
            // A step is what the counter moves by, so a zero would never move it at all. Taking the
            // direction from where it starts and where it ends is what that can only have meant.
            stride = to >= from ? 1 : -1;
        }

        // The counter is a variable the step itself names, and a loop with no counter has nothing
        // to run its body against, so an empty field falls back to the name every for-loop uses.
        var counter = ReadText(step, "name").Trim();
        if (counter.Length == 0)
        {
            counter = "i";
        }
        var interval = Pace(Number(step, "intervalMs"));
        var round = 0;
        using var indexScope = EnterLoop(VariableStore.LoopIndexName);
        using var nameScope = EnterLoop(true, counter);

        for (var value = from; stride > 0 ? value <= to : value >= to; value += stride)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set(VariableStore.LoopIndexName, Value.FromNumber(round));
            Variables.Set(counter, Value.FromNumber(value));

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Return or Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                Give(step, "ended", Value.FromText("break"));
                return Signal.Normal;
            }

            round++;
            await Pause(interval, token);
        }

        Give(step, "ended", Value.FromText("range"));
        return Signal.Normal;
    }

    /// <summary>
    /// Compares one value against the values of each case in turn and runs the first case that
    /// matches. The rest are left alone, and the otherwise steps take over when nothing matches —
    /// that is what makes a switch different from a row of separate ifs.
    /// </summary>
    private async Task<Signal> RunSwitch(ExecutableStep step, int depth, CancellationToken token)
    {
        var value = ReadValue(step, "switchValue");
        var mode = ReadText(step, "switchMode");

        foreach (var branch in step.Children("cases"))
        {
            if (branch.Type != "control.case")
            {
                continue;
            }

            foreach (var wanted in Labels(branch))
            {
                if (CaseMatches(step, value, wanted, mode))
                {
                    var signal = await RunBranch(branch, depth, token);

                    // A branch is a block of the macro like any other, so it says how it went
                    // too. It is run from here rather than through the walk, which is why it has
                    // to be written down by hand.
                    WriteOutcome(branch, signal is Signal.Failed ? OutcomeFailed : OutcomeOk);

                    // A break written in a case leaves this switch, not the loop around it. A
                    // continue is still the loop's: it has nothing to say about a switch.
                    return signal is Signal.Break ? Signal.Normal : signal;
                }
            }
        }

        var otherwise = await RunBranch(step, depth, token, "otherwise");
        return otherwise is Signal.Break ? Signal.Normal : otherwise;
    }

    /// <summary>
    /// Runs one branch of a switch as a place a break can stand: the switch is counted while the
    /// branch runs, so a break that reaches the runner knows there is something to leave.
    /// </summary>
    private async Task<Signal> RunBranch(ExecutableStep step, int depth, CancellationToken token,
        string children = "body")
    {
        _switches++;
        try
        {
            return await RunSteps(step.Children(children), depth + 1, token);
        }
        finally
        {
            _switches--;
        }
    }

    /// <summary>
    /// The values a case answers to, separated by a semicolon or a line break. They are read
    /// through the same $name substitution as everywhere else, so a case value can name a variable.
    /// </summary>
    private IReadOnlyList<string> Labels(ExecutableStep branch)
        => [.. branch.Text("values")
            .Split([';', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(label => Interpolate(label).Trim())
            .Where(label => label.Length > 0)];

    /// <summary>Compares the switch's value against one case value the way the step chose.</summary>
    private bool CaseMatches(ExecutableStep step, Value value, string wanted, string mode)
    {
        switch (mode.Trim().ToLowerInvariant())
        {
            case "notequals":
                return !CaseEquals(step, value, wanted);
            case "contains":
            case "startswith":
            case "endswith":
                // The ways of comparing text are the ones the whole program shares.
                return TextCompare.Matches(value.AsText(), wanted, mode, OptionsOf(step, mode));
            case "greaterthan":
                return Order(step, value.AsText(), wanted) > 0;
            case "greaterorequal":
                return Order(step, value.AsText(), wanted) >= 0;
            case "lessthan":
                return Order(step, value.AsText(), wanted) < 0;
            case "lessorequal":
                return Order(step, value.AsText(), wanted) <= 0;
            case "regex":
                try
                {
                    return TextCompare.Matches(value.AsText(), wanted, TextCompare.Regex,
                        OptionsOf(step, TextCompare.Regex));
                }
                catch (ArgumentException)
                {
                    // A pattern the machine cannot read is the macro's problem to see, not a
                    // stack trace to puzzle over, so it comes back as a failed step.
                    throw new StepFailure(RunKeys.BadPattern, wanted);
                }
            default:
                return CaseEquals(step, value, wanted);
        }
    }

    /// <summary>
    /// Equality keeps the number reading it has always had, and hands the text reading to the one
    /// shared comparison so the spacing and case switches mean the same thing here as elsewhere.
    /// </summary>
    private bool CaseEquals(ExecutableStep step, Value value, string wanted)
        => value.NumericEquals(Value.FromText(wanted))
           || TextCompare.Matches(value.AsText(), wanted, TextCompare.Exactly,
               OptionsOf(step, TextCompare.Exactly));

    /// <summary>
    /// Puts two texts in order for a greater-than or less-than branch: as numbers when both read
    /// as numbers, and the way the expression language compares text otherwise, so "b" after "a"
    /// works and "10" still beats "9" rather than falling behind it.
    /// </summary>
    private int Order(ExecutableStep step, string value, string wanted)
        => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var left)
           && double.TryParse(wanted, NumberStyles.Any, CultureInfo.InvariantCulture, out var right)
            ? left.CompareTo(right)
            : string.Compare(value, wanted, OptionsOf(step, TextCompare.Contains).Comparison);

    private async Task<Signal> RunIf(ExecutableStep step, int depth, CancellationToken token)
    {
        var condition = step.Condition("condition")
                        ?? throw new StepFailure(RunKeys.MissingCondition);
        var matched = Check(condition, depth, token);
        return await RunSteps(step.Children(matched ? "then" : "else"), depth + 1, token);
    }

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
                    if (step.Condition("catchWhen") is not { } wanted || Check(wanted, depth, token))
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

    /// <summary>
    /// Answers one condition. Nothing here waits or hands work to another thread, so a condition
    /// can be asked again and again without holding anything up — which is what the wait block
    /// does with it.
    /// </summary>
    /// <summary>
    /// Whether a condition's answer comes from the screen, and so from a look at it. The ones that
    /// do are the ones whose own "how often the screen is looked at" counts, the same field the
    /// vision steps carry.
    /// </summary>
    private static bool LooksAtScreen(string conditionType)
        => ActionSemanticsCatalog.Find(conditionType)?.LooksAtScreen ?? false;

    /// <summary>
    /// What the catalogue says a step must be given, checked before the step runs. An unanswered
    /// field is a mistake to report rather than an empty value for the action to interpret; a word
    /// outside the choices is refused unless the field says an unknown word falls back. Both use
    /// the key the catalogue names for that field, so a path still says "no path" rather than the
    /// general word for an unanswered field.
    /// </summary>
    private static void ValidateAnswers(ExecutableStep step)
    {
        if (ActionSemanticsCatalog.Find(step.Type) is not { } definition)
        {
            return;
        }

        foreach (var facts in definition.Parameters)
        {
            var written = step.Text(facts.Name).Trim();
            if (facts.MissingValue(written))
            {
                throw new StepFailure(facts.MissingKey, facts.Name);
            }

            if (facts.Shape is ActionValueShape.Choice
                && written.Length > 0
                && facts.Options.Count > 0
                && !facts.Options.Contains(written, StringComparer.Ordinal)
                && !facts.AllowsUnknownOption)
            {
                throw new StepFailure(facts.OptionKey, written);
            }
        }
    }
    private bool Check(ExecutableStep condition, int depth, CancellationToken token)
    {
        ValidateAnswers(condition);
        token.ThrowIfCancellationRequested();
        if (LooksAtScreen(condition.Type))
        {
            // A condition about the screen is asked again on every pass of whatever block holds it,
            // so it is the same reading of the screen as a step's and leaves the same gap.
            GateNow(condition, token);
        }

        switch (condition.Type)
        {
            case "condition.compare":
                return Compare(condition);
            case "condition.group":
                return Group(condition, depth, token);
            case "condition.randomChance":
                return System.Random.Shared.NextDouble() * 100 < Number(condition, "chancePercent");
            case "condition.imageExists":
                return ImageThere(condition);
            case "condition.imageNotExists":
                return !ImageThere(condition);
            case "condition.textExists":
                return TextThere(condition);
            case "condition.textNotExists":
                return !TextThere(condition);

            case "condition.uiaExists":
                return _devices.Ui.Exists(Query(condition), 0);

            case "condition.uiaNotExists":
                return !_devices.Ui.Exists(Query(condition), 0);

            case "condition.browserExists":
                return _devices.Browser.Exists(Element(condition), ElementStateOf(condition));

            case "condition.browserNotExists":
                return !_devices.Browser.Exists(Element(condition), ElementStateOf(condition));

            case "condition.expression":
                return ReadValue(condition, "expression").AsBool();

            case "condition.stepResult":
                return StepResult(condition);

            case "condition.listContains":
                return ListHolds(condition);

            case "condition.pathExists":
                return _devices.Files.Exists(ReadValue(condition, "path").AsText());

            case "condition.processRunning":
                return _devices.Processes.Find(ReadValue(condition, "processName").AsText()).Count > 0;

            case "condition.windowExists":
                return WindowThere(condition);

            case "condition.valueInRange":
                return InRange(condition);

            case "condition.dateCompare":
                return DatesCompared(condition);

            case "condition.colorEquals":
                {
                    var target = PixelColor.Parse(ReadValue(condition, "color").AsText());
                    return PixelAt(condition).Matches(target, ReadValue(condition, "tolerance").AsNumber());
                }

            case "condition.colorsMatch":
                return ColoursMatch(condition);

            case "condition.colorsExist":
                return PatternHits(condition).Count > 0;

            case "condition.colorCoverage":
                return ColourShare(condition);

            default:
                Log(LogLevel.Warn, depth, condition.Type, LogKeys.UnknownCondition, condition.Type);
                return false;
        }
    }

    /// <summary>
    /// Whether one step of the macro ended up doing what a condition says it should have. The
    /// answer is read from the step's outcome, which the run writes as it goes — so a step the run
    /// has not reached yet answers "not reached" rather than "no", and a condition about a step
    /// that is not in the macro at all is a mistake rather than a quiet false.
    /// </summary>
    private bool StepResult(ExecutableStep condition)
    {
        var id = ReadText(condition, "stepId").Trim();
        if (id.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingStep);
        }

        var expected = ReadText(condition, "expectedOutcome").Trim();
        if (!Variables.TryGet(OutcomeName(id), out var outcome))
        {
            throw new StepFailure(RunKeys.NoSuchStep, id);
        }

        return string.Equals(outcome.AsText().Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a list holds a value. The list is read the way every other list field is, so it can
    /// be written out or named by a variable holding one; the value is compared the way an
    /// equality test compares, so "1" finds 1 and text is not told apart by case.
    /// </summary>
    private bool ListHolds(ExecutableStep condition)
    {
        if (!ListText.TryParse(ReadText(condition, "list"), Variables, out var items, out var error))
        {
            throw new StepFailure(RunKeys.BadList, error?.Detail ?? ReadText(condition, "list"));
        }

        var wanted = ReadValue(condition, "wantedValue");
        return items.Any(item => item.NumericEquals(wanted)
            || string.Equals(item.AsText(), wanted.AsText(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a window answering to a title or to a program's name is open. An empty value means
    /// "any window at all", which is how a macro waits for something to come up without knowing
    /// what Windows will call it.
    /// </summary>
    private bool WindowThere(ExecutableStep condition)
    {
        var rule = WindowRuleOf(condition, "window", "how");
        return _devices.Windows.Find(rule.Text, rule.Match, rule.Compare, rule.Options) is not null;
    }

    /// <summary>
    /// Whether a value sits between two others. Both ends count as inside, so a range of 1 to 10
    /// holds 1 and 10 — a range written with its ends included is what a number line looks like,
    /// and the hint says so where the macro is written.
    /// </summary>
    private bool InRange(ExecutableStep condition)
    {
        var value = ReadValue(condition, "testValue").AsNumber();
        var low = ReadValue(condition, "min").AsNumber();
        var high = ReadValue(condition, "max").AsNumber();
        return value >= Math.Min(low, high) && value <= Math.Max(low, high);
    }

    /// <summary>
    /// Whether one date is before, after or the same as another. Both sides are read the way every
    /// date function reads one, so <c>$sys.dateTime</c>, <c>2026-01-01</c> and <c>$today()</c> all
    /// work; "the same day" ignores the time of day, which is what "has this run today already"
    /// needs and what comparing two timestamps cannot answer.
    /// </summary>
    private bool DatesCompared(ExecutableStep condition)
    {
        var left = FunctionLibrary.Moment(DateText(ReadText(condition, "left")));
        var right = FunctionLibrary.Moment(DateText(ReadText(condition, "right")));
        return ReadText(condition, "dateOperator").Trim().ToLowerInvariant() switch
        {
            "before" => left < right,
            "after" => left > right,
            "sameday" => left.Date == right.Date,
            _ => left == right,
        };
    }

    /// <summary>
    /// Reads one side of a date comparison. A date written out is taken as written: 2026-01-31 is
    /// that day and not the arithmetic 2026 minus 1 minus 31, which is what every other value field
    /// would make of it — and a date that quietly turned into 1994 would be a bug nobody could see.
    /// A name, a <c>$name</c> or something plainly worked out, such as <c>today()</c>, is read the
    /// way every other value is.
    /// </summary>
    private string DateText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed.StartsWith('$') || trimmed.Contains('('))
        {
            return Read(trimmed).AsText();
        }

        return Variables.TryGet(trimmed, out var named) ? named.AsText() : trimmed;
    }

    /// <summary>True when a reference picture is somewhere in a condition's search areas.</summary>
    private bool ImageThere(ExecutableStep condition) => Search(condition) is not null;

    /// <summary>
    /// True when the text a condition names is somewhere in its search areas. The negative wording
    /// is a condition of its own rather than a switch on this one, so what a step means is written
    /// in its name instead of hidden in a checkbox.
    /// </summary>
    private bool TextThere(ExecutableStep condition)
    {
        var wanted = ReadValue(condition, "queryText").AsText();
        var way = TextWay(condition, wanted);
        return ReadSpans(condition).Any(span => Matches(condition, span.Text, wanted, way));
    }

    /// <summary>
    /// The way a step looks for text, read and checked before anything is looked at: a word that is
    /// none of the five, or a pattern the machine cannot read, is refused here rather than after a
    /// screen has been read and compared.
    /// </summary>
    private static string TextWay(ExecutableStep step, string wanted)
    {
        var way = Way(step.Text("matchMode"));
        try
        {
            TextCompare.Check(wanted, way);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, wanted);
        }

        return way;
    }

    /// <summary>
    /// Answers whether the points a condition names show the colours it names. Each entry is one
    /// "x,y,#RRGGBB", separated by a semicolon or a line break, and the mode decides whether every
    /// point has to match or one is enough.
    /// </summary>
    private bool ColoursMatch(ExecutableStep condition)
    {
        var tolerance = ReadValue(condition, "tolerance").AsNumber();
        var every = !string.Equals(ReadText(condition, "colorMatchMode").Trim(), "any",
            StringComparison.OrdinalIgnoreCase);
        var window = AnchorOf(condition).Window;

        var matched = Points(condition)
            .Select(point => PixelAt(condition, point.Where, window)
                .Matches(point.Colour, tolerance));

        return every ? matched.All(match => match) : matched.Any(match => match);
    }

    /// <summary>One point of a colour comparison: where it is, and the colour it has to show.</summary>
    private sealed record ColourPoint(ScreenPoint Where, PixelColor Colour);

    /// <summary>
    /// Reads a list of "x,y,#RRGGBB" points, separated by a semicolon or a line break. Each one is
    /// resolved where it is read, so the whole list can be counted from a window's corner.
    /// </summary>
    private IReadOnlyList<ColourPoint> Points(ExecutableStep step)
    {
        var text = ReadText(step, "points").Trim();
        var points = new List<ColourPoint>();
        foreach (var entry in text.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                || !IsColour(parts[2]))
            {
                throw new StepFailure(RunKeys.BadPoints, entry);
            }

            points.Add(new ColourPoint(Place(step, x, y), PixelColor.Parse(parts[2])));
        }

        if (points.Count == 0)
        {
            throw new StepFailure(RunKeys.BadPoints, text);
        }

        return points;
    }

    /// <summary>True when the text reads as a #RRGGBB or #RGB colour.</summary>
    private static bool IsColour(string text)
    {
        var value = text.Trim().TrimStart('#');
        return value.Length is 3 or 6 && value.All(char.IsAsciiHexDigit);
    }

    private bool Compare(ExecutableStep step)
    {
        var left = ReadValue(step, "variable");
        var right = ReadValue(step, "compareValue");
        var same = left.NumericEquals(right)
            || Compare(step, left, right, TextCompare.Exactly);

        return ReadText(step, "compareOperator") switch
        {
            "equals" => same,
            "notEquals" => !same,
            "greaterThan" => left.AsNumber() > right.AsNumber(),
            "greaterOrEqual" => left.AsNumber() >= right.AsNumber(),
            "lessThan" => left.AsNumber() < right.AsNumber(),
            "lessOrEqual" => left.AsNumber() <= right.AsNumber(),
            // The text comparisons are the ones the whole program shares, so a pattern in a condition
            // and the same pattern in a text search cannot come to mean two different things.
            TextCompare.Contains => Compare(step, left, right, TextCompare.Contains),
            "notContains" => !Compare(step, left, right, TextCompare.Contains),
            "exists" => !left.IsNull && left.AsText().Length > 0,
            TextCompare.Regex => Compare(step, left, right, TextCompare.Regex),
            _ => false,
        };
    }

    /// <summary>
    /// Holds two values together the way <see cref="TextCompare"/> says, turning a pattern the machine
    /// cannot read into a failed step rather than a stack trace — the same answer the steps that look
    /// for text give.
    /// </summary>
    private bool Compare(ExecutableStep step, Value left, Value right, string way)
    {
        try
        {
            return TextCompare.Matches(left.AsText(), right.AsText(), way,
                OptionsOf(step, way));
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, right.AsText());
        }
    }

    private bool Group(ExecutableStep step, int depth, CancellationToken token)
    {
        var children = step.Children("conditions");
        if (children.Count == 0)
        {
            return true;
        }

        var results = new List<bool>(children.Count);
        foreach (var child in children)
        {
            results.Add(Check(child, depth, token));
        }

        return ReadText(step, "op") switch
        {
            "or" => results.Any(result => result),
            "none" => results.All(result => !result),
            _ => results.All(result => result),
        };
    }

    private void SetVariable(ExecutableStep step, int depth)
    {
        var name = ReadText(step, "name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var target = Target(step);
        var value = ReadValue(step, "value");
        Variables.Set(name, value, target);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, value.AsText());
    }

    /// <summary>
    /// Puts the macro's own question to whoever is at the machine and keeps the answer. The
    /// question is written by the person who wrote the macro, so it travels as plain text rather
    /// than as a message key: the engine holds no user-facing words of its own.
    /// </summary>
    private async Task<Signal> AskPerson(ExecutableStep step, int depth, CancellationToken token)
    {
        var message = ReadValue(step, "message").AsText();
        var detail = ReadValue(step, "detail").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var question = detail.Length == 0 ? message : message + "\n\n" + detail;
        var answer = await _host.AskUser(new AskQuestion(AskKind.Confirm, question, string.Empty), token);

        Store(step, "confirmed", Value.FromBool(answer.Confirmed), depth);
        Log(LogLevel.Info, depth, step.Type,
            answer.Confirmed ? LogKeys.AskedYes : LogKeys.AskedNo, question);
        return Signal.Normal;
    }

    /// <summary>
    /// Asks for a line of writing and keeps it. A question nobody answers hands over an empty line
    /// rather than failing: a macro that needs an answer says so by looking at what it got, and one
    /// that runs under a trigger never had anybody to ask.
    /// </summary>
    private async Task<Signal> AskForText(ExecutableStep step, int depth, CancellationToken token)
    {
        var message = ReadValue(step, "message").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var suggestion = ReadValue(step, "default").AsText();
        var answer = await _host.AskUser(new AskQuestion(AskKind.Text, message, suggestion), token);

        Store(step, "text", Value.FromText(answer.Text), depth);
        Log(LogLevel.Info, depth, step.Type, LogKeys.AskedText, answer.Text);
        return Signal.Normal;
    }

    /// <summary>
    /// Works out one expression and stores the answer. It reads the value the same way storing a
    /// value does, so the two steps differ only in what they are called, not in what they accept.
    /// </summary>
    private void Calculate(ExecutableStep step, int depth)
    {
        var name = ReadText(step, "name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var value = ReadValue(step, "value");
        var target = Target(step);
        Variables.Set(name, value, target);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, value.AsText());
    }

    /// <summary>
    /// Adds to a shared number as one step. Reading the old value and writing the sum from two
    /// ordinary steps can lose one of two runs' additions; here both happen under the table's lock,
    /// so every addition that was asked for is in the answer.
    /// </summary>
    private void IncrementGlobal(ExecutableStep step, int depth)
    {
        var name = ReadText(step, "name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var amount = ReadValue(step, "amount").AsNumber();
        double sum;
        try
        {
            if (!Variables.Global.TryAddNumber(name, amount, out sum))
            {
                throw new StepFailure(RunKeys.GlobalMustExist, name);
            }
        }
        catch (ExpressionException failure)
        {
            throw new StepFailure(RunKeys.NotANumber, $"{name}: {failure.Detail}");
        }

        // The count after this step, so a macro can compare it or write it down without reading
        // the same global again — a second read would also be a second look at a value another run
        // may have changed in between.
        Give(step, "value", Value.FromNumber(sum));
        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, Value.FormatNumber(sum));
    }

    private void CreateList(ExecutableStep step, int depth)
    {
        var name = ReadText(step, "name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        if (!ListText.TryParse(ReadText(step, "items"), Variables, out var items, out var error))
        {
            throw new StepFailure(RunKeys.BadList, error?.Detail ?? ReadText(step, "items"));
        }

        var target = Target(step);
        var list = Value.FromList(items);
        Variables.Set(name, list, target);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, list.AsText());
    }

    /// <summary>
    /// Changes one item of a list — or empties it — as a single step under the bag's lock: two runs
    /// appending to the same global list both see the list as it is when their turn comes, so
    /// neither writes a list that is missing the other's item. A local list is changed the same way
    /// under its own bag, which no other run touches.
    /// </summary>
    private void ChangeList(ExecutableStep step, int depth)
    {
        var name = ReadText(step, "name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var bag = Variables.Local.Contains(name) ? Variables.Local : Variables.Global;
        if (!bag.TryUpdate(name, current => Changed(step, current), out var list))
        {
            throw new StepFailure(RunKeys.NotAList, name);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, list.AsText());
    }

    /// <summary>
    /// The list one change step makes out of the list that is there now. The step's own value is
    /// read here, inside the lock, so a step that names the list it changes reads it as it stands
    /// and a value that cannot be read leaves the list as it was.
    /// </summary>
    private Value Changed(ExecutableStep step, Value current)
    {
        if (!current.IsList)
        {
            throw new StepFailure(RunKeys.NotAList, ReadText(step, "name").Trim());
        }

        var items = new List<Value>(current.Items);
        switch (step.Type)
        {
            case "control.listAdd":
                items.Add(ReadValue(step, "value"));
                break;
            case "control.listInsert":
                items.Insert(Math.Clamp(Number(step, "listIndex"), 0, items.Count),
                    ReadValue(step, "value"));
                break;
            case "control.listSet":
                items[Index(step, items.Count)] = ReadValue(step, "value");
                break;
            case "control.listRemoveAt":
                items.RemoveAt(Index(step, items.Count));
                break;
            default:
                items.Clear();
                break;
        }

        return Value.FromList(items);
    }

    private int Index(ExecutableStep step, int count)
    {
        var index = Number(step, "listIndex");
        if (index < 0)
        {
            index += count;
        }

        if (index < 0 || index >= count)
        {
            throw new StepFailure(RunKeys.IndexOutOfRange, ReadText(step, "listIndex"));
        }

        return index;
    }

    private VariableTarget Target(ExecutableStep step)
    {
        if (!string.Equals(ReadText(step, "scope"), "global", StringComparison.OrdinalIgnoreCase))
        {
            return VariableTarget.Local;
        }

        // Only the Variable Center creates shared variables, so a macro may write to one
        // that already exists but never invent a new one.
        var name = ReadText(step, "name").Trim();
        if (!Variables.Global.Contains(name))
        {
            throw new StepFailure(RunKeys.GlobalMustExist, name);
        }

        return VariableTarget.Global;
    }

    /// <summary>The same, for something that turns up rather than becomes true.</summary>
    private static async Task<T> WaitForValueAsync<T>(Func<T?> probe, int interval,
        CancellationToken token)
        where T : class
    {
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await Pause(interval, token);
        }
    }

    private void Log(LogLevel level, int depth, string step, string key, params object[] arguments)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, depth, step, key, arguments);

        // Kept beside being handed to the host: a run that ends on a failure writes all of this to
        // a file afterwards, and a run nobody is watching has no host that keeps it.
        _log.Add(entry);
        _host.Log(entry);
    }

    /// <summary>Why a step could not run. The key is translated by the interface.</summary>
    private sealed class StepFailure : Exception
    {
        public StepFailure(string key, string detail = "")
            : base(key)
        {
            Key = key;
            Detail = detail;
        }

        public string Key { get; }

        public string Detail { get; }
    }
}
