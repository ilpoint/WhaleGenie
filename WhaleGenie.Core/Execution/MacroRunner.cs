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
public sealed class MacroRunner
{
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
    /// How many loops the run is inside right now. A break or a continue only means something
    /// while this is above zero; one that reaches the top of a macro has nothing to act on.
    /// </summary>
    private int _loops;

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
    /// Where the pictures of failed runs are written. Relative by default, which puts them beside
    /// the macro's own data; the application hands in an absolute folder so they land in the
    /// program's folder instead of inside a project somebody may well be sharing.
    /// </summary>
    public string FailureFolder { get; init; } = "logs";

    /// <summary>
    /// True when a run that stops on a failure should leave a picture of the screen behind, so the
    /// user can see what was on it at the time. Nothing is written while a macro is running well.
    /// </summary>
    public bool FailureScreenshot { get; init; }

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
    public bool ExclusiveInput { get; init; }

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
            return new RunResult(RunStatus.Completed, "Run.Finished", string.Empty, 1);
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
        _failure = null;
        _images.Clear();
        _frames.Clear();
        _looked.Clear();
        PrepareOutcomes(steps);
        Log(LogLevel.Info, 0, string.Empty, "Run.Start", steps.Count);
        if (Math.Abs(DelayScale - 1) > 0.001)
        {
            Log(LogLevel.Info, 0, string.Empty, "Run.Speed", DelayScale);
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
                Signal.Stop => new RunResult(RunStatus.Stopped, "Run.Stopped", string.Empty, _executed),
                Signal.Failed => new RunResult(RunStatus.Failed, _failure?.Key ?? "Run.Failed",
                    _failure?.Detail ?? string.Empty, _executed),
                _ => new RunResult(RunStatus.Completed, "Run.Finished", string.Empty, _executed),
            };

            if (outcome.Status is RunStatus.Failed)
            {
                LeaveFailurePicture();
            }

            Log(LogLevel.Info, 0, string.Empty, SummaryKey(outcome.Status), _executed);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warn, 0, string.Empty, "Run.Stopped", _executed);
            return new RunResult(RunStatus.Stopped, "Run.Stopped", string.Empty, _executed);
        }
        finally
        {
            // Whatever the run decided, the machine is handed back the way it was found.
            LetGoOfHeldInput();
        }
    }

    /// <summary>The line written once a run is over.</summary>
    private static string SummaryKey(RunStatus status) => status switch
    {
        RunStatus.Stopped => "Run.Stopped",
        RunStatus.Failed => "Run.Aborted",
        _ => "Run.Finished",
    };

    /// <summary>The message a break or a continue gets when it turns out to have no loop.</summary>
    private static string LoopControlKey(Signal signal)
        => signal is Signal.Break ? "Run.BreakOutsideLoop" : "Run.ContinueOutsideLoop";

    /// <summary>
    /// Leaves a picture of the screen behind when a run stops on a failure.
    /// </summary>
    /// <remarks>
    /// The primary screen is what is taken: it is where a macro that fails is nearly always
    /// working, and covering every monitor costs more to take and more to look at. The picture is
    /// written whether or not anything went wrong while taking it — a run that has already failed
    /// must not be brought down by the act of recording it, so a failure here is only logged.
    /// </remarks>
    private void LeaveFailurePicture()
    {
        if (!FailureScreenshot)
        {
            return;
        }

        try
        {
            var desk = _devices.Screen.Desktop.Bounds;
            var frame = _devices.Screen.Capture(desk.Left, desk.Top, desk.Width, desk.Height);
            if (frame.IsEmpty)
            {
                return;
            }

            var name = "failure-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var relative = Path.Combine(FailureFolder, name + ".png");
            for (var counter = 1; _devices.Files.Exists(_devices.Files.Resolve(relative)); counter++)
            {
                relative = Path.Combine(FailureFolder,
                    string.Create(CultureInfo.InvariantCulture, $"{name}-{counter}.png"));
            }

            _devices.Files.WriteBytes(relative, PngWriter.Encode(frame));
            Log(LogLevel.Info, 0, string.Empty, "Run.FailurePicture", _devices.Files.Resolve(relative));
        }
        catch (Exception failure)
        {
            Log(LogLevel.Warn, 0, string.Empty, "Run.FailurePictureFailed", failure.Message);
        }
    }

    /// <summary>What a step tells the runner to do next.</summary>
    private enum Signal
    {
        Normal,
        Break,
        Continue,
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
        => step.Type.StartsWith("condition.", StringComparison.Ordinal);

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
    /// Runs another macro's steps as though they had been written here, on values of its own:
    /// what the caller passes is what it can read, and what it leaves behind comes back through
    /// the names the call asked for.
    /// </summary>
    private async Task<Signal> RunOtherMacro(ExecutableStep step, int depth, CancellationToken token)
    {
        var name = Read(step.Text("macro")).AsText().Trim();
        var steps = name.Length == 0 ? null : _macros.Steps(name);
        if (steps is null)
        {
            throw new StepFailure("Run.MacroNotFound", name);
        }

        if (_callDepth >= CallDepthLimit)
        {
            throw new StepFailure("Run.MacroTooDeep", name);
        }

        // What the call may ask to have back is settled before anything runs: a name the called
        // macro never offered is a mistake in the call, and saying so now keeps it from turning
        // into a half-finished call or, worse, hiding the failure that was already on its way.
        var signature = _macros.Signature(name) ?? MacroSignature.Empty;
        var wanted = WantedResults(step, signature);

        // Read the arguments while the caller's values are still the ones in force, then hand the
        // called macro a table of its own holding just those. A macro's own values are its own —
        // that is why one macro cannot read another's — so a call says what goes in and what
        // comes back rather than leaning on whatever happened to be left lying around.
        var called = new VariableBag();
        foreach (var argument in signature.Arguments)
        {
            called.Set(argument.Name, ArgumentValue(step, argument));
        }

        var caller = Variables.SwapLocal(called);
        _callDepth++;
        PrepareOutcomes(steps);

        // The called macro starts outside every loop, whatever the caller is inside. A break in
        // there belongs to a loop of its own or to nothing; it must never reach out and cut a
        // loop in the caller short.
        var outerLoops = _loops;
        _loops = 0;
        try
        {
            Log(LogLevel.Info, depth, step.Type, "Run.CalledMacro", name, steps.Count);
            var signal = await RunSteps(steps, depth + 1, token);

            // A break or a continue that got this far had no loop of its own to act on. That is
            // a mistake in the called macro, and the caller gets told rather than being handed
            // back what looks like a finished call. Only "start the next round" can arrive here
            // as a continue: a break or a continue written in the macro is caught where it is.
            if (signal is Signal.Break or Signal.Continue)
            {
                throw new StepFailure(LoopControlKey(signal), name);
            }

            return signal;
        }
        finally
        {
            _loops = outerLoops;
            _callDepth--;
            Take(wanted, Variables.SwapLocal(caller));
        }
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
            throw new StepFailure("Run.MissingArgument", argument.Name);
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
            var result = row.TryGetValue("key", out var key) ? key.Trim() : string.Empty;
            var name = row.TryGetValue("name", out var written) ? written.Trim() : string.Empty;
            if (result.Length == 0 || name.Length == 0)
            {
                continue;
            }

            if (!signature.Offers(result))
            {
                throw new StepFailure("Run.UndeclaredResult", result);
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
            Log(LogLevel.Debug, depth, step.Type, "Run.Skipped");
            Report(step, OutcomeSkipped, 0, null, []);
            return Signal.Normal;
        }

        // The host may leave this one step out, which is what "skip this step once" is: the step
        // did nothing, so it is answered the way a switched-off step is and the run goes on.
        if (!await _host.BeforeStep(step, depth, token))
        {
            Log(LogLevel.Debug, depth, step.Type, "Run.Skipped");
            Report(step, OutcomeSkipped, 0, null, []);
            return Signal.Normal;
        }

        await Pause(Pace(step.Meta.DelayBeforeMs), token);

        // What the step works out while it runs is kept for whoever is watching it, and nothing is
        // kept when nobody is: a macro a trigger started is not made to turn every result into text
        // so that a window which is not there can ignore it.
        var outerWorked = _worked;
        _worked = _looks is null ? null : [];
        var startedStep = Stopwatch.GetTimestamp();
        try
        {
            return await RunAttempts(step, depth, token, startedStep);
        }
        finally
        {
            _worked = outerWorked;
        }
    }

    /// <summary>
    /// One step's attempts at its work, from the first try to whichever ending it comes to. The
    /// clock covers all of them, because "how long did this step take" is answered by the step
    /// rather than by the last try of it.
    /// </summary>
    private async Task<Signal> RunAttempts(ExecutableStep step, int depth, CancellationToken token,
        long startedStep)
    {
        for (var attempt = 0; ; attempt++)
        {
            // A step that is allowed so long gets a clock of its own. The limit is what stops it:
            // a wait that would run past it gives up where it stands rather than carrying on and
            // being told off afterwards, which is what "this step may take at most so long" means
            // to whoever wrote it. An outer stop is a different thing and stays one.
            using var limit = step.Meta.TimeoutMs > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(token)
                : null;
            limit?.CancelAfter(step.Meta.TimeoutMs);

            try
            {
                var started = Stopwatch.GetTimestamp();
                Signal signal;
                try
                {
                    signal = await ExecuteChecked(step, depth, limit?.Token ?? token);
                }
                catch (OperationCanceledException)
                    when (limit is { IsCancellationRequested: true } && !token.IsCancellationRequested)
                {
                    throw WaitedTooLong(step);
                }

                // The clock catches what the token cannot: a device call that blocks until it is
                // finished never looks at a cancellation, so it is still told off for going over.
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (step.Meta.TimeoutMs > 0 && elapsed > step.Meta.TimeoutMs)
                {
                    // This is the step having run past its clock rather than a wait that was cut
                    // short, and the step may well have got what it was after on the way: the
                    // honest thing to report is the length, not a picture that was in fact found.
                    throw new StepFailure("Run.Timeout",
                        step.Meta.TimeoutMs.ToString(CultureInfo.InvariantCulture));
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
                    if (attempt < step.Meta.RetryCount)
                    {
                        Log(LogLevel.Warn, depth, step.Type, "Run.Retry", attempt + 1);
                        await Pause(RetryPause(step, attempt + 1), token);
                        continue;
                    }

                    var decided = await Decide(step, token);
                    if (decided is Decision.Retry)
                    {
                        await Pause(RetryPause(step, attempt + 1), token);
                        continue;
                    }

                    return StepFailed(step, decided, Elapsed(startedStep));
                }

                await Pause(Pace(step.Meta.DelayAfterMs), token);
                WriteOutcome(step, OutcomeOk);
                Report(step, OutcomeOk, Elapsed(startedStep), null, Worked());
                return signal;
            }
            catch (StepFailure) when (attempt < step.Meta.RetryCount)
            {
                Log(LogLevel.Warn, depth, step.Type, "Run.Retry", attempt + 1);
                await Pause(RetryPause(step, attempt + 1), token);
            }
            catch (StepFailure failure)
            {
                Log(LogLevel.Error, depth, step.Type, failure.Key, failure.Detail);
                _failure = failure;

                var decision = await Decide(step, token);
                if (decision is not Decision.Retry)
                {
                    return StepFailed(step, decision, Elapsed(startedStep));
                }

                await Pause(RetryPause(step, attempt + 1), token);
            }
        }
    }

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
                "vision.waitColor" => new StepFailure("Run.WaitColorTimeout",
                    PixelColor.Parse(Read(step.Text("color")).AsText()).ToHex()),
                "vision.waitImage" or "vision.clickImage" => new StepFailure("Run.ImageNotFound",
                    Written(step.Rows("image"))),
                "vision.waitStable" => new StepFailure("Run.ScreenNotStable", patience),
                "ocr.clickText" => new StepFailure("Run.TextNotFound",
                    Read(step.Text("text")).AsText()),
                "control.waitUntil" => new StepFailure("Run.WaitTimeout", WaitingFor(step, patience)),
                _ => new StepFailure("Run.Timeout", patience),
            };
        }
        catch (ExpressionException)
        {
            // A field that cannot be read at all is the business of another failure; the only
            // thing this one has to say is that the step ran out of time.
            return new StepFailure("Run.Timeout", patience);
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
    private Signal StepFailed(ExecutableStep step, Decision decided, double elapsedMs)
    {
        WriteOutcome(step, OutcomeFailed, _failure);
        Report(step, OutcomeFailed, elapsedMs, _failure, Worked());
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
        IReadOnlyList<StepResult> results)
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
            StepErrorAction.AskUser => await Ask(step, _failure ?? new StepFailure("Run.Failed"), token),
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
            throw new StepFailure("Run.BadExpression", failure.Detail);
        }
        catch (DeviceUnavailableException failure)
        {
            throw new StepFailure("Run.NoDevice", failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            throw new StepFailure(failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not StepFailure)
        {
            // Whatever a device driver throws, the run reports it as a failed step rather
            // than letting it escape and take the whole run down.
            throw new StepFailure("Run.DeviceFailed", failure.Message);
        }
    }

    private async Task<Signal> Execute(ExecutableStep step, int depth, CancellationToken token)
    {
        // Whatever the step before this one read is not what this one read. A step that looks at a
        // picture it was handed rather than taking one of its own says nothing about a window,
        // which is truer than repeating what the step before it was looking at.
        _takenWindow = string.Empty;
        _takenMethod = null;

        // A step that sends keys or clicks goes through the input door when this run asked to share
        // it. What is kept apart is the input, so the door is taken and given up around one step
        // rather than held for the whole macro.
        if (!ExclusiveInput || !SendsInput(step.Type))
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
        => type.StartsWith("input.", StringComparison.Ordinal)
            || type.StartsWith("gamepad.", StringComparison.Ordinal);

    private async Task<Signal> Dispatch(ExecutableStep step, int depth, CancellationToken token)
    {
        switch (step.Type)
        {
            case "control.sequence":
                return await RunSteps(step.Children("steps"), depth + 1, token);

            case "control.runMacro":
                return await RunOtherMacro(step, depth, token);

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

            case "control.stop":
                Log(LogLevel.Warn, depth, step.Type, "Run.StopRequested",
                    Read(step.Text("reason")).AsText());
                return Signal.Stop;

            case "control.fail":
                // The macro says this step did not work. It travels the same way a device failure
                // does, so whatever the step's failure rule says — retry, carry on, ask — holds for
                // a step the macro itself called failed.
                throw new StepFailure("Run.StepFailedByMacro", Read(step.Text("reason")).AsText());

            case "control.note":
                // A note is for whoever reads the macro. There is nothing to do and nothing to say.
                return Signal.Normal;

            case "control.setVariable":
                SetVariable(step, depth);
                return Signal.Normal;

            case "control.calculate":
                Calculate(step, depth);
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
                Log(Level(step.Text("level")), depth, step.Type, "Run.Message", Read(step.Text("message")).AsText());
                return Signal.Normal;

            case "control.ask":
                return await AskPerson(step, depth, token);

            case "control.input":
                return await AskForText(step, depth, token);

            // ------------------------------------------------------------------- file
            case "file.readText":
                ReadTextFile(step, depth);
                return Signal.Normal;

            case "file.writeText":
                WriteTextFile(step, depth);
                return Signal.Normal;

            case "file.appendLog":
                AppendLog(step, depth);
                return Signal.Normal;

            case "file.zip":
                ZipFolder(step, depth);
                return Signal.Normal;

            case "file.exists":
                FileExists(step, depth);
                return Signal.Normal;

            case "file.delete":
                DeleteFile(step, depth);
                return Signal.Normal;

            case "file.copy":
                CopyFile(step, depth);
                return Signal.Normal;

            case "file.move":
                MoveFile(step, depth);
                return Signal.Normal;

            case "file.createFolder":
                CreateFolder(step, depth);
                return Signal.Normal;

            case "file.deleteFolder":
                DeleteFolder(step, depth);
                return Signal.Normal;

            case "file.path":
                PathPart(step, depth);
                return Signal.Normal;

            case "file.unzip":
                UnzipFile(step, depth);
                return Signal.Normal;

            case "file.listFiles":
                ListFiles(step, depth);
                return Signal.Normal;

            case "file.readJson":
                ReadJson(step, depth);
                return Signal.Normal;

            case "file.writeJson":
                WriteJson(step, depth);
                return Signal.Normal;

            case "file.readCsv":
                ReadCsv(step, depth);
                return Signal.Normal;

            case "file.writeCsv":
                WriteCsv(step, depth);
                return Signal.Normal;

            case "file.saveVariables":
                SaveVariables(step, depth);
                return Signal.Normal;

            case "file.loadVariables":
                LoadVariables(step, depth);
                return Signal.Normal;

            // ----------------------------------------------------------------- data
            case "data.base64Encode":
                EncodeBase64(step, depth);
                return Signal.Normal;

            case "data.base64Decode":
                DecodeBase64(step, depth);
                return Signal.Normal;

            case "data.hash":
                HashText(step, depth);
                return Signal.Normal;

            // -------------------------------------------------------------- clipboard
            case "clipboard.writeText":
                ClipboardWrite(step, depth);
                return Signal.Normal;

            case "clipboard.readText":
                ClipboardRead(step, depth);
                return Signal.Normal;

            case "clipboard.readImage":
                ClipboardImageRead(step, depth);
                return Signal.Normal;

            case "clipboard.writeImage":
                ClipboardImageWrite(step, depth);
                return Signal.Normal;

            case "clipboard.readFiles":
                ClipboardFilesRead(step, depth);
                return Signal.Normal;

            case "clipboard.writeFiles":
                ClipboardFilesWrite(step, depth);
                return Signal.Normal;

            case "clipboard.clear":
                _devices.Clipboard.Clear();
                Log(LogLevel.Info, depth, step.Type, "Run.ClearedClipboard");
                return Signal.Normal;

            case "clipboard.waitChange":
                await ClipboardWait(step, depth, token);
                return Signal.Normal;

            case "clipboard.copy":
                await ClipboardCopy(step, depth, token);
                return Signal.Normal;

            case "clipboard.paste":
                await ClipboardPaste(step, depth, token);
                return Signal.Normal;

            // ---------------------------------------------------------------- process
            case "process.start":
                StartProcess(step, depth);
                return Signal.Normal;

            case "process.waitFor":
                await WaitForProgram(step, depth, token);
                return Signal.Normal;

            case "process.waitExit":
                await WaitForExit(step, depth, token);
                return Signal.Normal;

            case "process.exists":
                ProgramRunning(step, depth);
                return Signal.Normal;

            case "process.list":
                ListPrograms(step, depth);
                return Signal.Normal;

            case "process.info":
                ProgramDetails(step, depth);
                return Signal.Normal;

            case "process.kill":
                StopProgram(step, depth);
                return Signal.Normal;

            case "command.run":
                RunCommand(step, depth);
                return Signal.Normal;

            // ----------------------------------------------------------------- system
            case "system.info":
                SystemInfo(step, depth);
                return Signal.Normal;

            case "screen.list":
                ListDisplays(step, depth);
                return Signal.Normal;

            case "system.environment":
                EnvironmentVariable(step, depth);
                return Signal.Normal;

            case "system.power":
                Power(step, depth);
                return Signal.Normal;

            case "system.volume":
                Volume(step, depth);
                return Signal.Normal;

            case "system.sound":
                PlaySound(step, depth);
                return Signal.Normal;

            case "system.notify":
                Notify(step, depth);
                return Signal.Normal;

            case "system.ime":
                InputMethod(step, depth);
                return Signal.Normal;

            case "system.brightness":
                Brightness(step, depth);
                return Signal.Normal;

            // ----------------------------------------------------------------- window
            case "window.exists":
                WindowExists(step, depth);
                return Signal.Normal;

            case "window.waitFor":
                await WaitForWindow(step, depth, token);
                return Signal.Normal;

            case "window.activate":
                Act(step, depth, "Run.ActivatedWindow", window => _devices.Windows.Activate(window.Handle));
                return Signal.Normal;

            case "window.minimize":
                Act(step, depth, "Run.MinimizedWindow", window => _devices.Windows.Minimize(window.Handle));
                return Signal.Normal;

            case "window.maximize":
                Act(step, depth, "Run.MaximizedWindow", window => _devices.Windows.Maximize(window.Handle));
                return Signal.Normal;

            case "window.restore":
                Act(step, depth, "Run.RestoredWindow", window => _devices.Windows.Restore(window.Handle));
                return Signal.Normal;

            case "window.move":
                {
                    var x = Number(step, "x");
                    var y = Number(step, "y");
                    var width = Math.Max(1, Number(step, "width"));
                    var height = Math.Max(1, Number(step, "height"));
                    Act(step, depth, "Run.MovedWindow",
                        window => _devices.Windows.Move(window.Handle, x, y, width, height));
                    return Signal.Normal;
                }

            case "window.close":
                Act(step, depth, "Run.ClosedWindow", window => _devices.Windows.Close(window.Handle));
                return Signal.Normal;

            case "window.list":
                ListWindows(step, depth);
                return Signal.Normal;

            case "window.info":
                WindowBox(step, depth);
                return Signal.Normal;

            // ------------------------------------------------------------------ input
            case "input.keyPress":
                await PressKey(step, token);
                return Signal.Normal;

            case "input.keyDown":
                HoldKey(step);
                return Signal.Normal;

            case "input.keyUp":
                LetGoKey(step);
                return Signal.Normal;

            case "input.hotkey":
                await SendHotkey(step, token);
                return Signal.Normal;

            case "input.typeText":
                Input(step).TypeText(Read(step.Text("text")).AsText(), Pace(Number(step, "intervalMs")));
                return Signal.Normal;

            case "input.mouseMove":
                MovePointer(step, Place(step, Number(step, "x"), Number(step, "y")));
                return Signal.Normal;

            case "input.mouseMoveRelative":
                MovePointerBy(step);
                return Signal.Normal;

            case "input.mouseClick":
                await ClickMouse(step, token);
                return Signal.Normal;

            case "input.mouseDoubleClick":
                await DoubleClickMouse(step, token);
                return Signal.Normal;

            case "input.keySequence":
                await SendKeySequence(step, depth, token);
                return Signal.Normal;

            case "input.mouseDown":
                {
                    HoldButton(step);
                    return Signal.Normal;
                }

            case "input.mouseUp":
                {
                    LetGoButton(step);
                    return Signal.Normal;
                }

            case "input.mouseScroll":
                await ScrollMouse(step, token);
                return Signal.Normal;

            case "input.mouseDrag":
                await DragPointer(step, token);
                return Signal.Normal;

            // ---------------------------------------------------------------- gamepad
            case "gamepad.connect":
                _devices.Gamepad.Connect();
                _gamepadUsed = true;
                return Signal.Normal;

            case "gamepad.button":
                await PressGamepadButton(step, token);
                return Signal.Normal;

            case "gamepad.stick":
                _devices.Gamepad.Stick(step.Text("stick"), Number(step, "x"), Number(step, "y"));
                _gamepadUsed = true;
                return Signal.Normal;

            case "gamepad.trigger":
                _devices.Gamepad.Trigger(step.Text("trigger"), Number(step, "amount"));
                _gamepadUsed = true;
                return Signal.Normal;

            case "gamepad.release":
                _devices.Gamepad.ReleaseAll();
                _gamepadUsed = true;
                return Signal.Normal;

            // ----------------------------------------------------------------- vision
            case "vision.capture":
                await Gate(step, token);
                Watching(() => Capture(step, depth));
                return Signal.Normal;

            case "vision.captureWindow":
                await Gate(step, token);
                Watching(() => CaptureWindow(step, depth));
                return Signal.Normal;

            case "vision.saveImage":
                SavePicture(step, depth);
                return Signal.Normal;

            case "vision.getPixel":
                await Gate(step, token);
                Watching(() => GetPixel(step, depth));
                return Signal.Normal;

            case "vision.waitColor":
                await Gate(step, token);
                await Watching(() => WaitColor(step, depth, token));
                return Signal.Normal;

            case "vision.findColor":
                await Gate(step, token);
                Watching(() => FindColor(step, depth));
                return Signal.Normal;

            case "vision.findColors":
                await Gate(step, token);
                Watching(() => FindColours(step, depth));
                return Signal.Normal;

            case "vision.waitStable":
                await Gate(step, token);
                await WaitStable(step, depth, token);
                return Signal.Normal;

            case "vision.findImage":
                await Gate(step, token);
                Watching(() => LookFor(step, depth));
                return Signal.Normal;

            case "vision.waitImage":
                await Gate(step, token);
                await Watching(() => WaitForImage(step, depth, token));
                return Signal.Normal;

            case "vision.clickImage":
                await Gate(step, token);
                await Watching(() => ClickImage(step, depth, token));
                return Signal.Normal;

            // -------------------------------------------------------------------- ocr
            case "ocr.recognize":
                await Gate(step, token);
                Watching(() => Recognize(step, depth));
                return Signal.Normal;

            case "ocr.findText":
                await Gate(step, token);
                Watching(() => FindText(step, depth));
                return Signal.Normal;

            case "ocr.clickText":
                await Gate(step, token);
                await Watching(() => ClickText(step, depth, token));
                return Signal.Normal;

            // -------------------------------------------------------------------- uia
            case "uia.exists":
                Exists(step, depth);
                return Signal.Normal;

            case "uia.find":
                FindElement(step, depth);
                return Signal.Normal;

            case "uia.waitElement":
                await WaitElement(step, depth, token);
                return Signal.Normal;

            case "uia.click":
                await ClickElement(step, depth, token);
                return Signal.Normal;

            case "uia.invoke":
                await InvokeElement(step, depth, token);
                return Signal.Normal;

            case "uia.setText":
                SetElementText(step, depth);
                return Signal.Normal;

            case "uia.read":
                ReadElement(step, depth);
                return Signal.Normal;

            case "uia.select":
                SelectItem(step, depth);
                return Signal.Normal;

            case "uia.check":
                SetElementChecked(step, depth);
                return Signal.Normal;

            case "uia.expand":
                SetElementExpanded(step, depth);
                return Signal.Normal;

            case "uia.scrollIntoView":
                ScrollElementIntoView(step, depth);
                return Signal.Normal;

            case "uia.readTable":
                ReadElementTable(step, depth);
                return Signal.Normal;

            case "uia.focusWindow":
                FocusWindow(step, depth);
                return Signal.Normal;

            // --------------------------------------------------------------- browser
            case "browser.open":
                BrowserOpen(step, depth);
                return Signal.Normal;

            case "browser.goTo":
                _devices.Browser.GoTo(Read(step.Text("url")).AsText());
                Log(LogLevel.Info, depth, step.Type, "Run.BrowserAddress", _devices.Browser.Url);
                return Signal.Normal;

            case "browser.click":
                _devices.Browser.Click(Read(step.Text("target")).AsText());
                Log(LogLevel.Info, depth, step.Type, "Run.BrowserClicked", step.Text("target"));
                return Signal.Normal;

            case "browser.fill":
                _devices.Browser.Fill(
                    Read(step.Text("target")).AsText(), Read(step.Text("text")).AsText());
                Log(LogLevel.Info, depth, step.Type, "Run.BrowserFilled", step.Text("target"));
                return Signal.Normal;

            case "browser.readText":
                BrowserReadText(step, depth);
                return Signal.Normal;

            case "browser.select":
                BrowserSelect(step, depth);
                return Signal.Normal;

            case "browser.check":
                _devices.Browser.Check(Read(step.Text("target")).AsText(),
                    Flag(step, "checked", true));
                Log(LogLevel.Info, depth, step.Type, "Run.BrowserChecked", step.Text("target"));
                return Signal.Normal;

            case "browser.hover":
                _devices.Browser.Hover(Read(step.Text("target")).AsText());
                Log(LogLevel.Info, depth, step.Type, "Run.BrowserHovered", step.Text("target"));
                return Signal.Normal;

            case "browser.press":
                BrowserPress(step, depth);
                return Signal.Normal;

            case "browser.waitFor":
                BrowserWaitFor(step, depth);
                return Signal.Normal;

            case "browser.upload":
                BrowserUpload(step, depth);
                return Signal.Normal;

            case "browser.switchTab":
                BrowserSwitchTab(step, depth);
                return Signal.Normal;

            case "browser.closeTab":
                _devices.Browser.CloseTab();
                Log(LogLevel.Info, depth, step.Type, "Run.ClosedTab");
                return Signal.Normal;

            case "browser.close":
                _devices.Browser.Close();
                Log(LogLevel.Info, depth, step.Type, "Run.ClosedBrowser");
                return Signal.Normal;

            // ------------------------------------------------------------------ script
            case "script.run":
                RunScript(step, depth);
                return Signal.Normal;

            default:
                throw new StepFailure("Run.Unsupported", step.Type);
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
                        ?? throw new StepFailure("Run.MissingCondition");
        var poll = Number(step, "pollMs");
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
        Log(LogLevel.Info, depth, step.Type, "Run.WaitedFor", waited, condition.Type);
        return Signal.Normal;
    }

    /// <summary>How long something has been going, in whole milliseconds.</summary>
    private static int Elapsed(long started)
        => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private async Task<Signal> RunRepeat(ExecutableStep step, int depth, CancellationToken token)
    {
        var times = Math.Max(0, Number(step, "times"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var round = 0; round < times; round++)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set("sys.loopIndex", Value.FromNumber(round));
            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                return Signal.Normal;
            }

            await Pause(interval, token);
        }

        return Signal.Normal;
    }

    private async Task<Signal> RunWhile(ExecutableStep step, int depth, CancellationToken token)
    {
        var limit = Math.Max(1, Number(step, "maxIterations"));
        var condition = step.Condition("condition");

        for (var round = 0; round < limit; round++)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set("sys.loopIndex", Value.FromNumber(round));
            if (condition is not null && !Check(condition, depth, token))
            {
                return Signal.Normal;
            }

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                return Signal.Normal;
            }
        }

        return Signal.Normal;
    }

    /// <summary>
    /// Runs one round of a loop's body. Every loop goes through here so a break or a continue
    /// written inside a body finds a loop above it; the depth is what tells the two apart from
    /// one written where no loop is in reach.
    /// </summary>
    private async Task<Signal> RunBody(ExecutableStep step, int depth, CancellationToken token)
    {
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
        => _loops > 0
            ? signal
            : throw new StepFailure(LoopControlKey(signal), step.Type);

    private async Task<Signal> RunForEach(ExecutableStep step, int depth, CancellationToken token)
    {
        if (!ListText.TryParse(step.Text("items"), Variables, out var items, out var error))
        {
            throw new StepFailure("Run.BadList", error?.Detail ?? step.Text("items"));
        }

        var itemName = step.Text("itemVariable").Trim();
        if (itemName.Length == 0)
        {
            itemName = "item";
        }

        var order = string.Equals(step.Text("reverse"), "true", StringComparison.OrdinalIgnoreCase)
            ? items.Reverse()
            : items;

        // The round number is only worth having when the step asked for it by name: a variable
        // nobody asked for is one more thing to puzzle over in the variable list.
        var indexName = step.Text("indexVariable").Trim();
        var index = 0;
        foreach (var item in order)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set("sys.loopIndex", Value.FromNumber(index));
            Variables.Set(itemName, item);
            if (indexName.Length > 0)
            {
                Variables.Set(indexName, Value.FromNumber(index));
            }

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                return Signal.Normal;
            }

            index++;
        }

        return Signal.Normal;
    }

    /// <summary>
    /// Counts from one number to another, running the body once per value. The counter is a variable
    /// the body can read, which is the difference between this and "repeat": repeating counts the
    /// rounds in the runner's head, while counting hands the number to the macro.
    /// </summary>
    private async Task<Signal> RunFor(ExecutableStep step, int depth, CancellationToken token)
    {
        var from = OptionalNumber(step, "from", 1);
        var to = OptionalNumber(step, "to", 5);
        var stride = OptionalNumber(step, "step", 1);
        if (stride == 0)
        {
            // A step is what the counter moves by, so a zero would never move it at all. Taking the
            // direction from where it starts and where it ends is what that can only have meant.
            stride = to >= from ? 1 : -1;
        }

        // The counter is a variable the step itself names, and a loop with no counter has nothing
        // to run its body against, so an empty field falls back to the name every for-loop uses.
        var counter = step.Text("variable").Trim();
        if (counter.Length == 0)
        {
            counter = "i";
        }
        var interval = Pace(Number(step, "intervalMs"));
        var round = 0;

        for (var value = from; stride > 0 ? value <= to : value >= to; value += stride)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set("sys.loopIndex", Value.FromNumber(round));
            Variables.Set(counter, Value.FromNumber(value));

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                return Signal.Normal;
            }

            round++;
            await Pause(interval, token);
        }

        return Signal.Normal;
    }

    /// <summary>
    /// Compares one value against the values of each case in turn and runs the first case that
    /// matches. The rest are left alone, and the otherwise steps take over when nothing matches —
    /// that is what makes a switch different from a row of separate ifs.
    /// </summary>
    private async Task<Signal> RunSwitch(ExecutableStep step, int depth, CancellationToken token)
    {
        var value = Read(step.Text("value"));
        var mode = step.Text("matchMode");

        foreach (var branch in step.Children("cases"))
        {
            if (branch.Type != "control.case")
            {
                continue;
            }

            foreach (var wanted in Labels(branch))
            {
                if (CaseMatches(value, wanted, mode))
                {
                    var signal = await RunSteps(branch.Children("body"), depth + 1, token);

                    // A branch is a block of the macro like any other, so it says how it went
                    // too. It is run from here rather than through the walk, which is why it has
                    // to be written down by hand.
                    WriteOutcome(branch, signal is Signal.Failed ? OutcomeFailed : OutcomeOk);
                    return signal;
                }
            }
        }

        return await RunSteps(step.Children("otherwise"), depth + 1, token);
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
    private static bool CaseMatches(Value value, string wanted, string mode)
    {
        switch (mode.Trim().ToLowerInvariant())
        {
            case "notequals":
                return !value.NumericEquals(Value.FromText(wanted));
            case "contains":
                return value.AsText().Contains(wanted, StringComparison.OrdinalIgnoreCase);
            case "startswith":
                return value.AsText().StartsWith(wanted, StringComparison.OrdinalIgnoreCase);
            case "endswith":
                return value.AsText().EndsWith(wanted, StringComparison.OrdinalIgnoreCase);
            case "greaterthan":
                return Order(value.AsText(), wanted) > 0;
            case "greaterorequal":
                return Order(value.AsText(), wanted) >= 0;
            case "lessthan":
                return Order(value.AsText(), wanted) < 0;
            case "lessorequal":
                return Order(value.AsText(), wanted) <= 0;
            case "regex":
                try
                {
                    return Regex.IsMatch(value.AsText(), wanted, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    // A pattern the machine cannot read is the macro's problem to see, not a
                    // stack trace to puzzle over, so it comes back as a failed step.
                    throw new StepFailure("Run.BadPattern", wanted);
                }
            default:
                // NumericEquals reads "1" against 1 as well as plain text, the same way an
                // equality condition does.
                return value.NumericEquals(Value.FromText(wanted));
        }
    }

    /// <summary>
    /// Puts two texts in order for a greater-than or less-than branch: as numbers when both read
    /// as numbers, and the way the expression language compares text otherwise, so "b" after "a"
    /// works and "10" still beats "9" rather than falling behind it.
    /// </summary>
    private static int Order(string value, string wanted)
        => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var left)
           && double.TryParse(wanted, NumberStyles.Any, CultureInfo.InvariantCulture, out var right)
            ? left.CompareTo(right)
            : string.Compare(value, wanted, StringComparison.OrdinalIgnoreCase);

    private async Task<Signal> RunIf(ExecutableStep step, int depth, CancellationToken token)
    {
        var condition = step.Condition("condition");
        var matched = condition is null || Check(condition, depth, token);
        return await RunSteps(step.Children(matched ? "then" : "else"), depth + 1, token);
    }

    /// <summary>
    /// Attempts a block, handles a failure in another block, and runs a third one either way.
    /// Catching the failure here is the whole point of the step, so it does not reach the
    /// failure rules and does not stop the macro.
    /// </summary>
    /// <remarks>
    /// A stopped run does not run its clean-up block. Every step in it would be racing the
    /// cancellation, so it would either do nothing or hold the stop up.
    /// </remarks>
    private async Task<Signal> RunTry(ExecutableStep step, int depth, CancellationToken token)
    {
        var signal = await RunSteps(step.Children("body"), depth + 1, token);

        if (signal is Signal.Failed)
        {
            Remember(step, _failure);
            Log(LogLevel.Warn, depth, step.Type, "Run.Caught");
            signal = await RunSteps(step.Children("catch"), depth + 1, token);
        }
        else
        {
            Remember(step, null);
        }

        var closing = step.Children("finally");
        if (closing.Count > 0)
        {
            Log(LogLevel.Debug, depth, step.Type, "Run.Finally");
            var closingSignal = await RunSteps(closing, depth + 1, token);

            // Stopping or failing while tidying up beats whatever the attempt did: a clean-up
            // that ends the run is the last word.
            if (closingSignal is Signal.Stop or Signal.Failed)
            {
                return closingSignal;
            }
        }

        return signal;
    }

    /// <summary>Records why the attempt failed, or empties the result when it did not.</summary>
    private void Remember(ExecutableStep step, StepFailure? failure)
    {
        var reason = failure switch
        {
            null => string.Empty,
            { Detail.Length: > 0 } => failure.Detail,
            _ => failure.Key,
        };

        Give(step, "error", Value.FromText(reason));
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
    private static bool LooksAtScreen(string conditionType) => conditionType is
        "condition.imageExists" or "condition.imageNotExists"
        or "condition.textExists" or "condition.textNotExists"
        or "condition.colorEquals" or "condition.colorsMatch" or "condition.colorsExist"
        or "condition.colorCoverage";

    private bool Check(ExecutableStep condition, int depth, CancellationToken token)
    {
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
                return System.Random.Shared.NextDouble() * 100 < Number(condition, "percent");
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

            case "condition.expression":
                return Read(condition.Text("expression")).AsBool();

            case "condition.stepResult":
                return StepResult(condition);

            case "condition.listContains":
                return ListHolds(condition);

            case "condition.pathExists":
                return _devices.Files.Exists(Read(condition.Text("path")).AsText());

            case "condition.processRunning":
                return _devices.Processes.Find(Read(condition.Text("name")).AsText()).Count > 0;

            case "condition.windowExists":
                return WindowThere(condition);

            case "condition.valueInRange":
                return InRange(condition);

            case "condition.dateCompare":
                return DatesCompared(condition);

            case "condition.colorEquals":
                {
                    var target = PixelColor.Parse(Read(condition.Text("color")).AsText());
                    return PixelAt(condition).Matches(target, Read(condition.Text("tolerance")).AsNumber());
                }

            case "condition.colorsMatch":
                return ColoursMatch(condition);

            case "condition.colorsExist":
                return PatternHits(condition).Count > 0;

            case "condition.colorCoverage":
                return ColourShare(condition);

            default:
                Log(LogLevel.Warn, depth, condition.Type, "Run.UnknownCondition", condition.Type);
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
        var id = condition.Text("step").Trim();
        if (id.Length == 0)
        {
            throw new StepFailure("Run.MissingStep");
        }

        var expected = condition.Text("expected").Trim();
        if (!Variables.TryGet(OutcomeName(id), out var outcome))
        {
            throw new StepFailure("Run.NoSuchStep", id);
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
        if (!ListText.TryParse(condition.Text("list"), Variables, out var items, out var error))
        {
            throw new StepFailure("Run.BadList", error?.Detail ?? condition.Text("list"));
        }

        var wanted = Read(condition.Text("value"));
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
        var value = Read(condition.Text("value")).AsText().Trim();
        var match = string.Equals(condition.Text("match").Trim(), "process",
            StringComparison.OrdinalIgnoreCase)
            ? WindowMatch.Process
            : WindowMatch.Title;

        return _devices.Windows.Find(value, match) is not null;
    }

    /// <summary>
    /// Whether a value sits between two others. Both ends count as inside, so a range of 1 to 10
    /// holds 1 and 10 — a range written with its ends included is what a number line looks like,
    /// and the hint says so where the macro is written.
    /// </summary>
    private bool InRange(ExecutableStep condition)
    {
        var value = Read(condition.Text("value")).AsNumber();
        var low = Read(condition.Text("min")).AsNumber();
        var high = Read(condition.Text("max")).AsNumber();
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
        var left = FunctionLibrary.Moment(DateText(condition.Text("left")));
        var right = FunctionLibrary.Moment(DateText(condition.Text("right")));
        return condition.Text("operator").Trim().ToLowerInvariant() switch
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
        var wanted = Read(condition.Text("text")).AsText();
        var mode = condition.Text("matchMode");
        return ReadSpans(condition).Any(span => Matches(span.Text, wanted, mode));
    }

    /// <summary>
    /// Answers whether the points a condition names show the colours it names. Each entry is one
    /// "x,y,#RRGGBB", separated by a semicolon or a line break, and the mode decides whether every
    /// point has to match or one is enough.
    /// </summary>
    private bool ColoursMatch(ExecutableStep condition)
    {
        var tolerance = Read(condition.Text("tolerance")).AsNumber();
        var every = !string.Equals(condition.Text("mode").Trim(), "any", StringComparison.OrdinalIgnoreCase);
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
        var text = Interpolate(step.Text("points")).Trim();
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
                throw new StepFailure("Run.BadPoints", entry);
            }

            points.Add(new ColourPoint(Place(step, x, y), PixelColor.Parse(parts[2])));
        }

        if (points.Count == 0)
        {
            throw new StepFailure("Run.BadPoints", text);
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
        var left = Read(step.Text("variable"));
        var right = Read(step.Text("value"));
        var same = left.NumericEquals(right)
            || string.Equals(left.AsText(), right.AsText(), StringComparison.OrdinalIgnoreCase);

        return step.Text("operator") switch
        {
            "equals" => same,
            "notEquals" => !same,
            "greaterThan" => left.AsNumber() > right.AsNumber(),
            "greaterOrEqual" => left.AsNumber() >= right.AsNumber(),
            "lessThan" => left.AsNumber() < right.AsNumber(),
            "lessOrEqual" => left.AsNumber() <= right.AsNumber(),
            "contains" => left.AsText().Contains(right.AsText(), StringComparison.OrdinalIgnoreCase),
            "notContains" => !left.AsText().Contains(right.AsText(), StringComparison.OrdinalIgnoreCase),
            "exists" => !left.IsNull && left.AsText().Length > 0,
            "regexMatch" => Regex.IsMatch(left.AsText(), right.AsText()),
            _ => false,
        };
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

        return step.Text("op") switch
        {
            "or" => results.Any(result => result),
            "none" => results.All(result => !result),
            _ => results.All(result => result),
        };
    }

    private void SetVariable(ExecutableStep step, int depth)
    {
        var name = step.Text("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure("Run.MissingVariable");
        }

        var target = Target(step);
        var value = Read(step.Text("value"));
        Variables.Set(name, value, target);
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, value.AsText());
    }

    /// <summary>
    /// Puts the macro's own question to whoever is at the machine and keeps the answer. The
    /// question is written by the person who wrote the macro, so it travels as plain text rather
    /// than as a message key: the engine holds no user-facing words of its own.
    /// </summary>
    private async Task<Signal> AskPerson(ExecutableStep step, int depth, CancellationToken token)
    {
        var message = Read(step.Text("message")).AsText();
        var detail = Read(step.Text("detail")).AsText();
        if (message.Length == 0)
        {
            throw new StepFailure("Run.MissingQuestion");
        }

        var question = detail.Length == 0 ? message : message + "\n\n" + detail;
        var answer = await _host.AskUser(new AskQuestion(AskKind.Confirm, question, string.Empty), token);

        Store(step, "confirmed", Value.FromBool(answer.Confirmed), depth);
        Log(LogLevel.Info, depth, step.Type,
            answer.Confirmed ? "Run.AskedYes" : "Run.AskedNo", question);
        return Signal.Normal;
    }

    /// <summary>
    /// Asks for a line of writing and keeps it. A question nobody answers hands over an empty line
    /// rather than failing: a macro that needs an answer says so by looking at what it got, and one
    /// that runs under a trigger never had anybody to ask.
    /// </summary>
    private async Task<Signal> AskForText(ExecutableStep step, int depth, CancellationToken token)
    {
        var message = Read(step.Text("message")).AsText();
        if (message.Length == 0)
        {
            throw new StepFailure("Run.MissingQuestion");
        }

        var suggestion = Read(step.Text("default")).AsText();
        var answer = await _host.AskUser(new AskQuestion(AskKind.Text, message, suggestion), token);

        Store(step, "text", Value.FromText(answer.Text), depth);
        Log(LogLevel.Info, depth, step.Type, "Run.AskedText", answer.Text);
        return Signal.Normal;
    }

    /// <summary>
    /// Works out one expression and stores the answer. It reads the value the same way storing a
    /// value does, so the two steps differ only in what they are called, not in what they accept.
    /// </summary>
    private void Calculate(ExecutableStep step, int depth)
    {
        var name = step.Text("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure("Run.MissingVariable");
        }

        var value = Read(step.Text("value"));
        var target = Target(step);
        Variables.Set(name, value, target);
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, value.AsText());
    }

    private void CreateList(ExecutableStep step, int depth)
    {
        var name = step.Text("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure("Run.MissingVariable");
        }

        if (!ListText.TryParse(step.Text("items"), Variables, out var items, out var error))
        {
            throw new StepFailure("Run.BadList", error?.Detail ?? step.Text("items"));
        }

        var target = Target(step);
        var list = Value.FromList(items);
        Variables.Set(name, list, target);
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, list.AsText());
    }

    private void ChangeList(ExecutableStep step, int depth)
    {
        var name = step.Text("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure("Run.MissingVariable");
        }

        if (!Variables.TryGet(name, out var current) || !current.IsList)
        {
            throw new StepFailure("Run.NotAList", name);
        }

        var items = new List<Value>(current.Items);
        switch (step.Type)
        {
            case "control.listAdd":
                items.Add(Read(step.Text("value")));
                break;
            case "control.listInsert":
                items.Insert(Math.Clamp(Number(step, "index"), 0, items.Count), Read(step.Text("value")));
                break;
            case "control.listSet":
                items[Index(step, items.Count)] = Read(step.Text("value"));
                break;
            case "control.listRemoveAt":
                items.RemoveAt(Index(step, items.Count));
                break;
            default:
                items.Clear();
                break;
        }

        var target = Variables.Local.Contains(name) ? VariableTarget.Local : VariableTarget.Global;
        var list = Value.FromList(items);
        Variables.Set(name, list, target);
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, list.AsText());
    }

    private int Index(ExecutableStep step, int count)
    {
        var index = Number(step, "index");
        if (index < 0)
        {
            index += count;
        }

        if (index < 0 || index >= count)
        {
            throw new StepFailure("Run.IndexOutOfRange", step.Text("index"));
        }

        return index;
    }

    private VariableTarget Target(ExecutableStep step)
    {
        if (!string.Equals(step.Text("scope"), "global", StringComparison.OrdinalIgnoreCase))
        {
            return VariableTarget.Local;
        }

        // Only the Variable Center creates shared variables, so a macro may write to one
        // that already exists but never invent a new one.
        var name = step.Text("name").Trim();
        if (!Variables.Global.Contains(name))
        {
            throw new StepFailure("Run.GlobalMustExist", name);
        }

        return VariableTarget.Global;
    }

    // ------------------------------------------------------------------- devices

    /// <summary>
    /// The input device that delivers this step's input, chosen by the step's own settings: in
    /// front by default, posted at one window when the step names one, or through a driver.
    /// </summary>
    private IInputDevice Input(ExecutableStep step) => _devices.Inputs.For(Route(step));

    /// <summary>Presses a key and remembers it, so the run can let it go if the macro never does.</summary>
    private void HoldKey(ExecutableStep step)
    {
        var device = Input(step);
        var key = step.Text("key");
        device.KeyDown(key);
        if (!_heldKeys.Any(held => Same(held.Device, device) && Same(held.Name, key)))
        {
            _heldKeys.Add((device, key));
        }
    }

    /// <summary>Releases a key the macro asked to release, and stops counting it as held.</summary>
    private void LetGoKey(ExecutableStep step)
    {
        var device = Input(step);
        var key = step.Text("key");
        device.KeyUp(key);
        _heldKeys.RemoveAll(held => Same(held.Device, device) && Same(held.Name, key));
    }

    /// <summary>The same, for a mouse button, remembering where it was pressed.</summary>
    private void HoldButton(ExecutableStep step)
    {
        var device = Input(step);
        var button = Button(step);
        var point = Point(step, "x", "y");
        device.MouseDown(button, point.X, point.Y);
        if (!_heldButtons.Any(held =>
                Same(held.Device, device) && Same(held.Button, button)))
        {
            _heldButtons.Add((device, button, point));
        }
    }

    /// <summary>Releases a mouse button the macro asked to release, wherever it is now held.</summary>
    private void LetGoButton(ExecutableStep step)
    {
        var device = Input(step);
        var button = Button(step);
        var point = Point(step, "x", "y");
        device.MouseUp(button, point.X, point.Y);
        _heldButtons.RemoveAll(held => Same(held.Device, device) && Same(held.Button, button));
    }

    /// <summary>True when two device handles or two names stand for the same thing.</summary>
    private static bool Same(IInputDevice device, IInputDevice other) => ReferenceEquals(device, other);

    private static bool Same(string name, string other)
        => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lets go of anything the macro left pressed. A key or a button held down when a run ends
    /// stays down on the machine until something else takes it up — the user's own typing goes
    /// to the wrong place and the mouse drags whatever it is over — so this happens however the
    /// run ended: finished, stopped, failed or cancelled. A release that fails is logged rather
    /// than raised, because the run already has its answer and a stuck key is not made better by
    /// throwing.
    /// </summary>
    private void LetGoOfHeldInput()
    {
        if (_heldKeys.Count == 0 && _heldButtons.Count == 0 && !_gamepadUsed)
        {
            return;
        }

        var keys = _heldKeys.ToList();
        var buttons = _heldButtons.ToList();
        _heldKeys.Clear();
        _heldButtons.Clear();

        var released = 0;
        foreach (var (device, key) in keys)
        {
            try
            {
                device.KeyUp(key);
                released++;
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, "Run.LetGoFailed", key, failure.Message);
            }
        }

        foreach (var (device, button, point) in buttons)
        {
            try
            {
                device.MouseUp(button, point.X, point.Y);
                released++;
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, "Run.LetGoFailed", button, failure.Message);
            }
        }

        // A controller left holding a button, or a stick pushed over, would keep a game walking
        // into a wall long after the macro that pushed it has finished.
        if (_gamepadUsed)
        {
            _gamepadUsed = false;
            try
            {
                _devices.Gamepad.ReleaseAll();
                Log(LogLevel.Info, 0, string.Empty, "Run.GamepadReleased");
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, "Run.GamepadLetGoFailed", failure.Message);
            }
        }

        if (released > 0)
        {
            Log(LogLevel.Info, 0, string.Empty, "Run.LetGo", released);
        }
    }

    /// <summary>Where a step wants its input to go. Anything unset means the front window.</summary>
    private InputRoute Route(ExecutableStep step)
    {
        var mode = step.Text("inputMode").Trim().ToLowerInvariant();
        if (mode == "driver")
        {
            return new InputRoute(InputDelivery.Driver, 0);
        }

        if (mode != "background")
        {
            return InputRoute.Front;
        }

        // Posting messages needs a window to post them at. The step names it the way the window
        // actions name one — the text held up against the window the way it says to hold it up —
        // so a step that types into a window whose title moves with the document is not left
        // without a handle on it.
        var rule = WindowRuleOf(step, "targetWindow", "targetHow");
        if (rule.Text.Length == 0)
        {
            throw new StepFailure("Run.MissingTargetWindow");
        }

        var window = _devices.Windows.Find(rule.Text, rule.Match, rule.Compare)
                     ?? throw new StepFailure("Run.WindowNotFound", rule.Text);

        return new InputRoute(InputDelivery.Background, window.Handle);
    }

    /// <summary>
    /// The corner a step's numbers are measured from, or <c>null</c> when they are screen pixels
    /// already. It is looked up afresh every time the step runs, which is what keeps a macro that
    /// names a window or a control pointing at the same place after the window has been dragged
    /// elsewhere or the list under the control has been scrolled.
    /// </summary>
    private ScreenPoint? Anchor(ExecutableStep step) => AnchorOf(step).Corner;

    /// <summary>
    /// The same, together with the window those numbers were counted from when there was one. The
    /// window is also where the step's pixels are to be read from: a step that says "a hundred
    /// pixels into the game window" and a step that says "read the game window" are saying the same
    /// thing about which window, and one of them should not have to say it twice.
    /// </summary>
    private (ScreenPoint? Corner, WindowInfo? Window) AnchorOf(ExecutableStep step)
    {
        var mode = step.Text("anchorMode").Trim().ToLowerInvariant();

        if (mode == "element")
        {
            var selector = step.Text("anchorSelector");
            var anchor = _devices.Ui.FindAll(AnchorQuery(step), 1).FirstOrDefault()
                         ?? throw new StepFailure("Run.ElementNotFound", selector);

            return (anchor.Location, null);
        }

        if (mode is not ("window" or "client"))
        {
            return (null, null);
        }

        // The window is recognised the way the window actions recognise one, and no text at all
        // means the window in front, the same way every other window field reads.
        var rule = WindowRuleOf(step, "anchorWindow", "anchorHow");
        var window = _devices.Windows.Find(rule.Text, rule.Match, rule.Compare)
                     ?? throw new StepFailure("Run.WindowNotFound", rule.Text);

        var corner = mode == "client" ? _devices.Windows.ClientOrigin(window.Handle)
            : window.Location;
        return (corner, window);
    }

    /// <summary>One written position, in the screen pixels the devices ask for.</summary>
    private ScreenPoint Place(ExecutableStep step, int x, int y) => Placed(Anchor(step), x, y);

    private static ScreenPoint Placed(ScreenPoint? origin, int x, int y)
        => origin is { } corner ? new ScreenPoint(corner.X + x, corner.Y + y) : new ScreenPoint(x, y);

    /// <summary>The colour of the pixel a step points at, counted from wherever it anchors.</summary>
    private PixelColor PixelAt(ExecutableStep step)
    {
        var (corner, window) = Spot(step);
        return PixelAt(step, corner, window);
    }

    /// <summary>
    /// The colour of one pixel. A pixel the step counts from the screen is read straight off the
    /// screen, which is a fraction of the cost of a picture; one it counts from a window is read
    /// out of a picture of that window, so a window another window covers still gives its own
    /// colour rather than the covering window's.
    /// </summary>
    private PixelColor PixelAt(ExecutableStep step, ScreenPoint point, WindowInfo? window)
    {
        if (window is null)
        {
            // A pixel of the desktop is read straight off it rather than out of a picture, so this
            // is the one reading that has to say for itself that it is not reading a window.
            Reading(null);
            return _devices.Screen.PixelAt(point.X, point.Y);
        }

        var shot = Shot(step, point.X, point.Y, 1, 1, window);
        if (shot.Origin != point)
        {
            // The picture came back from somewhere else, which means the window does not reach
            // that far: there is no such pixel, and reading a neighbour's colour would be a lie.
            throw new StepFailure("Run.PixelOutsideScreen", $"{point.X},{point.Y}");
        }

        return shot.Frame[0, 0];
    }

    /// <summary>
    /// Whether this kind of step is one that looks at the screen, and so is one whose looking can
    /// be tried out on its own. It is what the dialog offers "try looking" for, and the only thing
    /// <see cref="LookOnce"/> will look at the screen for.
    /// </summary>
    public static bool CanLook(string stepType) => stepType is
        "vision.findImage" or "vision.waitImage" or "vision.clickImage"
        or "vision.findColor" or "vision.waitColor" or "vision.getPixel"
        or "vision.capture" or "vision.captureWindow"
        or "ocr.recognize" or "ocr.findText" or "ocr.clickText";

    /// <summary>
    /// Looks at the screen the way a step would and hands back what it saw, without doing anything
    /// about it: no key is pressed, no mouse button goes down, no variable is written. It answers
    /// "what does this step actually see", where running the step would answer it by clicking on
    /// the user's screen. Null when this kind of step does not look at the screen at all.
    /// <paramref name="watch"/> is told which window is being read as it happens, if anybody wants
    /// to be: it is the same callback a run hands over, so a trial can show what a run shows.
    /// </summary>
    public static LookOutcome LookOnce(ExecutableStep step, IDeviceLayer devices,
        IRunLooks? watch = null)
    {
        if (!CanLook(step.Type))
        {
            return new LookOutcome(null, "Run.NotALookingStep");
        }

        var asked = new Asked(watch);
        try
        {
            new MacroRunner(new VariableStore(), devices: devices, looks: asked).SeeOnce(step);
        }
        catch (ExpressionException failure)
        {
            return new LookOutcome(null, "Run.BadExpression", failure.Detail);
        }
        catch (DeviceUnavailableException failure)
        {
            return new LookOutcome(null, "Run.NoDevice", failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while looking, the answer is a message rather than
            // an exception in the dialog's face.
            return new LookOutcome(null, "Run.DeviceFailed", failure.Message);
        }

        return asked.Seen is { } look
            ? new LookOutcome(look)
            : new LookOutcome(null, "Run.NotALookingStep");
    }

    /// <summary>
    /// Answers one condition the way a run would, on the values in play at that moment and without
    /// carrying anything out. This is what a breakpoint's own condition is asked with: the debugger
    /// has the values the run has just written, and none of the run's own machinery. A condition
    /// that cannot be answered at all comes back saying why rather than throwing, so the caller can
    /// tell "no" apart from "could not be asked".
    /// </summary>
    public static ConditionOutcome AskCondition(ExecutableStep condition, VariableStore variables,
        IDeviceLayer devices, IMacroLibrary? macros = null)
    {
        try
        {
            return new ConditionOutcome(new MacroRunner(variables, devices: devices, macros: macros)
                .Check(condition, 0, CancellationToken.None));
        }
        catch (ExpressionException failure)
        {
            return new ConditionOutcome(false, "Run.BadExpression", failure.Detail);
        }
        catch (DeviceUnavailableException failure)
        {
            return new ConditionOutcome(false, "Run.NoDevice", failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while a condition is asked, the answer is a message
            // rather than an exception in the debugger's face.
            return new ConditionOutcome(false, "Run.DeviceFailed", failure.Message);
        }
    }

    /// <summary>Where a look goes when the caller only wanted the one.</summary>
    private sealed class Asked(IRunLooks? watch) : IRunLooks
    {
        public StepLook? Seen { get; private set; }

        public void Look(StepLook look) => Seen = look;

        /// <summary>Nothing is read from what a step came to; only the one look was wanted.</summary>
        public void Ran(StepRun run)
        {
        }

        public void Reading(WindowInfo? window) => watch?.Reading(window);
    }

    /// <summary>
    /// One look at the screen for a step, without the step: a search looks once rather than
    /// waiting, a wait does not wait, and the mouse is left alone.
    /// </summary>
    private void SeeOnce(ExecutableStep step)
    {
        switch (step.Type)
        {
            case "vision.findImage":
            case "vision.waitImage":
            case "vision.clickImage":
                Watching(() => Hits(step));
                break;

            case "vision.findColor":
            case "vision.waitColor":
                Watching(() => Scan(step, PixelColor.Parse(Read(step.Text("color")).AsText()),
                    Read(step.Text("tolerance")).AsNumber(), measured: false));
                break;

            case "vision.findColors":
                Watching(() => PatternHits(step));
                break;

            case "vision.getPixel":
                Watching(() =>
                {
                    var (point, window) = Spot(step);
                    SawPixel(step, point, PixelAt(step, point, window).ToHex(), window);
                });
                break;

            case "vision.capture":
                Watching(() => Took(step, CaptureBox(step)));
                break;

            case "vision.captureWindow":
                Watching(() => Took(step, WindowBox(step)));
                break;

            case "ocr.recognize":
                Watching(() => WasRead(step, ReadBox(step)));
                break;

            case "ocr.findText":
            case "ocr.clickText":
                Watching(() => SawSought(step));
                break;

            default:
                break;
        }
    }

    /// <summary>Hands over a picture that was taken, without keeping it anywhere.</summary>
    private void Took(ExecutableStep step, (ImageFrame Frame, ScreenPoint Corner) taken)
        => Saw(step, LookKind.Capture, taken.Frame, taken.Corner,
            [
                new(new ImageMatch(0, taken.Corner,
                    new ScreenSize(taken.Frame.Width, taken.Frame.Height)), LookRole.Area),
            ],
            // The name the picture would be handed over under, which is what the run itself puts
            // here when it really takes one; a step asked to try its looking writes nothing.
            1, step.Return("image"));

    /// <summary>Hands over what was read off a picture, without writing the reading anywhere.</summary>
    private void WasRead(ExecutableStep step,
        (ScreenPoint Corner, ImageFrame Area, IReadOnlyList<TextSpan> Spans) read)
        => Saw(step, LookKind.Text, read.Area, read.Corner,
            Marks([(read.Area, read.Corner)], TextMarks(read.Spans, read.Corner), 0), 0,
            note: string.Join(' ', read.Spans.Select(span => span.Text)),
            minimum: Number(step, "minScore"));

    /// <summary>Hands over what a step looking for writing would find right now.</summary>
    private void SawSought(ExecutableStep step)
    {
        var readings = ReadSpans(step);
        var (_, chosen) = Counting(step, readings, Read(step.Text("text")).AsText(),
            step.Text("matchMode"));

        SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen,
            Read(step.Text("text")).AsText(), minimum: Number(step, "minScore"));
    }

    /// <summary>Where the pixel a step watches sits on the screen, window anchoring and all.</summary>
    private ScreenPoint PixelPoint(ExecutableStep step) => Spot(step).Corner;

    /// <summary>
    /// The same, with the window the point was counted from when there was one, which is also the
    /// window its colour has to be read out of.
    /// </summary>
    private (ScreenPoint Corner, WindowInfo? Window) Spot(ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        return (Placed(anchor.Corner, Number(step, "x"), Number(step, "y")), anchor.Window);
    }

    /// <summary>
    /// Where a mouse action lands. An empty position means "wherever the pointer already is",
    /// which is what a click or a press with no coordinates written down means; a step that says so
    /// outright reads the pointer the same way, whatever its numbers hold. Asking for the pointer is
    /// answered before anything is measured from, so a step that says it works at the pointer does
    /// not need the window its numbers would have been counted from to be there at all.
    /// </summary>
    private ScreenPoint Point(ExecutableStep step, string xName, string yName)
    {
        if (Flag(step, "useCursor", false))
        {
            return Input(step).Cursor;
        }

        var x = step.Text(xName).Trim();
        var y = step.Text(yName).Trim();
        return x.Length == 0 && y.Length == 0
            ? Input(step).Cursor
            : Place(step, Number(step, xName), Number(step, yName));
    }

    private static string Button(ExecutableStep step)
    {
        var button = step.Text("button").Trim();
        return button.Length == 0 ? "left" : button;
    }

    /// <summary>
    /// Moves the pointer to a point. A straight move is handed to the device exactly as it always
    /// was; a bent or hand-like one is worked out into a path first, because the shape of that
    /// path is the whole point of those styles.
    /// </summary>
    private void MovePointer(ExecutableStep step, ScreenPoint to)
    {
        var route = MousePath.Route(step.Text("style"));
        var duration = Pace(Number(step, "durationMs"));
        if (route is MouseRoute.Direct)
        {
            Input(step).MoveMouse(to.X, to.Y, duration);
            return;
        }

        WalkPointer(Input(step), route, Input(step).Cursor, to, duration);
    }

    /// <summary>The same, for a move written as an offset from wherever the pointer is.</summary>
    private void MovePointerBy(ExecutableStep step)
    {
        var dx = Number(step, "dx");
        var dy = Number(step, "dy");
        var route = MousePath.Route(step.Text("style"));
        var duration = Pace(Number(step, "durationMs"));
        if (route is MouseRoute.Direct)
        {
            Input(step).MoveMouseRelative(dx, dy, duration);
            return;
        }

        var from = Input(step).Cursor;
        WalkPointer(Input(step), route, from, new ScreenPoint(from.X + dx, from.Y + dy), duration);
    }

    private void WalkPointer(IInputDevice input, MouseRoute route, ScreenPoint from,
        ScreenPoint to, int duration)
    {
        if (duration <= 0)
        {
            duration = Pace(BorrowedMoveMs);
        }

        input.MoveMouseAlong(
            MousePath.Plan(route, from, to, MousePath.StepsFor(route, duration)), duration);
    }

    /// <summary>Presses, travels, and lets go: the same shape of path as a plain move.</summary>
    private async Task DragPointer(ExecutableStep step, CancellationToken token)
    {
        // Both ends are measured from the same corner, so a drag across a window stays inside it.
        var origin = Anchor(step);
        var from = Placed(origin, Number(step, "startX"), Number(step, "startY"));
        var to = Placed(origin, Number(step, "endX"), Number(step, "endY"));
        var duration = Pace(Number(step, "durationMs"));
        var settle = Pace(OptionalNumber(step, "settleMs", 0));
        var steps = Math.Clamp(Number(step, "steps"), 1, 200);
        var route = MousePath.Route(step.Text("style"));
        if (route is not MouseRoute.Direct)
        {
            if (duration <= 0)
            {
                duration = Pace(BorrowedMoveMs);
            }
        }

        if (settle <= 0)
        {
            if (route is MouseRoute.Direct)
            {
                Input(step).Drag(Button(step), from.X, from.Y, to.X, to.Y, duration, steps);
            }
            else
            {
                Input(step).DragAlong(Button(step), MousePath.Plan(route, from, to, steps), duration);
            }

            return;
        }

        // Held at the end for a moment before letting go: a slot a thing is dragged into only takes
        // it once the pointer has rested over it, and letting go the instant the pointer arrives is
        // how a drag onto something turns into a drag past it. That is three calls rather than the
        // one the devices offer, because the one they offer lets go before it comes back.
        Input(step).MouseDown(Button(step), from.X, from.Y);
        Input(step).MoveMouseAlong(MousePath.Plan(route, from, to, steps), duration);
        await Pause(settle, token);
        Input(step).MouseUp(Button(step), to.X, to.Y);
    }

    private static IReadOnlyList<string> Keys(ExecutableStep step)
        => Chord(step.Text("keys"));

    /// <summary>The keys of one written combination, such as the Ctrl+Shift+S a row of a sequence holds.</summary>
    private static IReadOnlyList<string> Chord(string written)
        => [.. written.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Sends a run of combinations in order, one row after another, the way a macro plays out a
    /// combo. Each row is held together for its own moment and then the run waits out the beat
    /// before the next one — the gaps are what make a run read as separate presses rather than as
    /// one long hold, and they are the part of a combo a game notices. A row may carry its own hold
    /// and its own gap, because a combination with one beat of its own is the rule rather than the
    /// exception; the whole run may be played again with a pause between.
    /// </summary>
    private async Task SendKeySequence(ExecutableStep step, int depth, CancellationToken token)
    {
        var rows = step.Rows("keys");
        var hold = Number(step, "holdMs");
        var gap = Number(step, "gapMs");
        var after = Pace(Number(step, "afterMs"));
        var runs = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));
        var presses = 0;

        for (var run = 0; run < runs; run++)
        {
            token.ThrowIfCancellationRequested();
            if (run > 0)
            {
                await Pause(interval, token);
            }

            for (var index = 0; index < rows.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var keys = Chord(Column(rows[index], "keys"));
                if (keys.Count == 0)
                {
                    // A row that was added and never filled in is left out rather than sent as a
                    // press of nothing.
                    continue;
                }

                Input(step).Hotkey(keys, Pace(Cell(rows[index], "holdMs", hold)));
                presses++;

                // The beat is between the presses; there is nothing to wait for after the last one.
                if (index < rows.Count - 1)
                {
                    await Pause(Pace(Cell(rows[index], "gapMs", gap)), token);
                }
            }

            if (after > 0)
            {
                await Pause(after, token);
            }
        }

        if (presses == 0)
        {
            throw new StepFailure("Run.NoKeys", string.Empty);
        }

        Log(LogLevel.Info, depth, step.Type, "Run.KeysSent", presses);
    }

    /// <summary>
    /// Presses a key, over again when the step asks for more than one. The pause goes between the
    /// presses and never after the last, so a step that presses once waits for nothing.
    /// </summary>
    private async Task PressKey(ExecutableStep step, CancellationToken token)
    {
        var key = step.Text("key");
        var hold = Pace(Number(step, "holdMs"));
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            Input(step).KeyPress(key, hold);
        }
    }

    /// <summary>
    /// Holds a controller button or lets it up, or presses and lets go of it. A tap is what a
    /// button is normally for, so it is the one that happens when the step does not say; the hold
    /// is the pause between the press and the release, and a step that asked for none still gets a
    /// short one, because a report that comes and goes in the same instant is one a game may never
    /// see. Tapping several times is the same question the key actions answer with repeat and
    /// interval, so it is answered the same way here: one pause between taps, none before the first.
    /// </summary>
    private async Task PressGamepadButton(ExecutableStep step, CancellationToken token)
    {
        var device = _devices.Gamepad;
        var button = step.Text("button");
        _gamepadUsed = true;

        switch (step.Text("mode").Trim().ToLowerInvariant())
        {
            case "down":
                device.Button(button, true);
                return;
            case "up":
                device.Button(button, false);
                return;
        }

        var hold = Math.Max(Pace(Number(step, "holdMs")), ShortestPressMs);
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            device.Button(button, true);
            await Pause(hold, token);
            device.Button(button, false);
        }
    }

    /// <summary>The same for a combination: it can be sent more than once with a pause between.</summary>
    private async Task SendHotkey(ExecutableStep step, CancellationToken token)
    {
        var keys = Keys(step);
        var hold = Pace(Number(step, "holdMs"));
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            Input(step).Hotkey(keys, hold);
        }
    }

    /// <summary>
    /// Clicks the mouse. The device's own click is a quick tap, so a step that asks for a hold
    /// presses, waits and releases by itself; without one the device does the whole thing, which
    /// also covers a click aimed at a background window.
    /// </summary>
    /// <summary>Wheel units in one notch, the amount a wheel turns in.</summary>
    private const int WheelNotch = 120;

    /// <summary>
    /// Turns the wheel. "Notches" is the step a wheel normally moves in; "pixels" measures the
    /// same turn finer, with 120 units to a notch, which is what an exact amount needs. A smooth
    /// duration splits the whole turn over several smaller wheel events with a pause between, so
    /// an application that animates its scrolling has the time to follow.
    /// </summary>
    private async Task ScrollMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var direction = step.Text("direction");
        var pixels = string.Equals(step.Text("unit").Trim(), "pixels", StringComparison.OrdinalIgnoreCase);
        var amount = Math.Max(1, Number(step, "amount"));
        var total = pixels ? amount : amount * WheelNotch;
        var smooth = Pace(Number(step, "smoothMs"));
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            if (smooth <= 0)
            {
                Input(step).Scroll(direction, total, point.X, point.Y);
                continue;
            }

            // One event per 15 ms is about 66 a second: roughly the fastest an application would
            // draw its scrolling at, and never more events than there are units to send.
            var events = Math.Clamp(smooth / 15, 1, total);
            var share = total / events;
            var extra = total % events;

            for (var index = 0; index < events; index++)
            {
                token.ThrowIfCancellationRequested();
                if (index > 0)
                {
                    await Pause(smooth / events, token);
                }

                Input(step).Scroll(direction, share + (index < extra ? 1 : 0), point.X, point.Y);
            }
        }
    }

    private async Task ClickMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var button = Button(step);
        var clicks = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));
        var hold = Pace(Number(step, "holdMs"));
        var spread = Spread(step);
        var from = ArrivedFrom(step);

        // A step that lets the clicks drift, or that wants the pointer put back, has to place every
        // click itself: sending them all at once would put them all on the same pixel, and there
        // would be nowhere to put the pointer back from.
        if (hold <= 0 && spread == 0 && from is null)
        {
            Input(step).Click(button, point.X, point.Y, clicks, interval);
            return;
        }

        for (var count = 0; count < clicks; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            var at = Scattered(point, spread);
            if (hold <= 0)
            {
                Input(step).Click(button, at.X, at.Y, 1, 0);
                continue;
            }

            Input(step).MouseDown(button, at.X, at.Y);
            await Pause(hold, token);
            Input(step).MouseUp(button, at.X, at.Y);
        }

        if (from is { } back)
        {
            Input(step).MoveMouse(back.X, back.Y, 0);
        }
    }

    /// <summary>
    /// Double clicks the mouse: two presses in quick succession, which is what tells the machine a
    /// double click from two single ones. The step may ask for several of them in a row with a
    /// pause between, which is the same question the other input steps answer with repeat and
    /// interval; there is no hold, because holding the button is what makes it stop being a double
    /// click.
    /// </summary>
    private async Task DoubleClickMouse(ExecutableStep step, CancellationToken token)
    {
        var point = Point(step, "x", "y");
        var repeats = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "intervalMs"));
        var spread = Spread(step);
        var from = ArrivedFrom(step);

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pause(interval, token);
            }

            var at = Scattered(point, spread);
            Input(step).Click(Button(step), at.X, at.Y, 2, 0);
        }

        if (from is { } back)
        {
            Input(step).MoveMouse(back.X, back.Y, 0);
        }
    }

    /// <summary>
    /// How far a click is allowed to drift from the point the step names, in pixels: 0, the default,
    /// is the point itself.
    /// </summary>
    private int Spread(ExecutableStep step)
    {
        var written = step.Text("variance").Trim();
        return written.Length == 0 ? 0 : Math.Clamp(Number(step, "variance"), 0, 100000);
    }

    /// <summary>
    /// A point with the drift the step asked for on it, so a run of clicks is spread over a button
    /// rather than landing on one pixel. A drift of nothing is the point itself, to the pixel.
    /// </summary>
    private static ScreenPoint Scattered(ScreenPoint point, int spread)
        => spread <= 0
            ? point
            : new ScreenPoint(
                point.X + Random.Shared.Next(-spread, spread + 1),
                point.Y + Random.Shared.Next(-spread, spread + 1));

    /// <summary>
    /// Where the pointer was before a step that has to put it back, or null when the step does not
    /// put it back. Posting messages at a window never moves the pointer on screen, so there is
    /// nothing to put back there and nothing is read.
    /// </summary>
    private ScreenPoint? ArrivedFrom(ExecutableStep step)
        => Flag(step, "moveBack", false) && Route(step).Delivery is not InputDelivery.Background
            ? Input(step).Cursor
            : null;

    // -------------------------------------------------------------------- looks

    /// <summary>
    /// Runs one step that looks at the screen and hands over what it saw, whatever came of it: the
    /// look of a step that found nothing is the one worth having.
    /// </summary>
    private void Watching(Action body)
    {
        _saw = null;
        try
        {
            body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>The same, for a step that has to wait for what it is looking at.</summary>
    private async Task Watching(Func<Task> body)
    {
        _saw = null;
        try
        {
            await body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>Hands the look over to whoever is watching, and forgets it.</summary>
    private void Show()
    {
        var look = _saw;
        _saw = null;
        if (_looks is null || look is null)
        {
            return;
        }

        _looks.Look(look);
    }

    /// <summary>
    /// How the marks are built, and what sort of looking this was. Nothing is built when nobody is
    /// watching: a picture of every step of a run nobody is looking at is work for nothing, and a
    /// step that waits would build one every two hundred milliseconds.
    /// </summary>
    private void Saw(ExecutableStep step, LookKind kind, ImageFrame frame, ScreenPoint origin,
        IReadOnlyList<LookBox> boxes, int chosen, string looking = "", ImageFrame? needle = null,
        string? note = null, double? minimum = null, int picture = 0, int pictures = 0)
    {
        if (_looks is null)
        {
            return;
        }

        _saw = new StepLook
        {
            StepId = step.Id,
            StepType = step.Type,
            Kind = kind,
            Frame = frame,
            Origin = origin,
            Boxes = boxes,
            ChosenIndex = chosen,
            Looking = looking,
            Needle = needle,
            Note = note,
            Minimum = minimum,
            PictureNumber = picture,
            PictureCount = pictures,
            Source = _takenWindow,
            Method = _takenMethod ?? CaptureWay.Auto,
        };
    }

    /// <summary>
    /// What a step that looked for something saw: the areas it searched, every place the thing was
    /// found, and the one it went with. Several areas are put together into one picture so that
    /// every mark has somewhere to sit.
    /// </summary>
    private void SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string looking = "",
        ImageFrame? needle = null, double? minimum = null, int picture = 0, int pictures = 0)
    {
        if (_looks is null)
        {
            return;
        }

        var (frame, origin) = Picture(areas);
        Saw(step, kind, frame, origin, Marks(areas, found, chosen),
            chosen == 0 ? 0 : areas.Count + chosen, looking, needle, null, minimum, picture, pictures);
    }

    /// <summary>
    /// Adds where a step has just acted to the look it took to get there, so a step that clicked
    /// the wrong place can be seen rather than guessed at.
    /// </summary>
    private void Aim(ScreenPoint point)
    {
        if (_looks is null || _saw is null)
        {
            return;
        }

        var boxes = new List<LookBox>(_saw.Boxes)
        {
            new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
        };

        _saw = _saw with { Boxes = boxes, ChosenIndex = boxes.Count };
    }

    /// <summary>
    /// A look at a single pixel: a square of screen around it with the pixel itself marked, which
    /// is what a step watching for a colour has to show for itself.
    /// </summary>
    private void SawPixel(ExecutableStep step, ScreenPoint point, string looking,
        WindowInfo? window)
    {
        if (_looks is null)
        {
            return;
        }

        var desk = _devices.Screen.Desktop.Bounds;
        var left = Math.Clamp(point.X - Neighbourhood, desk.Left, Math.Max(desk.Left, desk.Right - 1));
        var top = Math.Clamp(point.Y - Neighbourhood, desk.Top, Math.Max(desk.Top, desk.Bottom - 1));
        var right = Math.Clamp(point.X + Neighbourhood + 1, left + 1, desk.Right);
        var bottom = Math.Clamp(point.Y + Neighbourhood + 1, top + 1, desk.Bottom);

        var shot = Shot(step, left, top, right - left, bottom - top, window);
        Saw(step, LookKind.Pixel, shot.Frame, shot.Origin,
            [
                new(new ImageMatch(1, shot.Origin,
                    new ScreenSize(shot.Frame.Width, shot.Frame.Height)), LookRole.Area),
                new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
            ],
            2, looking);
    }

    /// <summary>How far around a watched pixel the look reaches, so the pixel has some context.</summary>
    private const int Neighbourhood = 60;

    /// <summary>The marks for a look: where it looked, and everything that was found there.</summary>
    private static List<LookBox> Marks(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen)
    {
        var marks = new List<LookBox>();
        foreach (var (area, origin) in areas)
        {
            marks.Add(new LookBox(
                new ImageMatch(0, origin, new ScreenSize(area.Width, area.Height)), LookRole.Area));
        }

        for (var index = 0; index < found.Count; index++)
        {
            marks.Add(new LookBox(
                found[index].Match,
                index + 1 == chosen ? LookRole.Hit : LookRole.Candidate,
                found[index].Label));
        }

        return marks;
    }

    /// <summary>
    /// The picture to draw a step's marks on: the single area it looked at, or the whole of what
    /// it looked at when there are several. Several are laid out of the areas themselves rather
    /// than read off the screen again — where the areas came from is not always the screen, and a
    /// step that reads a window it does not cover would otherwise be drawn over a picture of
    /// whatever is on top of it.
    /// </summary>
    private static (ImageFrame Frame, ScreenPoint Origin) Picture(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas)
    {
        if (areas.Count == 1)
        {
            return areas[0];
        }

        var left = areas.Min(area => area.Origin.X);
        var top = areas.Min(area => area.Origin.Y);
        var width = Math.Max(1, areas.Max(area => area.Origin.X + area.Frame.Width) - left);
        var height = Math.Max(1, areas.Max(area => area.Origin.Y + area.Frame.Height) - top);
        var pixels = new byte[(long)width * height * 4];
        foreach (var (frame, origin) in areas)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Array.Copy(frame.Bgra, (long)row * frame.Width * 4, pixels,
                    ((long)(origin.Y - top + row) * width + (origin.X - left)) * 4,
                    (long)frame.Width * 4);
            }
        }

        return (new ImageFrame(width, height, pixels), new ScreenPoint(left, top));
    }

    /// <summary>Copies a region of the screen into a variable the macro can look at again.</summary>
    private void Capture(ExecutableStep step, int depth)
    {
        var (frame, corner) = CaptureBox(step);
        Keep(step, depth, frame, corner);
    }

    /// <summary>
    /// The rectangle a capture step covers, taken off the screen. The corner is worked out first so
    /// that the rectangle reported is the one on screen, which is what a later "region" naming this
    /// picture has to read.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Corner) CaptureBox(ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        var corner = Placed(anchor.Corner, Number(step, "x"), Number(step, "y"));
        var (width, height) = Extent(step, corner);
        var shot = Shot(step, corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// Copies a whole window into a variable. The window's own rectangle is what is copied, so the
    /// title bar and the border come with it, and a window that is not in front can be read too.
    /// </summary>
    private void CaptureWindow(ExecutableStep step, int depth)
    {
        var (frame, corner) = WindowBox(step);
        Keep(step, depth, frame, corner);
    }

    /// <summary>The whole of the window a step names, taken off the screen.</summary>
    private (ImageFrame Frame, ScreenPoint Corner) WindowBox(ExecutableStep step)
    {
        var window = Locate(step);
        var shot = Shot(step, window.Location.X, window.Location.Y, window.Size.Width,
            window.Size.Height, window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// Writes a picture out as a file. The picture is named the same way every other step that
    /// takes one names it — an image variable from an earlier step, or a file that is already
    /// there — so a macro can capture, look, and keep the one that turned out to matter. It is
    /// always a PNG: the format is the small end of what a picture can be written as, and a macro
    /// that wants something else has a program for it.
    /// </summary>
    private void SavePicture(ExecutableStep step, int depth)
    {
        var picture = Reference(step);
        if (picture.IsEmpty)
        {
            throw new StepFailure("Run.MissingImage", step.Text("image"));
        }

        var path = PathOf(step);
        _devices.Files.WriteBytes(path, PngWriter.Encode(picture));
        Log(LogLevel.Info, depth, step.Type, "Run.SavedPicture", path,
            $"{picture.Width}x{picture.Height}");
    }

    /// <summary>
    /// Keeps a picture under the name a step gave it, with the rectangle it covers beside it, so a
    /// later step can search inside it or hand the region to something else. The size written down
    /// is the picture's own rather than the size that was asked for: a capture that ran off the
    /// edge of the screen comes back smaller, and a search region that is bigger than the picture
    /// would look at pixels that are not there.
    /// </summary>
    private void Keep(ExecutableStep step, int depth, ImageFrame frame, ScreenPoint corner)
    {
        // The picture is kept under the name of the image result, and only there: a step that was
        // told not to hand the picture over leaves nothing for a later step to search in, which is
        // what leaving that result unticked means.
        var name = Give(step, "image", Value.FromText($"<image {frame.Width}x{frame.Height}>"));
        if (name.Length > 0)
        {
            _images[name] = frame;
        }

        // Where the picture sits is one record beside the picture, so it can be handed to a search
        // region as it stands or read one part at a time.
        Give(step, "place", Value.FromRecord(
        [
            ("left", Value.FromNumber(corner.X)),
            ("top", Value.FromNumber(corner.Y)),
            ("width", Value.FromNumber(frame.Width)),
            ("height", Value.FromNumber(frame.Height)),
            ("box", Value.FromText($"{corner.X},{corner.Y},{frame.Width},{frame.Height}")),
        ]));

        GiveTaken(step);
        Saw(step, LookKind.Capture, frame, corner,
            [new LookBox(new ImageMatch(0, corner, new ScreenSize(frame.Width, frame.Height)), LookRole.Area)],
            1, name);
        if (name.Length > 0)
        {
            Log(LogLevel.Info, depth, step.Type, "Run.Capture", name, $"{frame.Width}x{frame.Height}");
        }
    }

    private void GetPixel(ExecutableStep step, int depth)
    {
        var (point, window) = Spot(step);
        var colour = PixelAt(step, point, window);
        var asHex = !string.Equals(step.Text("asHex").Trim(), "false", StringComparison.OrdinalIgnoreCase);
        var name = Give(step, "color", asHex
            ? Value.FromText(colour.ToHex())
            : Value.FromNumber((colour.R) | (colour.G << 8) | (colour.B << 16)));

        SawPixel(step, point, colour.ToHex(), window);
        if (name.Length > 0)
        {
            Log(LogLevel.Info, depth, step.Type, "Run.Set", name, colour.ToHex());
        }
    }

    private async Task WaitColor(ExecutableStep step, int depth, CancellationToken token)
    {
        var target = PixelColor.Parse(Read(step.Text("color")).AsText());
        var tolerance = Read(step.Text("tolerance")).AsNumber();

        // The corner is looked up again on every check, so a wait that watches a window keeps
        // watching the same spot inside it even if the window is moved while the macro waits.
        var (point, window) = Spot(step);
        var interval = LookBeat(step);
        await WaitForFlagAsync(
            () => PixelAt(step).Matches(target, tolerance), interval, token);
        SawPixel(step, point, target.ToHex(), window);

        Log(LogLevel.Info, depth, step.Type, "Run.SawColor", target.ToHex());
    }

    /// <summary>
    /// Looks for a colour in the search areas and writes where it was found, once: what it does not
    /// find it leaves empty rather than waiting for, the way finding a picture does. A step that
    /// names a least share of the region — how full a bar has to be before it acts — counts as
    /// finding nothing until that much of the region shows the colour, and waiting on it is the
    /// job of <c>vision.waitColor</c> or of a wait-until block.
    /// </summary>
    private void FindColor(ExecutableStep step, int depth)
    {
        var target = PixelColor.Parse(Read(step.Text("color")).AsText());
        var tolerance = Read(step.Text("tolerance")).AsNumber();
        var least = Read(step.Text("minCoverage")).AsNumber() / 100;

        // The share is only measured when the step asks about it, either by handing it over or by
        // saying below which share it will not act: the walk it costs is otherwise wasted.
        var measured = least > 0 || step.Return("coverage").Length > 0;
        var pass = Scan(step, target, tolerance, measured);

        // The share is handed over whether or not the colour was found: a region the step will not
        // act on is exactly when the number is worth reading.
        if (pass.Coverage is { } share)
        {
            Give(step, "coverage", Value.FromNumber(Math.Round(share * 100, 1)));
        }

        var hits = pass.Enough(least) ? pass.Hits : [];
        var match = Chosen(hits, step);
        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, match.Score);
        }

        GiveEvery(step, HitRecords(hits));
        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ImageMissing" : "Run.ImageFound",
            match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    /// <summary>
    /// One pass over the search areas, in reading order. A step that asks how much of the areas
    /// shows the colour — rather than only where it is — pays for a walk of the whole of them,
    /// which is why it is done for the steps that ask and for no others.
    /// </summary>
    private ColourPass Scan(ExecutableStep step, PixelColor target, double tolerance, bool measured)
    {
        var wanted = Wanted(step);
        var areas = SearchAreas(step);
        // Asked for by score, the pixels closest to the colour are the hits; the rest of the time
        // the first ones read left to right and top to bottom are, which is the cheaper walk.
        var surest = OrderOf(step) is MatchOrder.Score;
        var hits = new List<ImageMatch>();
        long matched = 0;
        long total = 0;
        foreach (var (area, origin) in areas)
        {
            foreach (var point in PixelSearch.Find(area, target, tolerance, wanted, surest))
            {
                hits.Add(new ImageMatch(
                    1 - area[point.X, point.Y].DistanceTo(target),
                    new ScreenPoint(origin.X + point.X, origin.Y + point.Y),
                    new ScreenSize(1, 1)));
            }

            if (measured)
            {
                matched += PixelSearch.Count(area, target, tolerance);
                total += (long)area.Width * area.Height;
            }
        }

        Sorted(hits, step);
        SawSearch(step, LookKind.Colour, areas, [.. hits.Select(hit => (hit, (string?)null))],
            WentWith(hits, step), target.ToHex());
        return new ColourPass(hits, measured && total > 0 ? (double)matched / total : null);
    }

    /// <summary>
    /// Whether enough of the search areas shows the colour, which is the question a bar, a gauge or
    /// a lamp is asked: not where the colour is but how much of the region it fills. The whole of
    /// every area is walked either way, so this is a thing to wait on rather than to ask in a tight
    /// loop. A least share of nothing asks only whether the colour is anywhere in the region, which
    /// is what a pixel-by-pixel condition cannot say.
    /// </summary>
    private bool ColourShare(ExecutableStep condition)
    {
        var target = PixelColor.Parse(Read(condition.Text("color")).AsText());
        var tolerance = Read(condition.Text("tolerance")).AsNumber();
        var least = Read(condition.Text("minCoverage")).AsNumber() / 100;
        return Scan(condition, target, tolerance, measured: true).Enough(least);
    }

    /// <summary>
    /// What one pass over the search areas came back with: where the colour was, and, for a step
    /// that asked about the share rather than the place, how much of the areas shows it.
    /// </summary>
    private sealed record ColourPass(List<ImageMatch> Hits, double? Coverage)
    {
        /// <summary>
        /// True when this pass is what the step was after: the colour is somewhere in the areas and,
        /// where a least share was named, enough of the areas shows it. A least share of zero is
        /// the step that only asked where the colour is.
        /// </summary>
        public bool Enough(double least)
            => Hits.Count > 0 && (Coverage is not { } share || share >= least);
    }

    /// <summary>
    /// Looks for a colour that has to have the colours the step names around it, and writes where
    /// it was found. One colour on its own says "something red is here", which a busy screen is
    /// full of; the colours around it say which red thing this is, which is how two buttons drawn
    /// alike are told apart without a branch for each.
    /// </summary>
    private void FindColours(ExecutableStep step, int depth)
    {
        var hits = PatternHits(step);
        var match = Chosen(hits, step);
        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, match.Score);
        }

        GiveEvery(step, HitRecords(hits));
        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ImageMissing" : "Run.ImageFound",
            match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    /// <summary>
    /// One pass over the search areas: the pixels that show the pattern's own colour with every one
    /// of the colours around it where it was asked for, in reading order. Each hit is a single pixel
    /// — its centre is the pixel itself — and its score says how close the whole pattern came, one
    /// being exact. The colours that had to be around the pixel the step went with are marked beside
    /// it, which is what tells a step whose pattern is wrong from one that looked in the wrong place.
    /// </summary>
    private List<ImageMatch> PatternHits(ExecutableStep step)
    {
        var target = PixelColor.Parse(Read(step.Text("color")).AsText());
        var tolerance = Read(step.Text("tolerance")).AsNumber();
        var around = Pattern(step);

        var wanted = Wanted(step);
        var areas = SearchAreas(step);
        var hits = new List<ImageMatch>();
        foreach (var (area, origin) in areas)
        {
            foreach (var point in PixelSearch.FindPattern(area, target, tolerance, around, wanted))
            {
                hits.Add(new ImageMatch(
                    Closeness(area, point, target, around),
                    new ScreenPoint(origin.X + point.X, origin.Y + point.Y),
                    new ScreenSize(1, 1)));
            }
        }

        Sorted(hits, step);
        var taken = WentWith(hits, step);
        var marks = new List<(ImageMatch Match, string? Label)>(
            hits.Select(hit => (hit, (string?)null)));
        if (taken > 0)
        {
            var at = hits[taken - 1].Location;
            foreach (var colour in around)
            {
                marks.Add((
                    new ImageMatch(1, new ScreenPoint(at.X + colour.Dx, at.Y + colour.Dy),
                        new ScreenSize(1, 1)),
                    colour.Colour.ToHex()));
            }
        }

        SawSearch(step, LookKind.Colour, areas, marks, taken, target.ToHex());
        return hits;
    }

    /// <summary>
    /// The colours a pattern has to have around it. They are written the way the points of a
    /// multi-point colour comparison are — a line of two numbers and a colour — but the numbers are
    /// counted from the middle of the pattern rather than from the corner of the screen, because
    /// the whole point of a pattern is that it is found wherever it happens to be.
    /// </summary>
    private IReadOnlyList<ColourStep> Pattern(ExecutableStep step)
    {
        var text = Interpolate(step.Text("points")).Trim();
        var around = new List<ColourStep>();
        foreach (var entry in text.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dx)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dy)
                || !IsColour(parts[2]))
            {
                throw new StepFailure("Run.BadPoints", entry);
            }

            around.Add(new ColourStep(dx, dy, PixelColor.Parse(parts[2])));
        }

        if (around.Count == 0)
        {
            throw new StepFailure("Run.BadPoints", text);
        }

        return around;
    }

    /// <summary>
    /// How close the whole pattern came, from 0 to 1: the average of how close each of its colours
    /// is, the one in the middle included, so a pattern found on a faded screen scores lower than
    /// the same pattern on a fresh one.
    /// </summary>
    private static double Closeness(ImageFrame area, ScreenPoint at, PixelColor target,
        IReadOnlyList<ColourStep> around)
    {
        var total = area[at.X, at.Y].DistanceTo(target);
        foreach (var step in around)
        {
            total += area[at.X + step.Dx, at.Y + step.Dy].DistanceTo(step.Colour);
        }

        return 1 - (total / (around.Count + 1));
    }

    /// <summary>
    /// Waits for the picture to hold still: the screen counts as settled once every watched area
    /// has stayed within the tolerance of the frame that started the wait for a whole quiet
    /// stretch. A fixed pause is what this replaces, and it is wrong in both directions — too
    /// short and the step after it works on half a picture, too long and every run pays for the
    /// slowest case.
    /// </summary>
    private async Task WaitStable(ExecutableStep step, int depth, CancellationToken token)
    {
        var tolerance = Read(step.Text("tolerance")).AsNumber();
        var allowed = Read(step.Text("changedPercent")).AsNumber() / 100;
        var quiet = Math.Max(0, Number(step, "quietMs"));
        var interval = LookBeat(step);

        var started = Stopwatch.GetTimestamp();
        long Elapsed() => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var anchor = SearchAreas(step);
        var since = Elapsed();

        while (true)
        {
            await Pause(interval, token);
            var now = SearchAreas(step);
            if (Still(anchor, now, tolerance, allowed))
            {
                if (Elapsed() - since >= quiet)
                {
                    Log(LogLevel.Info, depth, step.Type, "Run.FrameStill", Elapsed());
                    return;
                }
            }
            else
            {
                // Counted from this frame on: what has to hold still for the quiet stretch is not
                // the picture the wait opened on, which may have been another screen altogether,
                // but the picture the screen has just arrived at.
                anchor = now;
                since = Elapsed();
            }
        }
    }

    /// <summary>
    /// True when every watched area is the picture it is compared against, as far as the step is
    /// willing to say so: no more of it than the step allows may have moved by more than its
    /// colour tolerance.
    /// </summary>
    private static bool Still(IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> before,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> after, double tolerance, double allowed)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        for (var index = 0; index < before.Count; index++)
        {
            if (ImageFrame.ChangedShare(before[index].Frame, after[index].Frame, tolerance) > allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Looks once and writes where the picture was, or an empty value when it was not.</summary>
    private void LookFor(ExecutableStep step, int depth)
    {
        var hits = Hits(step);
        var match = Chosen(hits, step);

        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, match.Score);
        }

        GiveEvery(step, HitRecords(hits));
        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ImageMissing" : "Run.ImageFound",
            match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    private async Task WaitForImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var found = await HitsUntil(step, token);

        GiveMatch(step, found.Match.Location, found.Match.Size, found.Match.Score);
        GiveEvery(step, HitRecords(found.Hits));
        Log(LogLevel.Info, depth, step.Type, "Run.ImageFound",
            $"{found.Match.Center.X},{found.Match.Center.Y}");
    }

    private async Task ClickImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var found = await HitsUntil(step, token);

        // A step that asked to look again waits a moment and confirms the picture is still there: a
        // button that flashed past, or a screen that has already moved on, is not clicked. The
        // second look is a single look rather than another wait: what is being asked is whether the
        // picture is still there, not whether it turns up again.
        var again = Pace(OptionalNumber(step, "recheckMs", 0));
        if (again > 0)
        {
            await Pause(again, token);
            var hits = Hits(step);
            found = Chosen(hits, step) is { } match
                ? new Found(hits, match)
                : throw new StepFailure("Run.ImageNotFound", Written(step.Rows("image")));
        }

        var at = Scattered(new ScreenPoint(
            found.Match.Center.X + Number(step, "offsetX"),
            found.Match.Center.Y + Number(step, "offsetY")), Spread(step));

        // Where it was found and every place it was found, the same as the step that only looks:
        // a step that clicks something is also a step whose position a later one may want.
        GiveMatch(step, found.Match.Location, found.Match.Size, found.Match.Score);
        GiveEvery(step, HitRecords(found.Hits));

        Aim(at);
        Input(step).Click(Button(step), at.X, at.Y, 1, 0);
        Log(LogLevel.Info, depth, step.Type, "Run.ClickedImage", at.X, at.Y);
    }

    /// <summary>One attempt at finding a reference picture: the hit the step asked for.</summary>
    private ImageMatch? Search(ExecutableStep step) => Chosen(Hits(step), step);

    /// <summary>
    /// Every place the reference picture appears in the search areas, in screen coordinates and in
    /// reading order: down the screen first, then across. That is the order a person counts them in
    /// when looking at a screenshot, which is what "the third one" has to mean to be useful.
    /// Which of the pictures the step listed it goes with is the step's own choice: the first one
    /// that turns up, whichever scored closest, or all of them at once. The hits that come back are
    /// the hits of the one picture the step went with, and "the third one" counts inside it.
    /// </summary>
    private List<ImageMatch> Hits(ExecutableStep step)
    {
        var query = SearchQuery(step);
        var areas = SearchAreas(step);
        var wanted = References(step);

        // One picture, looked for in every area the step named: the places it turned up, in the
        // order this step counts them in.
        List<ImageMatch> Look(int number)
        {
            var hits = new List<ImageMatch>();
            foreach (var (area, origin) in areas)
            {
                foreach (var found in _devices.Vision.FindAll(area, wanted[number].Frame, query))
                {
                    hits.Add(found with
                    {
                        Location = new ScreenPoint(
                            found.Location.X + origin.X, found.Location.Y + origin.Y),
                    });
                }
            }

            Sorted(hits, step);
            return hits;
        }

        // What the window is shown: the picture the step went with, or the first one it listed when
        // it went with none of them. Handing over nothing at all would leave out what was being
        // looked for and where.
        void SawPicture(int number, IReadOnlyList<ImageMatch> hits, bool found)
            => SawSearch(step, LookKind.Template, areas,
                [.. hits.Select(hit => (hit, (string?)null))],
                found ? WentWith(hits, step) : 0, wanted[number].Name, wanted[number].Frame,
                query.ConfidencePercent / 100, number + 1, wanted.Count);

        switch (WhichOfThePictures(step))
        {
            case "every":
                {
                    // The whole set has to be there. The step goes with the picture it listed
                    // first, which is the one whose hits are handed over.
                    var looked = new List<ImageMatch>[wanted.Count];
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        looked[number] = Look(number);
                        if (looked[number].Count == 0)
                        {
                            SawPicture(0, [], found: false);
                            return [];
                        }
                    }

                    SawPicture(0, looked[0], found: true);
                    return looked[0];
                }

            case "best":
                {
                    // Every picture is looked at, and the one whose closest match scored highest
                    // is the one the step goes with.
                    var looked = new List<ImageMatch>[wanted.Count];
                    var winner = -1;
                    var surest = double.NegativeInfinity;
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        looked[number] = Look(number);
                        var score = looked[number].Count == 0
                            ? double.NegativeInfinity
                            : looked[number].Max(hit => hit.Score);
                        if (score > surest)
                        {
                            surest = score;
                            winner = number;
                        }
                    }

                    if (winner < 0)
                    {
                        SawPicture(0, [], found: false);
                        return [];
                    }

                    SawPicture(winner, looked[winner], found: true);
                    return looked[winner];
                }

            default:
                {
                    // The first picture that turns up is the one the step goes with, and the rest
                    // of the list is not looked at at all.
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        var hits = Look(number);
                        if (hits.Count == 0)
                        {
                            continue;
                        }

                        SawPicture(number, hits, found: true);
                        return hits;
                    }

                    SawPicture(0, [], found: false);
                    return [];
                }
        }
    }

    /// <summary>
    /// Which of the pictures a step listed it goes with, as written in its field. Anything that
    /// does not read as one of the other two is the first that turns up, which is what a step
    /// written before this field existed asks for and what almost every step wants.
    /// </summary>
    private static string WhichOfThePictures(ExecutableStep step)
    {
        var written = step.Text("imageSet").Trim().ToLowerInvariant();
        return written.Length == 0 ? "first" : written;
    }

    /// <summary>
    /// How a step asked for a picture to be looked for: which way, how sure, what to leave out of
    /// the comparing, and how many hits it wants.
    /// </summary>
    private VisionQuery SearchQuery(ExecutableStep step)
    {
        var confidence = Number(step, "confidence");
        if (confidence <= 0)
        {
            confidence = 90;
        }

        var features = Number(step, "minFeatures");
        var skip = Read(step.Text("ignoreColor")).AsText().Trim();

        return new VisionQuery(
            AlgorithmOf(step.Text("algorithm")),
            confidence,
            features > 0 ? features : 6,
            skip.Length == 0 ? null : PixelColor.Parse(skip),
            Wanted(step));
    }

    /// <summary>
    /// How a step asked for a picture to be looked for, as written in its field. One field answers
    /// both "which way" and "comparing what", because those were never two questions.
    /// </summary>
    private static MatchAlgorithm AlgorithmOf(string written) => written.Trim().ToLowerInvariant()
        switch
    {
        "correlated" => MatchAlgorithm.Correlated,
        "difference" => MatchAlgorithm.Difference,
        "feature" => MatchAlgorithm.Feature,
        _ => MatchAlgorithm.Normed,
    };

    /// <summary>What order a step asked its hits to be counted in, as written in its field.</summary>
    private static MatchOrder OrderOf(ExecutableStep step) => step.Text("orderBy").Trim()
        .ToLowerInvariant() switch
    {
        "score" => MatchOrder.Score,
        "area" => MatchOrder.Area,
        "random" => MatchOrder.Random,
        _ => MatchOrder.Reading,
    };

    /// <summary>
    /// The hits in the order the step wants to count them in, which is what "the third one" means.
    /// Reading order is how a person counts them; by score is for a macro that wants the surest one
    /// wherever it is on the screen; by area is for one that wants the whole of something rather
    /// than a piece of it; shuffled is for one that must not always take the same one.
    /// </summary>
    private static void Sorted(List<ImageMatch> hits, ExecutableStep step)
    {
        switch (OrderOf(step))
        {
            case MatchOrder.Score:
                hits.Sort((left, right) => right.Score.CompareTo(left.Score));
                break;

            case MatchOrder.Area:
                hits.Sort((left, right) => Box(right).CompareTo(Box(left)));
                break;

            case MatchOrder.Random:
                Shuffle(hits);
                break;

            default:
                hits.Sort(Reading);
                break;
        }
    }

    /// <summary>The same for what was read off the screen: a reading of writing is a hit as well.</summary>
    private static void Sorted(List<TextSpan> spans, ExecutableStep step)
    {
        switch (OrderOf(step))
        {
            case MatchOrder.Score:
                spans.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));
                break;

            case MatchOrder.Area:
                spans.Sort((left, right) => Box(right.Size).CompareTo(Box(left.Size)));
                break;

            case MatchOrder.Random:
                Shuffle(spans);
                break;

            default:
                // Reading order is the order the readings came back in: the reading model hands
                // them over in the order they appear on the screen, and re-sorting them by position
                // would break up a line whose words sit at slightly different heights.
                break;
        }
    }

    /// <summary>
    /// How much room a hit takes up, which is what "the biggest one" counts. An area of nothing is
    /// possible: a colour hit is one pixel and a reading of no characters has no width.
    /// </summary>
    private static long Box(ImageMatch hit) => Box(hit.Size);

    private static long Box(ScreenSize size) => (long)size.Width * size.Height;

    /// <summary>Shuffles in place, so a macro that takes the first of several does not always take the same one.</summary>
    private static void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = Random.Shared.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }

    /// <summary>The hits a wait ended on: the whole set and the one the step asked for.</summary>
    private sealed record Found(List<ImageMatch> Hits, ImageMatch Match);

    /// <summary>
    /// The hits, once there are enough of them. How long that may take is the step's own timeout
    /// and nothing else: the wait ends where the clock does, and the step it belongs to is what
    /// fails, retries or gives up from there.
    /// </summary>
    private async Task<Found> HitsUntil(ExecutableStep step, CancellationToken token)
    {
        var interval = LookBeat(step);

        Found? Enough()
        {
            var hits = Hits(step);
            return Chosen(hits, step) is { } match ? new Found(hits, match) : null;
        }

        return await WaitForValueAsync(Enough, interval, token);
    }

    /// <summary>How many hits to look for: the one the step asked for, or the whole list.</summary>
    /// <summary>
    /// How many hits a search has to collect: all it can hold when the step keeps the whole list,
    /// and only as far as the hit it named otherwise. Asking for the list is what makes collecting
    /// it worth anything, and one step hands that over by ticking the box rather than by a switch of
    /// its own.
    /// </summary>
    private int Wanted(ExecutableStep step) => Wants(step, "every")
        ? MatchLimit
        : Index(step, "matchIndex", 1);

    /// <summary>The hit a step wants, counted from the top left, or null when there are not that many.</summary>
    private ImageMatch? Chosen(IReadOnlyList<ImageMatch> hits, ExecutableStep step)
    {
        var index = Index(step, "matchIndex", 1);
        return index <= hits.Count ? hits[index - 1] : null;
    }

    /// <summary>
    /// Which of the hits a step went with, counted from one, or zero when there are not that many
    /// to choose from.
    /// </summary>
    private int WentWith(IReadOnlyList<ImageMatch> hits, ExecutableStep step)
    {
        var index = Index(step, "matchIndex", 1);
        return index <= hits.Count ? index : 0;
    }

    /// <summary>
    /// Which hit a step means, counted from one. Anything that does not read as a number from one
    /// onwards falls back to the first, so a half-written step aims at something rather than at
    /// nothing.
    /// </summary>
    private int Index(ExecutableStep step, string name, int fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0 ? fallback : Math.Clamp(Number(step, name), 1, MatchLimit);
    }

    /// <summary>Reading order, the order the hits are counted in.</summary>
    private static int Reading(ImageMatch left, ImageMatch right)
        => left.Location.Y != right.Location.Y
            ? left.Location.Y.CompareTo(right.Location.Y)
            : left.Location.X.CompareTo(right.Location.X);

    /// <summary>The most hits one step collects, so a flat colour cannot fill memory.</summary>
    private const int MatchLimit = 200;

    /// <summary>
    /// The pictures a step looks for, in the order it listed them. A picture the step did not fill
    /// in is left out rather than looked for; a step that lists none is a step that cannot look for
    /// anything, and says so.
    /// </summary>
    private IReadOnlyList<WantedPicture> References(ExecutableStep step)
    {
        var wanted = new List<WantedPicture>();
        foreach (var row in step.Rows("image"))
        {
            var written = row.TryGetValue("image", out var text) ? text.Trim() : string.Empty;
            if (written.Length > 0)
            {
                wanted.Add(new WantedPicture(written, Picture(written)));
            }
        }

        return wanted.Count > 0 ? wanted : throw new StepFailure("Run.MissingImage", string.Empty);
    }

    /// <summary>One reference picture of a step: how the step wrote it down, and the picture itself.</summary>
    private sealed record WantedPicture(string Name, ImageFrame Frame);

    /// <summary>
    /// The one reference picture a step names, for the steps that hold a single one rather than a
    /// list: something an earlier Capture saved, or a file on disk.
    /// </summary>
    private ImageFrame Reference(ExecutableStep step)
    {
        var text = step.Text("image").Trim();
        if (text.Length == 0)
        {
            throw new StepFailure("Run.MissingImage", string.Empty);
        }

        return Picture(text);
    }

    /// <summary>
    /// A picture the step wrote down: what an earlier Capture saved, or a file on disk. The picture
    /// is kept beside the time the file was last written, so a macro that looks for the same picture
    /// again — in a loop, or every time it polls while it waits — uses the one it already has
    /// instead of reading and decoding the file again.
    /// </summary>
    private ImageFrame Picture(string text)
    {
        // A picture an earlier Capture saved, named with or without the dollar sign so it reads
        // the same as every other field a variable can go in.
        var named = text.StartsWith('$') ? text[1..].Trim() : text;
        if (_images.TryGetValue(named, out var captured))
        {
            return captured;
        }

        // Otherwise it is a file, either written out or held in a variable.
        var path = Read(text).AsText();
        var written = Written(path);
        if (written is { } stamp
            && _references.TryGetValue(path, out var kept)
            && kept.Written == stamp)
        {
            return kept.Frame;
        }

        var picture = _devices.Vision.Load(path) ?? throw new StepFailure("Run.MissingImage", text);
        if (written is not null)
        {
            _references[path] = (written.Value, picture);
        }

        return picture;
    }

    /// <summary>
    /// When a file was last written, or null when it cannot be asked — a picture held in a
    /// variable, or a path this run has no business reading. Nothing is kept for those.
    /// </summary>
    private static DateTime? Written(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The places to search: the whole screen unless the step names regions, each of which may be
    /// counted from a window's corner when the step says so. One row of the step's region is either
    /// a rectangle written out — which is what the region picker fills in — or the name of a
    /// variable, which is how a rectangle or a picture taken earlier gets to be searched.
    /// </summary>
    private IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> SearchAreas(ExecutableStep step)
    {
        var rows = step.Rows("region");
        if (rows.Count == 0)
        {
            // With nowhere written down, the whole of whatever the step is about: the window its
            // numbers are counted from when it names one, and the desktop otherwise.
            var anchor = AnchorOf(step);
            if (anchor.Window is { } window)
            {
                var whole = Shot(step, window.Location.X, window.Location.Y, window.Size.Width,
                    window.Size.Height, window);
                return [(whole.Frame, whole.Origin)];
            }

            var desk = _devices.Screen.Desktop.Bounds;
            var desktop = Shot(step, desk.Left, desk.Top, desk.Width, desk.Height);
            return [(desktop.Frame, desktop.Origin)];
        }

        var areas = new List<(ImageFrame, ScreenPoint)>();
        foreach (var row in rows)
        {
            areas.AddRange(Areas(step, row));
        }

        if (areas.Count == 0)
        {
            throw new StepFailure("Run.BadRegion", Written(rows));
        }

        return areas;
    }

    /// <summary>What the regions of a step say, for a failure that has to show what it could not read.</summary>
    private static string Written(IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
        => string.Join("; ", rows.Select(row => string.Join(',', row.Values)));

    /// <summary>
    /// What one region of a step comes to. A rectangle written out says where it is; a region that
    /// names something looks the name up, which is how a picture an earlier Capture took is
    /// searched — the screen is grabbed once and looked at as often as the macro likes — and how a
    /// rectangle held in a variable gets to be used.
    /// </summary>
    private IEnumerable<(ImageFrame Frame, ScreenPoint Origin)> Areas(ExecutableStep step,
        IReadOnlyDictionary<string, string> row)
    {
        if (!row.TryGetValue("text", out var written))
        {
            yield return Rectangle(step, row);
            yield break;
        }

        // A picture taken earlier is looked up by the name the step wrote rather than through the
        // variables: what a variable holds is a note about the picture, and it is the picture
        // itself that is searched.
        var raw = written.Trim();
        var name = raw.StartsWith('$') ? raw[1..].Trim() : raw;
        if (_images.TryGetValue(name, out var picture))
        {
            yield return (picture, new ScreenPoint(0, 0));
            yield break;
        }

        // A record is one rectangle with a name for each of its sides, which is what a hit hands
        // over: naming that record here is how "look again at what you just found" is written
        // without the macro spelling the four numbers out again.
        if (Read(raw) is { IsRecord: true } record && Held(step, record) is { } held)
        {
            yield return held;
            yield break;
        }

        // Anything else is a rectangle, written out or held in a variable. It is only interpolated
        // rather than read as a whole expression: the commas of a written rectangle would stop an
        // expression at its first number.
        var named = Interpolate(raw).Trim();

        // Several rectangles are separated by a semicolon or a line break, so a variable holding a
        // list of them goes on working the way it always did.
        foreach (var rectangle in named.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Written(step, rectangle);
        }
    }

    /// <summary>
    /// The rectangle a record stands for: its four sides by name, or the one written rectangle an
    /// action that hands its box over as text puts in it. Nothing when the record is neither, so a
    /// record that is not a rectangle is reported as a region nobody could read rather than as a
    /// failure of its own.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin)? Held(ExecutableStep step, Value record)
    {
        if (record.Field("left") is { } left && record.Field("top") is { } top
            && record.Field("width") is { } width && record.Field("height") is { } height)
        {
            return Capture(step, Whole(left), Whole(top), Whole(width), Whole(height));
        }

        var box = record.Field("box")?.AsText().Trim() ?? string.Empty;
        return box.Length > 0 ? Written(step, box) : null;
    }

    /// <summary>One side of a rectangle held in a record, in whole screen pixels.</summary>
    private static int Whole(Value side) => (int)Math.Round(side.AsNumber());

    /// <summary>One region written as four numbers, captured where on the screen it says.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Rectangle(ExecutableStep step,
        IReadOnlyDictionary<string, string> row)
    {
        var x = Cell(row, "x");
        var y = Cell(row, "y");
        var width = Cell(row, "width");
        var height = Cell(row, "height");
        if (width <= 0 || height <= 0)
        {
            throw new StepFailure("Run.BadRegion", Written([row]));
        }

        return Capture(step, x, y, width, height);
    }

    /// <summary>What a row of a list parameter says in one column, or nothing when it has none.</summary>
    private static string Column(IReadOnlyDictionary<string, string> row, string column)
        => row.TryGetValue(column, out var written) ? written : string.Empty;

    /// <summary>One number of a row of a list parameter, as its field was filled in.</summary>
    private int Cell(IReadOnlyDictionary<string, string> row, string column)
        => (int)Math.Round(Read(Column(row, column)).AsNumber());

    /// <summary>
    /// One number of a row that may be left empty, where empty means the run's own value: a row
    /// only has to spell out the hold or the gap that is not the one everywhere else.
    /// </summary>
    private int Cell(IReadOnlyDictionary<string, string> row, string column, int fallback)
        => Column(row, column).Trim().Length > 0
            ? (int)Math.Round(Read(Column(row, column)).AsNumber())
            : fallback;

    /// <summary>One written rectangle, such as one a variable holds.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Written(ExecutableStep step, string rectangle)
    {
        var parts = rectangle.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            throw new StepFailure("Run.BadRegion", rectangle);
        }

        return Capture(step, x, y, width, height);
    }

    /// <summary>
    /// The picture taken of one rectangle. A window-anchored rectangle is counted from that
    /// window's corner, and the origin handed back is the one on screen, so the positions read out
    /// of the picture are pixels the rest of the macro can click on.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin) Capture(ExecutableStep step, int x, int y,
        int width, int height)
    {
        var anchor = AnchorOf(step);
        var corner = Placed(anchor.Corner, x, y);
        var shot = Shot(step, corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// How big the rectangle a step reads or copies is. A step that leaves one of these numbers out
    /// has not said how much of the screen it wants, and the whole of it is what that means: the
    /// side runs from where the rectangle starts to the edge of the screen, where the zero a number
    /// nobody gave would have left it one pixel wide.
    /// </summary>
    private (int Width, int Height) Extent(ExecutableStep step, ScreenPoint corner)
    {
        var desk = _devices.Screen.Desktop.Bounds;
        return (Reach("width", corner.X, desk.Right), Reach("height", corner.Y, desk.Bottom));

        int Reach(string name, int from, int limit)
            => step.Text(name).Trim().Length == 0
                ? Math.Clamp(limit - from, 1, limit)
                : Math.Max(1, Number(step, name));
    }

    /// <summary>
    /// One picture of the screen, taken the way this machine is set up to take one: a step says what
    /// it is looking at — a window, or a rectangle of the desktop — and whether the pixels come off
    /// the screen or out of the window itself is an answer about the machine rather than about the
    /// step. The window is the one the step's own coordinates are counted from, or the one it names
    /// outright.
    /// </summary>
    private ScreenShot Shot(ExecutableStep step, int x, int y, int width, int height,
        WindowInfo? window = null)
    {
        // Which window this is about to be read out of is asked for before anything is read, so a
        // debugger can put a frame there while the reading is going on rather than after it.
        Reading(window);

        var method = window is null
            ? _devices.Capture.ForScreen()
            : _devices.Capture.ForWindow();

        var source = window?.Handle ?? 0;
        var age = Math.Max(0, Number(step, "frameMaxAgeMs"));
        if (age == 0)
        {
            // Nothing already taken is old enough to do, so the rectangle asked for is the rectangle
            // read: this is the reading a macro has always done, at the cost it has always had.
            var exact = _devices.Screen.Capture(new ScreenCaptureRequest(x, y, width, height,
                method, source));
            Taken(window, exact.Method);
            return exact;
        }

        var key = Reused(source, method);
        var now = Stopwatch.GetTimestamp();
        if (_frames.TryGetValue(key, out var kept)
            && Stopwatch.GetElapsedTime(kept.TakenAt, now).TotalMilliseconds <= age
            && Covers(kept.Frame, kept.Origin, x, y, width, height))
        {
            Taken(window, kept.Method);
            return Cut(kept.Frame, kept.Origin, x, y, width, height, kept.Method);
        }

        // The whole of what the step is reading, so that the next step wanting another part of it
        // is served without reading the screen again: asking for one rectangle costs more than that
        // rectangle, and asking for the whole source is what makes the next look free.
        var whole = Whole(method, window);
        _frames[key] = (whole.Frame, whole.Origin, whole.Method, now);
        Taken(window, whole.Method);
        return Cut(whole.Frame, whole.Origin, x, y, width, height, whole.Method);
    }

    /// <summary>
    /// Which pictures are worth keeping for a second look: one to each source, one to each way of
    /// reading it. Two steps reading the same window the same way see the same picture, and a step
    /// reading it another way gets its own — a window copied off the screen and a window read as the
    /// window itself shows it are not the same pixels. What is keyed on is the way that was asked
    /// for rather than the one that came back, because the next step has to look its picture up
    /// before anything is read: the automatic choice is a question, and this machine answers it the
    /// same way twice in a run.
    /// </summary>
    private static (long Source, CaptureWay Method) Reused(long source, CaptureWay method)
        => (source, method);

    /// <summary>The whole of what a step reads from: the window, or the screen.</summary>
    private ScreenShot Whole(CaptureWay method, WindowInfo? window)
    {
        if (window is null)
        {
            var desk = _devices.Screen.Desktop.Bounds;
            return _devices.Screen.Capture(
                new ScreenCaptureRequest(desk.Left, desk.Top, desk.Width, desk.Height, method));
        }

        return _devices.Screen.Capture(new ScreenCaptureRequest(window.Location.X, window.Location.Y,
            window.Size.Width, window.Size.Height, method, window.Handle));
    }

    /// <summary>True when a picture taken whole covers the rectangle a step is asking for.</summary>
    private static bool Covers(ImageFrame frame, ScreenPoint origin, int x, int y, int width,
        int height)
        => x >= origin.X && y >= origin.Y
           && x + width <= origin.X + frame.Width && y + height <= origin.Y + frame.Height;

    /// <summary>
    /// The part of a picture taken whole that a step asked for. A rectangle reaching past the
    /// picture is cut down to what is there, the way a window read as itself is, and one altogether
    /// outside the picture is nothing to look at.
    /// </summary>
    private static ScreenShot Cut(ImageFrame whole, ScreenPoint origin, int x, int y, int width,
        int height, CaptureWay method)
    {
        if (origin.X == x && origin.Y == y && whole.Width == width && whole.Height == height)
        {
            return new ScreenShot(whole, origin, method);
        }

        var left = Math.Max(0, x - origin.X);
        var top = Math.Max(0, y - origin.Y);
        var right = Math.Min(whole.Width, x - origin.X + width);
        var bottom = Math.Min(whole.Height, y - origin.Y + height);
        if (right <= left || bottom <= top)
        {
            throw new StepFailure("Run.EmptyRegion", $"{x},{y} {width}x{height}");
        }

        var kept = new byte[(long)(right - left) * (bottom - top) * 4];
        for (var row = 0; row < bottom - top; row++)
        {
            Array.Copy(whole.Bgra, ((long)(top + row) * whole.Width + left) * 4, kept,
                (long)row * (right - left) * 4, (right - left) * 4);
        }

        return new ScreenShot(new ImageFrame(right - left, bottom - top, kept),
            new ScreenPoint(origin.X + left, origin.Y + top), method);
    }

    /// <summary>Notes what the picture a step is about to be given was taken of.</summary>
    private void Taken(WindowInfo? window, CaptureWay method)
    {
        _takenWindow = window?.Title ?? string.Empty;
        _takenMethod = method;
    }

    /// <summary>Tells whoever is watching which window the next picture comes out of.</summary>
    private void Reading(WindowInfo? window) => _looks?.Reading(window);

    /// <summary>
    /// Waits out however much of this step's gap is left and notes the look. Every step that reads
    /// the screen comes through here, so a step held to a gap is held to it whichever way it reads.
    /// </summary>
    private async Task Gate(ExecutableStep step, CancellationToken token)
    {
        var left = Until(step);
        if (left > 0)
        {
            await Pause(left, token);
        }

        Looked(step);
    }

    /// <summary>
    /// The same wait for a condition, which is asked for its answer rather than awaited, so there
    /// is nothing to hand back to while the gap runs out.
    /// </summary>
    private void GateNow(ExecutableStep step, CancellationToken token)
    {
        var left = Until(step);
        while (left > 0)
        {
            token.ThrowIfCancellationRequested();
            var nap = Math.Min(left, 20);
            Thread.Sleep(nap);
            left -= nap;
        }

        Looked(step);
    }

    /// <summary>
    /// How long this step still has to wait before it may look: nothing when it has never looked or
    /// leaves no gap at all.
    /// </summary>
    private int Until(ExecutableStep step)
    {
        var interval = Math.Max(0, Number(step, "intervalMs"));
        if (interval <= 0 || !_looked.TryGetValue(Key(step), out var last))
        {
            return 0;
        }

        return Math.Max(0, interval - (int)Stopwatch.GetElapsedTime(last).TotalMilliseconds);
    }

    /// <summary>Notes that a step is looking now, which is what the step after it is measured from.</summary>
    private void Looked(ExecutableStep step) => _looked[Key(step)] = Stopwatch.GetTimestamp();

    private static object Key(ExecutableStep step) => step.Id.Length > 0 ? step.Id : step;

    // --------------------------------------------------------------------- files

    /// <summary>A path a step named, with any <c>$variable</c> already filled in.</summary>
    private string PathOf(ExecutableStep step, string parameter = "path")
        => Read(step.Text(parameter)).AsText();

    /// <summary>
    /// The encoding a step named, or UTF-8 when it says nothing. Older macros have no such field,
    /// and UTF-8 without a mark is what they were written with.
    /// </summary>
    private static string EncodingOf(ExecutableStep step)
    {
        var name = step.Text("encoding").Trim();
        return name.Length == 0 ? TextEncoding.Default : name;
    }

    private static readonly JsonSerializerOptions Writable = new() { WriteIndented = true };

    private void ReadTextFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var text = _devices.Files.ReadText(path, EncodingOf(step));
        var limit = Number(step, "limit");
        var lines = string.Equals(step.Text("storeAs").Trim(), "lines",
            StringComparison.OrdinalIgnoreCase);

        if (!lines && limit <= 0)
        {
            // What a step that says nothing does, unchanged: the file's text, whole and as it is.
            Store(step, "text", Value.FromText(text), depth);
            Log(LogLevel.Info, depth, step.Type, "Run.ReadFile", path, text.Length);
            return;
        }

        var wanted = Lines(text, Flag(step, "skipBlankLines", false), Flag(step, "trim", false));
        if (limit > 0 && wanted.Count > limit)
        {
            wanted = [.. wanted.Take(limit)];
        }

        Store(step, "text", lines
            ? Value.FromList(wanted.Select(Value.FromText))
            : Value.FromText(string.Join(Environment.NewLine, wanted)), depth);
        Log(LogLevel.Info, depth, step.Type, "Run.ReadFileLines", path, wanted.Count);
    }

    /// <summary>
    /// A text cut into lines the way every editor cuts one: all three ways of ending a line count,
    /// and the break at the very end of the file does not leave an empty line behind it.
    /// </summary>
    private static List<string> Lines(string text, bool skipBlank, bool trim)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var kept = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var one = trim ? line.Trim() : line;
            if (skipBlank && one.Length == 0)
            {
                continue;
            }

            kept.Add(one);
        }

        return kept;
    }

    private void WriteTextFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var text = Read(step.Text("text")).AsText();
        var append = string.Equals(step.Text("mode").Trim(), "append", StringComparison.OrdinalIgnoreCase);
        var encoding = EncodingOf(step);
        var newline = step.Text("newline").Trim().ToLowerInvariant();

        if (newline == "end")
        {
            text += Environment.NewLine;
        }
        else if (newline == "start" && append && _devices.Files.HasOpenLine(path, encoding))
        {
            // Adding to a file is how a macro keeps a log or grows a list, and "a line of its own"
            // is the whole point: without this the new text lands on the end of the last line, which
            // is a mistake nobody sees until the file is read back.
            text = Environment.NewLine + text;
        }

        _devices.Files.WriteText(path, text, append, encoding);
        Log(LogLevel.Info, depth, step.Type, append ? "Run.AppendedFile" : "Run.WroteFile", path, text.Length);
    }

    /// <summary>
    /// Adds one line to a log file. It is a step of its own rather than write-text-in-append-mode
    /// because keeping a log is a common enough thing to want: the line break and the time in front
    /// of it are the two things every caller would otherwise have to remember to add.
    /// </summary>
    private void AppendLog(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var encoding = EncodingOf(step);
        var line = Read(step.Text("text")).AsText();
        if (Flag(step, "timestamp", true))
        {
            line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " " + line;
        }

        // The action promises one line, so a file whose last line was never closed — written by
        // something else, or by a run that was interrupted halfway — gets a break of its own first.
        // A file that is not there yet has no open line, so its first line is not pushed down.
        var lead = _devices.Files.HasOpenLine(path, encoding) ? Environment.NewLine : string.Empty;
        _devices.Files.WriteText(path, lead + line + Environment.NewLine, append: true, encoding);
        Log(LogLevel.Info, depth, step.Type, "Run.AppendedFile", path, line.Length);
    }

    private void FileExists(ExecutableStep step, int depth)
    {
        var found = _devices.Files.Exists(PathOf(step));
        Store(step, "exists", Value.FromBool(found), depth);
    }

    private void DeleteFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var recycle = Flag(step, "toRecycleBin", false);
        _devices.Files.Delete(path, recycle);
        Log(LogLevel.Info, depth, step.Type,
            recycle ? "Run.RecycledFile" : "Run.DeletedFile", path);
    }

    private void CopyFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var (to, go) = Landing(step, PathOf(step, "to"));
        if (!go)
        {
            Log(LogLevel.Info, depth, step.Type, "Run.FileLeftAlone", to);
            return;
        }

        _devices.Files.Copy(from, to);
        Log(LogLevel.Info, depth, step.Type, "Run.CopiedFile", from, to);
    }

    private void MoveFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var (to, go) = Landing(step, PathOf(step, "to"));
        if (!go)
        {
            Log(LogLevel.Info, depth, step.Type, "Run.FileLeftAlone", to);
            return;
        }

        _devices.Files.Move(from, to);
        Log(LogLevel.Info, depth, step.Type, "Run.MovedFile", from, to);
    }

    /// <summary>
    /// Where a copy or a move is to land, and what to do about a file already sitting there: put
    /// this one over it, leave the one that is there alone, or give the new one a number of its own
    /// beside it. Whether the name is taken is a question only the machine can answer.
    /// </summary>
    private (string To, bool Go) Landing(ExecutableStep step, string to)
    {
        var choice = step.Text("ifExists").Trim().ToLowerInvariant();
        if (!_devices.Files.Exists(to))
        {
            return (to, true);
        }

        return choice switch
        {
            "skip" => (to, false),
            "unique" => (Beside(to), true),
            _ => (to, true),
        };
    }

    /// <summary>
    /// A free name next to a taken one — report (2).csv, then report (3).csv — which is the way
    /// Windows itself gives a second file a name of its own.
    /// </summary>
    private string Beside(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var number = 2; number < 10_000; number++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({number}){extension}");
            if (!_devices.Files.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new StepFailure("Run.NoFreeName", path);
    }

    private void CreateFolder(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        _devices.Files.CreateFolder(path);
        Log(LogLevel.Info, depth, step.Type, "Run.MadeFolder", path);
    }

    private void DeleteFolder(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var recurse = Flag(step, "recurse", false);

        _devices.Files.DeleteFolder(path, recurse);
        Log(LogLevel.Info, depth, step.Type, "Run.RemovedFolder", path);
    }

    /// <summary>
    /// Works out one piece of a path. A macro that has to say "the file next to this one" or "the
    /// same name with .bak on the end" should not have to spell the whole path out again, and the
    /// two folder answers are the ones that are always in the same place on any machine.
    /// </summary>
    private void PathPart(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var other = Read(step.Text("name")).AsText().Trim();

        var answer = step.Text("operation").Trim().ToLowerInvariant() switch
        {
            "folder" => Path.GetDirectoryName(path) ?? string.Empty,
            "name" => Path.GetFileName(path),
            "basename" => Path.GetFileNameWithoutExtension(path),
            "extension" => Path.GetExtension(path),
            "full" => _devices.Files.Resolve(path),
            "temp" => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            "macros" => _devices.Files.BaseFolder,
            _ => other.Length == 0 ? path : Path.Combine(path, other),
        };

        Store(step, "path", Value.FromText(answer), depth);
    }

    private void UnzipFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var folder = PathOf(step, "folder");
        var overwrite = Flag(step, "overwrite", true);

        _devices.Files.Unzip(from, folder, overwrite);
        Log(LogLevel.Info, depth, step.Type, "Run.Unzipped", from, folder);
    }

    private void ZipFolder(ExecutableStep step, int depth)
    {
        var folder = PathOf(step, "folder");
        var to = PathOf(step, "to");

        _devices.Files.Zip(folder, to);
        Log(LogLevel.Info, depth, step.Type, "Run.Zipped", folder, to);
    }

    private void ListFiles(ExecutableStep step, int depth)
    {
        var folder = PathOf(step, "folder");
        var recurse = Flag(step, "recurse", false);
        // A filter said with a semi-colon is several filters, the way the action this is modelled
        // on reads it: *.txt; *.csv is one question, not two steps.
        var patterns = step.Text("pattern")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var levels = Number(step, "depth");
        var files = _devices.Files.List(folder, patterns, recurse,
            recurse ? (levels <= 0 ? -1 : levels) : 0);

        var since = ChangedSince(step);
        var smallest = Number(step, "minKb") * 1024L;
        var largest = Number(step, "maxKb") * 1024L;
        var kept = files.Where(entry =>
            (since is null || entry.Modified >= since)
            && (smallest <= 0 || entry.Size >= smallest)
            && (largest <= 0 || entry.Size <= largest));

        // A listing a macro loops over is the same listing on every run, so the folder's own order
        // is never the answer: an order is always asked for, and the name settles the ties.
        var ordered = InOrder(kept, step.Text("sortBy").Trim(), Flag(step, "descending", false));
        var root = Flag(step, "relative", false) ? _devices.Files.Resolve(folder) : "";
        var paths = ordered
            .Select(entry => root.Length == 0 ? entry.Path : Path.GetRelativePath(root, entry.Path))
            .ToList();

        Store(step, "files", Value.FromList(paths.Select(Value.FromText)), depth);
        Log(LogLevel.Info, depth, step.Type, "Run.ListedFiles", folder, paths.Count);
    }

    /// <summary>
    /// The earliest a listing reaches back to. Nothing said is every file, which is what a step
    /// that only wants what is in a folder expects.
    /// </summary>
    private DateTimeOffset? ChangedSince(ExecutableStep step)
    {
        var count = Number(step, "withinCount");
        if (count <= 0)
        {
            return null;
        }

        var back = step.Text("withinUnit").Trim().ToLowerInvariant() switch
        {
            "minutes" => TimeSpan.FromMinutes(count),
            "hours" => TimeSpan.FromHours(count),
            _ => TimeSpan.FromDays(count),
        };

        return DateTimeOffset.Now - back;
    }

    /// <summary>
    /// Puts a folder listing in the order asked for, falling back to the name so that two files
    /// agreeing on the key still come out the same way every run.
    /// </summary>
    private static IEnumerable<FileEntry> InOrder(IEnumerable<FileEntry> files, string key,
        bool descending)
    {
        var ordered = key.ToLowerInvariant() switch
        {
            "size" => files.OrderBy(entry => entry.Size),
            "modified" => files.OrderBy(entry => entry.Modified),
            "created" => files.OrderBy(entry => entry.Created),
            "extension" => files.OrderBy(entry => Path.GetExtension(entry.Name),
                StringComparer.OrdinalIgnoreCase),
            _ => files.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        };

        var byName = ordered.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        return descending ? byName.Reverse() : byName;
    }

    private void ReadJson(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var document = Json(path, _devices.Files.ReadText(path, EncodingOf(step)));
        var query = Read(step.Text("query")).AsText().Trim();
        var node = query.Length == 0
            ? document
            : Select(document, query) ?? throw new StepFailure("Run.BadJsonPath", query);

        var value = FromJson(node);
        Store(step, "value", value, depth);
    }

    private void WriteJson(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var query = Read(step.Text("query")).AsText().Trim();
        if (query.Length == 0)
        {
            throw new StepFailure("Run.MissingJsonPath");
        }

        var document = _devices.Files.Exists(path)
            ? Json(path, _devices.Files.ReadText(path, EncodingOf(step)))
            : new JsonObject();

        Assign(document, query, ToJson(Read(step.Text("value"))));

        var text = document.ToJsonString(Writable);
        _devices.Files.WriteText(path, text, false, EncodingOf(step));
        Log(LogLevel.Info, depth, step.Type, "Run.WroteFile", path, text.Length);
    }

    private void ReadCsv(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var rows = DelimitedFile.Read(
            _devices.Files.ReadText(path, EncodingOf(step)),
            Separator(step),
            Flag(step, "skipBlankLines", true),
            Flag(step, "trim", false));

        // The line the step starts at is the line the header is on, when there is one: "start at
        // row 3" is where reading begins, and everything else follows from there.
        var wanted = rows.Skip(Math.Max(1, Number(step, "startRow")) - 1);
        var header = Flag(step, "hasHeader", true) ? wanted.FirstOrDefault() : null;
        var body = header is null ? wanted : wanted.Skip(1);

        var limit = Number(step, "maxRows");
        if (limit > 0)
        {
            body = body.Take(limit);
        }

        // A CSV cell is text: a file that holds 1,234 or 2026-01-31 says nothing about whether that
        // is a number or a date, and guessing would be a macro quietly working on the wrong thing.
        var table = Narrow(step,
            header?.Select(Value.FromText).ToList(),
            body.Select(row => (IReadOnlyList<Value>)[.. row.Select(Value.FromText)]));
        Store(step, "rows", Shaped(step, table, path), depth);

        Log(LogLevel.Info, depth, step.Type, "Run.ReadFile", path, table.Rows.Count);
    }

    private void WriteCsv(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var separator = DelimitedFile.Named(Separator(step))[0];
        var rows = Read(step.Text("rows"));
        if (!rows.IsList)
        {
            throw new StepFailure("Run.NotAList", step.Text("rows"));
        }

        var append = string.Equals(step.Text("mode").Trim(), "append", StringComparison.OrdinalIgnoreCase);
        var table = new List<IReadOnlyList<string>>();

        // Appending to a file that is not there yet is the first line of a log, so the header goes
        // in with it; appending to one that is already there would leave a second header in the
        // middle of the data, which is the one thing nobody wants.
        var header = Header(step, separator);
        if (header.Count > 0 && (!append || !_devices.Files.Exists(path)))
        {
            table.Add(header);
        }

        // Adding by name is for the file somebody else's program keeps: the names in its first line
        // say which column is which, and the names of this step's cells say where each one goes, so
        // a column the file has and this step knows nothing about does not shift the rest along.
        var standing = Flag(step, "align", false) && append && _devices.Files.Exists(path)
            ? Standing(path, separator, step)
            : [];

        table.AddRange(rows.Items.Select(row => standing.Count > 0
            ? Placed(Cells(row), standing, RowNames(row, header))
            : (IReadOnlyList<string>)[.. Cells(row).Select(cell => cell.AsText())]));

        var text = DelimitedFile.Write(table, separator.ToString(), LineEnding(step),
            Flag(step, "quoteAll", false),
            string.Equals(step.Text("emptyCells").Trim(), "quoted", StringComparison.OrdinalIgnoreCase));
        _devices.Files.WriteText(path, text, append, EncodingOf(step));
        Log(LogLevel.Info, depth, step.Type, "Run.WroteFile", path, table.Count);
    }

    /// <summary>
    /// The names the file already has, which is the line a log names its columns on. A file with
    /// nothing in it has no names, and adding to it is then an ordinary add.
    /// </summary>
    private IReadOnlyList<string> Standing(string path, char separator, ExecutableStep step)
    {
        var rows = DelimitedFile.Read(_devices.Files.ReadText(path, EncodingOf(step)),
            separator.ToString(), skipBlankLines: false, trim: false);

        return rows.Count == 0 ? [] : rows[0];
    }

    /// <summary>
    /// One row of a CSV put where the file's own names say it goes. A cell the file has no column
    /// for, and a row with more cells than there are names for them, are both said out loud: the
    /// alternative is a value landing in whatever column happens to be tenth.
    /// </summary>
    private static IReadOnlyList<string> Placed(IReadOnlyList<Value> cells,
        IReadOnlyList<string> standing, IReadOnlyList<string> names)
    {
        if (names.Count < cells.Count)
        {
            throw new StepFailure("Run.AlignNeedsNames",
                cells.Count.ToString(CultureInfo.InvariantCulture));
        }

        var line = new string[standing.Count];
        Array.Fill(line, string.Empty);
        for (var at = 0; at < cells.Count; at++)
        {
            line[ColumnOf(standing, names[at])] = cells[at].AsText();
        }

        return line;
    }

    /// <summary>The reading of a name for a table whose names came back as plain text, as a CSV's do.</summary>
    private static int ColumnOf(IReadOnlyList<string> names, string wanted)
        => ColumnOf([.. names.Select(Value.FromText)], wanted);

    /// <summary>
    /// The names to put above the data: a list variable, or the names written out on one line the
    /// way a row of the file would be. Nothing asked for is no names at all.
    /// </summary>
    private IReadOnlyList<string> Header(ExecutableStep step, char separator)
    {
        var text = step.Text("header").Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var value = Read(text);
        if (value.IsList)
        {
            return [.. value.Items.Select(item => item.AsText())];
        }

        var written = value.AsText();
        return written.Length == 0
            ? []
            : [.. written.Split(separator, StringSplitOptions.TrimEntries)];
    }

    /// <summary>Which characters end a line, as the step asked for them.</summary>
    private static string LineEnding(ExecutableStep step)
        => step.Text("lineEnding").Trim().ToLowerInvariant() switch
        {
            "unix" or "lf" or "\\n" => "\n",
            _ => "\r\n",
        };

    /// <summary>
    /// One table a step read: the names above the columns, when the table has them, and the rows
    /// under them.
    /// </summary>
    private sealed record Table(IReadOnlyList<Value>? Header, IReadOnlyList<IReadOnlyList<Value>> Rows);

    /// <summary>
    /// The part of a table the step asked for: the rows that match, and then the columns it named.
    /// Reading a CSV takes the same words and means the same thing however many rows the file has,
    /// so what a step says about one table it says about them all.
    /// </summary>
    private Table Narrow(ExecutableStep step, IReadOnlyList<Value>? header,
        IEnumerable<IReadOnlyList<Value>> body)
    {
        var match = ColumnOf(step, header, "matchColumn");
        var wanted = Read(step.Text("matchValue"));
        var mode = step.Text("matchMode");

        // Which rows to keep is worked out before the columns are taken off, because the column a
        // row is matched on need not be one of the columns that come back.
        var rows = body
            .Where(row => match < 0 || CaseMatches(Cell(row, match), wanted.AsText(), mode))
            .ToList();

        var keep = Kept(step, header);
        if (keep.Count == 0)
        {
            return new Table(header, rows);
        }

        return new Table(
            header is null ? null : Project(header, keep),
            [.. rows.Select(row => Project(row, keep))]);
    }

    /// <summary>
    /// What the result variable holds. A list of rows is what a table is; a plain list of one
    /// column's values is what a macro walks through or adds up, and making that jump through a
    /// one-cell row first is the difference between reading a column and reading a table. A row is a
    /// record whose keys are the names written above the columns, so a macro reads a cell by the
    /// name it can see in the file rather than by counting along the line — which is how a table
    /// comes to be read out of the wrong column without anything saying so.
    /// </summary>
    private Value Shaped(ExecutableStep step, Table table, string path)
    {
        var width = table.Header?.Count ?? table.Rows.FirstOrDefault()?.Count ?? 0;

        if (string.Equals(step.Text("shape").Trim(), "values", StringComparison.OrdinalIgnoreCase))
        {
            if (width > 1)
            {
                throw new StepFailure("Run.OneColumnOnly", path);
            }

            return Value.FromList([.. table.Rows.Select(row => Cell(row, 0))]);
        }

        var names = ColumnNames(table.Header, width);
        return Value.FromList(
        [
            .. table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    index < row.Count ? row[index] : Value.FromText(string.Empty))))),
        ]);
    }

    /// <summary>
    /// What each cell of a row is called: the name written above it, or — for a file whose first row
    /// is not a header — the letter of its column, which is the same name the "only these columns"
    /// field takes. A column nobody named keeps its letter rather than taking the name of the one
    /// before it, and so does a column that repeats a name already taken: two columns under one name
    /// would put one of them out of reach of a record.
    /// </summary>
    private static IReadOnlyList<string> ColumnNames(IReadOnlyList<Value>? header, int width)
    {
        var names = new List<string>(width);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < width; index++)
        {
            var written = index < (header?.Count ?? 0) ? header![index].AsText().Trim() : string.Empty;
            var name = written.Length > 0 && taken.Add(written) ? written : Letter(index);
            names.Add(name);
            taken.Add(name);
        }

        return names;
    }

    /// <summary>The way a person writes a column place on its own: A, B, ... Z, AA.</summary>
    private static string Letter(int place)
    {
        var text = string.Empty;
        for (var at = place; at >= 0; at = (at / 26) - 1)
        {
            text = (char)('A' + (at % 26)) + text;
        }

        return text;
    }

    /// <summary>The columns a step named, in the order it named them; empty means all of them.</summary>
    private IReadOnlyList<int> Kept(ExecutableStep step, IReadOnlyList<Value>? header)
    {
        var wanted = step.Text("columns").Trim();
        if (wanted.Length == 0)
        {
            return [];
        }

        return
        [
            .. wanted
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => ColumnOf(header, name)),
        ];
    }

    /// <summary>Which column a step named with one of its own fields, or -1 when it named none.</summary>
    private int ColumnOf(ExecutableStep step, IReadOnlyList<Value>? header, string parameter)
    {
        var name = step.Text(parameter).Trim();
        return name.Length == 0 ? -1 : ColumnOf(header, name);
    }

    /// <summary>
    /// Which column a step means: the name written above it when the table has names, or the way a
    /// person writes a column — <c>B</c>, or <c>3</c> for the third one. A name the table does not
    /// have is a step asking for the wrong thing, and says so rather than reading the wrong column.
    /// </summary>
    private static int ColumnOf(IReadOnlyList<Value>? header, string wanted)
    {
        var written = wanted.Trim();
        if (header is not null)
        {
            for (var at = 0; at < header.Count; at++)
            {
                if (string.Equals(header[at].AsText().Trim(), written, StringComparison.OrdinalIgnoreCase))
                {
                    return at;
                }
            }
        }

        if (Letters(written) is { } letter)
        {
            return letter;
        }

        if (int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && number >= 1)
        {
            return number - 1;
        }

        throw new StepFailure("Run.NoSuchColumn", written);
    }

    /// <summary>The place a column letter names, counted from zero, or null when it is not letters.</summary>
    private static int? Letters(string text)
    {
        if (text.Length == 0 || text.Length > 3)
        {
            return null;
        }

        var place = 0;
        foreach (var character in text.ToUpperInvariant())
        {
            if (character is < 'A' or > 'Z')
            {
                return null;
            }

            place = place * 26 + (character - 'A' + 1);
        }

        return place - 1;
    }

    /// <summary>The cell in one column of a row, or an empty one when the row stops before it.</summary>
    private static Value Cell(IReadOnlyList<Value> row, int at)
        => at >= 0 && at < row.Count ? row[at] : Value.FromText(string.Empty);

    /// <summary>One row of a table, with only the named columns left in it and in that order.</summary>
    private static IReadOnlyList<Value> Project(IReadOnlyList<Value> row, IReadOnlyList<int> keep)
        => [.. keep.Select(at => Cell(row, at))];

    /// <summary>
    /// One row of a list as the cells of a line hold them. A row that is not a list is a single
    /// cell, which is what a macro that writes one value means.
    /// </summary>
    private static IReadOnlyList<Value> Cells(Value row) => row.Kind switch
    {
        // A record writes its fields in the order they are written in, which is the order the row
        // was read in: writing back a table read from a file gives the file it came from.
        ValueKind.Record => [.. row.Fields.Select(field => field.Value)],
        ValueKind.List => row.Items,
        _ => [row],
    };

    /// <summary>
    /// What each cell of a row is called on its way into a file. A record names its own cells, which
    /// is what lets a row be put under the file's own columns by name; anything else takes the names
    /// the step wrote out above them.
    /// </summary>
    private static IReadOnlyList<string> RowNames(Value row, IReadOnlyList<string> written)
        => row.IsRecord ? [.. row.Fields.Select(field => field.Name)] : written;

    private void SaveVariables(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var chosen = step.Text("names").Trim();
        var names = chosen.Length > 0
            ? chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [.. Variables.Local.Values.Keys
                .Concat(Variables.Global.Values.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

        var document = new JsonObject();
        foreach (var name in names)
        {
            if (Variables.TryGet(name, out var value))
            {
                document[name] = ToJson(value);
            }
        }

        var text = document.ToJsonString(Writable);
        _devices.Files.WriteText(path, text, false, EncodingOf(step));
        Log(LogLevel.Info, depth, step.Type, "Run.SavedVariables", path, document.Count);
    }

    private void LoadVariables(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var document = Json(path, _devices.Files.ReadText(path, EncodingOf(step)));
        if (document is not JsonObject saved)
        {
            throw new StepFailure("Run.BadJson", path);
        }

        foreach (var (name, node) in saved)
        {
            // Shared variables are only written to when the Variable Center already made one,
            // the same rule the "set variable" step follows.
            var target = Variables.Global.Contains(name) ? VariableTarget.Global : VariableTarget.Local;
            Variables.Set(name, FromJson(node), target);
        }

        Log(LogLevel.Info, depth, step.Type, "Run.LoadedVariables", path, saved.Count);
    }

    /// <summary>
    /// Encodes text as Base64. The text is read the way any other value is and then taken as
    /// UTF-8, which is the encoding every service a Base64 value is handed to expects.
    /// </summary>
    private void EncodeBase64(ExecutableStep step, int depth)
    {
        var text = Read(step.Text("text")).AsText();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        Store(step, "encoded", Value.FromText(encoded), depth);
    }

    /// <summary>
    /// Decodes Base64 back into text. Text that is not Base64 fails the step rather than turning
    /// into a value the macro cannot explain: a macro that decoded nothing should say so.
    /// </summary>
    private void DecodeBase64(ExecutableStep step, int depth)
    {
        var text = Read(step.Text("text")).AsText();
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text.Trim());
        }
        catch (FormatException)
        {
            throw new StepFailure("Run.BadBase64", text);
        }

        Store(step, "decoded", Value.FromText(Encoding.UTF8.GetString(bytes)), depth);
    }

    /// <summary>
    /// Works out the checksum of a piece of text or of a file. A file is walked over a piece at a
    /// time rather than read whole, because the files a checksum is asked about — an installer, a
    /// download, a backup — are exactly the ones that do not fit in memory.
    /// </summary>
    private void HashText(ExecutableStep step, int depth)
    {
        var algorithm = step.Text("algorithm");
        byte[] digest;
        if (string.Equals(step.Text("source").Trim(), "file", StringComparison.OrdinalIgnoreCase))
        {
            var path = PathOf(step);
            using var file = _devices.Files.OpenRead(path);
            digest = ComputeHash(algorithm, file);
        }
        else
        {
            var text = Read(step.Text("text")).AsText();
            digest = ComputeHash(algorithm, new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        Store(step, "digest", Value.FromText(Convert.ToHexString(digest).ToLowerInvariant()), depth);
    }

    /// <summary>
    /// The checksum a named algorithm works out. An unrecognized name falls back to SHA-256 rather
    /// than failing: a step that asked for a checksum should get one the macro can compare.
    /// </summary>
    private static byte[] ComputeHash(string algorithm, Stream bytes) =>
        algorithm.Trim().ToLowerInvariant() switch
        {
            "md5" => MD5.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => SHA256.HashData(bytes),
        };

    /// <summary>Reads a JSON file, reporting a broken one as a failed step.</summary>
    private static JsonNode Json(string path, string text)
    {
        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException error)
        {
            throw new StepFailure("Run.BadJson", $"{path}: {error.Message}");
        }
    }

    /// <summary>Walks a path such as <c>server.name</c> or <c>items[0].id</c>.</summary>
    private static JsonNode? Select(JsonNode? root, string query)
    {
        var node = root;
        foreach (var token in Tokens(query))
        {
            node = Within(node, token);
            if (node is null)
            {
                break;
            }
        }

        return node;
    }

    /// <summary>The node one step along a path, or nothing when there is no such place.</summary>
    private static JsonNode? Within(JsonNode? node, Part token) => node switch
    {
        JsonArray array when token.IsIndex && token.Index >= 0 && token.Index < array.Count
            => array[token.Index],
        JsonObject parent when !token.IsIndex
            => parent.TryGetPropertyValue(token.Name, out var child) ? child : null,
        _ => null,
    };

    /// <summary>One step of a path: a name, or a place in a list.</summary>
    private readonly record struct Part(string Name, int Index, bool IsIndex);

    /// <summary>
    /// A path split into its steps: names separated by dots, and a place in a list written between
    /// brackets — by number, counting from zero, or by name in quotes for a name that has a dot in
    /// it. Reading and writing share this, so a path that can be read can also be written.
    /// </summary>
    private static List<Part> Tokens(string query)
    {
        var tokens = new List<Part>();
        var text = query.Trim();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '.')
            {
                index++;
                continue;
            }

            if (text[index] == '[')
            {
                var close = text.IndexOf(']', index);
                if (close < 0)
                {
                    throw new StepFailure("Run.BadJsonPath", query);
                }

                tokens.Add(Token(text[(index + 1)..close].Trim().Trim('\'', '"'), query));
                index = close + 1;
                continue;
            }

            var end = index;
            while (end < text.Length && text[end] != '.' && text[end] != '[')
            {
                end++;
            }

            tokens.Add(Token(text[index..end], query));
            index = end;
        }

        return tokens;
    }

    /// <summary>One step, read from the words a path writes it with.</summary>
    private static Part Token(string written, string query)
    {
        var trimmed = written.Trim();
        if (trimmed.Length == 0)
        {
            throw new StepFailure("Run.BadJsonPath", query);
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
            ? new Part(string.Empty, position, true)
            : new Part(trimmed, 0, false);
    }

    /// <summary>
    /// Sets a value at a path, making the objects along the way. The path is read exactly the way
    /// <see cref="Select"/> reads one, so anything a macro can read it can also write: a path that
    /// names a place inside a list — the third item's price — is the everyday case, and refusing it
    /// would leave a file that can be read and not changed.
    /// </summary>
    private static void Assign(JsonNode root, string query, JsonNode? value)
    {
        var path = Tokens(query);
        if (path.Count == 0)
        {
            throw new StepFailure("Run.BadJsonPath", query);
        }

        var node = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            node = Descend(node, path[index], query);
        }

        var last = path[^1];
        switch (node)
        {
            case JsonObject parent when !last.IsIndex:
                parent[last.Name] = value;
                return;
            case JsonArray list when last.IsIndex && last.Index >= 0 && last.Index < list.Count:
                list[last.Index] = value;
                return;
            default:
                throw new StepFailure("Run.BadJsonPath", query);
        }
    }

    /// <summary>
    /// One step along a path, on the way to the place being written. An object that is not there
    /// yet is made, because "set this value" is how a macro writes a setting a file does not have;
    /// a place in a list is never made, because the place only means something inside a list that
    /// is already there and as long as it is.
    /// </summary>
    private static JsonNode Descend(JsonNode node, Part token, string query) => node switch
    {
        JsonObject parent when !token.IsIndex => parent[token.Name] is { } child
            ? child
            : parent[token.Name] = new JsonObject(),
        JsonArray list when token.IsIndex && token.Index >= 0 && token.Index < list.Count
            => list[token.Index] ?? throw new StepFailure("Run.BadJsonPath", query),
        _ => throw new StepFailure("Run.BadJsonPath", query),
    };

    // A document and the values a macro works with are the same shape, and the one place that
    // knows how the two are written as each other is JsonValues, so a record read out of a file is
    // the record that went into it.
    private static JsonNode? ToJson(Value value) => JsonValues.Node(value);

    private static Value FromJson(JsonNode? node) => JsonValues.FromJson(node);

    /// <summary>The character between two CSV cells.</summary>
    /// <summary>
    /// What the step said separates its cells: the character written beside the picker when there
    /// is one, and otherwise the one that was picked — which may be <c>auto</c>, and then the file
    /// itself decides.
    /// </summary>
    private static string Separator(ExecutableStep step)
        => step.Text("separatorText").Trim().Length > 0
            ? step.Text("separatorText")
            : step.Text("separator");

    // ------------------------------------------------------------------ clipboard

    /// <summary>Puts text on the clipboard, the same as copying it.</summary>
    private void ClipboardWrite(ExecutableStep step, int depth)
    {
        var text = Read(step.Text("text")).AsText();
        _devices.Clipboard.WriteText(text);
        Log(LogLevel.Info, depth, step.Type, "Run.CopiedToClipboard", text.Length);
    }

    /// <summary>Reads the clipboard into a variable.</summary>
    private void ClipboardRead(ExecutableStep step, int depth)
    {
        var text = _devices.Clipboard.ReadText();
        Store(step, "text", Value.FromText(text), depth);
        Log(LogLevel.Info, depth, step.Type, "Run.ReadClipboard", text.Length);
    }

    /// <summary>
    /// Takes the picture on the clipboard into an image variable, so a later step can look for it
    /// on screen. There is no rectangle to write down with it: a picture on the clipboard came from
    /// somewhere else, and nothing says where on this screen it might be.
    /// </summary>
    private void ClipboardImageRead(ExecutableStep step, int depth)
    {
        var picture = _devices.Clipboard.ReadImage();
        var name = step.Return("image");

        if (picture is null || picture.IsEmpty)
        {
            // A miss clears the variable outright, the same habit the find-image steps have: a
            // step inside a loop must never read the picture the previous pass left behind.
            if (name.Length > 0)
            {
                _images.Remove(name);
            }

            Give(step, "image", Value.FromText(string.Empty));
            Log(LogLevel.Warn, depth, step.Type, "Run.NoClipboardImage");
            return;
        }

        if (name.Length > 0)
        {
            _images[name] = picture;
        }

        Give(step, "image", Value.FromText($"<image {picture.Width}x{picture.Height}>"));
        Give(step, "width", Value.FromNumber(picture.Width));
        Give(step, "height", Value.FromNumber(picture.Height));
        Log(LogLevel.Info, depth, step.Type, "Run.ReadClipboardImage",
            $"{picture.Width}x{picture.Height}");
    }

    /// <summary>Puts a picture on the clipboard, from a file or from an image variable.</summary>
    private void ClipboardImageWrite(ExecutableStep step, int depth)
    {
        var picture = Reference(step);
        if (picture.IsEmpty)
        {
            throw new StepFailure("Run.MissingImage", step.Text("image"));
        }

        _devices.Clipboard.WriteImage(picture);
        Log(LogLevel.Info, depth, step.Type, "Run.CopiedImageToClipboard",
            $"{picture.Width}x{picture.Height}");
    }

    /// <summary>Takes the paths of the files on the clipboard into a list.</summary>
    private void ClipboardFilesRead(ExecutableStep step, int depth)
    {
        var files = _devices.Clipboard.ReadFiles();

        Store(step, "files", Value.FromList(files.Select(Value.FromText)), depth);
        Log(LogLevel.Info, depth, step.Type, "Run.ReadClipboardFiles", files.Count);
    }

    /// <summary>
    /// Puts files on the clipboard. A relative path is taken from the macros folder, and every
    /// file is checked to be there: the paste happens in another program, where a path that points
    /// nowhere is refused in a way the macro never gets to see.
    /// </summary>
    private void ClipboardFilesWrite(ExecutableStep step, int depth)
    {
        var value = Read(step.Text("files"));
        var paths = new List<string>();
        foreach (var item in value.IsList ? value.Items : [value])
        {
            var written = item.AsText().Trim();
            if (written.Length == 0)
            {
                continue;
            }

            var full = _devices.Files.Resolve(written);
            if (!_devices.Files.Exists(full))
            {
                throw new StepFailure("Run.FileNotFound", written);
            }

            paths.Add(full);
        }

        if (paths.Count == 0)
        {
            throw new StepFailure("Run.MissingPath");
        }

        _devices.Clipboard.WriteFiles(paths);
        Log(LogLevel.Info, depth, step.Type, "Run.CopiedFilesToClipboard", paths.Count);
    }

    /// <summary>Waits for the clipboard to change, then keeps whatever landed on it.</summary>
    private async Task ClipboardWait(ExecutableStep step, int depth, CancellationToken token)
    {
        await AwaitClipboard(step, _devices.Clipboard.ChangeCount, token);

        var text = _devices.Clipboard.ReadText();
        Give(step, "text", Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, "Run.ClipboardChanged", text.Length);
    }

    /// <summary>Presses the copy shortcut, then waits for what it put on the clipboard.</summary>
    private async Task ClipboardCopy(ExecutableStep step, int depth, CancellationToken token)
    {
        var before = _devices.Clipboard.ChangeCount;
        Input(step).Hotkey(Keys(step), 50);
        await AwaitClipboard(step, before, token);

        var text = _devices.Clipboard.ReadText();
        Give(step, "text", Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, "Run.CopiedFromWindow", text.Length);
    }

    /// <summary>Puts text on the clipboard when one is given, then presses the paste shortcut.</summary>
    private async Task ClipboardPaste(ExecutableStep step, int depth, CancellationToken token)
    {
        var given = step.Text("text").Trim().Length > 0;
        if (given)
        {
            _devices.Clipboard.WriteText(Read(step.Text("text")).AsText());
            await Pause(40, token);
        }

        Input(step).Hotkey(Keys(step), 50);
        Log(LogLevel.Info, depth, step.Type, given ? "Run.PastedText" : "Run.Pasted");
    }

    /// <summary>
    /// Waits for the clipboard's change counter to move past <paramref name="before"/>, which is
    /// how a macro notices a copy without reading the same text over and over.
    /// </summary>
    private async Task AwaitClipboard(ExecutableStep step, int before, CancellationToken token)
    {
        var timeout = OptionalNumber(step, "timeoutMs", 1500);

        if (!await WaitForFlagAsync(() => _devices.Clipboard.ChangeCount != before, timeout, 30, token))
        {
            throw new StepFailure("Run.ClipboardTimeout");
        }
    }

    // -------------------------------------------------------------------- process

    /// <summary>Starts another program and keeps its process id.</summary>
    private void StartProcess(ExecutableStep step, int depth)
    {
        var program = Read(step.Text("file")).AsText();
        var arguments = Read(step.Text("arguments")).AsText();
        var folder = Read(step.Text("workingDirectory")).AsText();
        var hidden = Flag(step, "hidden", true);
        var runAsAdmin = Flag(step, "runAsAdmin", false);

        var id = _devices.Processes.Start(
            new StartRequest(program, arguments, folder, hidden, runAsAdmin, EnvironmentOf(step)));
        Give(step, "processId", Value.FromNumber(id));
        Log(LogLevel.Info, depth, step.Type, "Run.StartedProgram", program, id);
    }

    /// <summary>
    /// The environment variables a step hands a program, written one <c>NAME=value</c> per line.
    /// Blank lines and lines starting with <c>#</c> are skipped, so a macro can keep a setting it
    /// has switched off next to the one it uses. A line that is not a pair at all is reported
    /// rather than ignored: a program started with half the environment the macro meant to give it
    /// would be harder to explain than a step that says it could not read the list.
    /// </summary>
    private Dictionary<string, string>? EnvironmentOf(ExecutableStep step)
    {
        var text = step.Text("environment");
        if (text.Trim().Length == 0)
        {
            return null;
        }

        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(text))
        {
            var pair = line.Trim();
            if (pair.Length == 0 || pair.StartsWith('#'))
            {
                continue;
            }

            var cut = pair.IndexOf('=');
            if (cut <= 0)
            {
                throw new StepFailure("Run.BadEnvironment", pair);
            }

            wanted[pair[..cut].Trim()] = Interpolate(pair[(cut + 1)..].Trim());
        }

        return wanted.Count == 0 ? null : wanted;
    }

    /// <summary>
    /// What a step feeds the program on its standard input, or nothing at all when the field was
    /// left empty. The text is not trimmed: a program that reads lines wants the line ending the
    /// macro wrote. It goes out as UTF-8, the same encoding this side reads the answer back in.
    /// </summary>
    private string? StandardInputOf(ExecutableStep step)
    {
        var text = step.Text("standardInput");
        return text.Trim().Length == 0 ? null : Interpolate(text);
    }

    /// <summary>
    /// The code page a step says the command prints in, or nothing at all to take this machine's
    /// own, which is what a command line on this machine writes in unless it was told otherwise.
    /// </summary>
    private static string? OutputEncodingOf(ExecutableStep step)
    {
        var chosen = step.Text("outputEncoding").Trim();
        return chosen.Length == 0 ? null : chosen;
    }

    /// <summary>Waits until a program with the given name shows up in the process list.</summary>
    private async Task WaitForProgram(ExecutableStep step, int depth, CancellationToken token)
    {
        var name = Read(step.Text("name")).AsText();
        var timeout = OptionalNumber(step, "timeoutMs", 10000);

        var ids = await WaitForValueAsync(
            () => _devices.Processes.Find(name) is { Count: > 0 } found ? found : null,
            timeout, 100, token);
        if (ids is null)
        {
            throw new StepFailure("Run.ProgramNotFound", name);
        }

        Give(step, "process", Value.FromNumber(ids[0]));
        Log(LogLevel.Info, depth, step.Type, "Run.ProgramFound", name, ids[0]);
    }

    /// <summary>Waits for a program to finish and keeps the exit code it returned.</summary>
    private async Task WaitForExit(ExecutableStep step, int depth, CancellationToken token)
    {
        var id = ProcessId(step);
        var timeout = OptionalNumber(step, "timeoutMs", 60000);

        if (!await WaitForFlagAsync(() => _devices.Processes.HasExited(id), timeout, 100, token))
        {
            throw new StepFailure("Run.ProcessTimeout", id.ToString(CultureInfo.InvariantCulture));
        }

        var code = _devices.Processes.ExitCode(id) ?? 0;
        Give(step, "exitCode", Value.FromNumber(code));
        Log(LogLevel.Info, depth, step.Type, "Run.ProcessExited", id, code);
    }

    /// <summary>Leaves true or false behind, depending on whether the program is running.</summary>
    private void ProgramRunning(ExecutableStep step, int depth)
    {
        var name = Read(step.Text("name")).AsText();
        var running = _devices.Processes.Find(name).Count > 0;
        Store(step, "running", Value.FromBool(running), depth);
    }

    /// <summary>Collects the names of the running programs into a list.</summary>
    private void ListPrograms(ExecutableStep step, int depth)
    {
        var programs = _devices.Processes.List();
        Give(step, "processes", Value.FromList(programs.Select(Value.FromText)));
        Log(LogLevel.Info, depth, step.Type, "Run.ListedPrograms", programs.Count);
    }

    /// <summary>
    /// Reads what is known about one running program into a variable and its parts, the way a
    /// match stores the place it found. The path is what a macro usually wants: it is how a
    /// window's program is started again, or told apart from another program of the same name.
    /// </summary>
    private void ProgramDetails(ExecutableStep step, int depth)
    {
        var target = Read(step.Text("target")).AsText().Trim();
        var details = _devices.Processes.Details(target)
            ?? throw new StepFailure("Run.NoSuchProcess", target);

        // One program is one record, the way one window is: what it is called, what it is, and what
        // it is costing are answers to the same question and travel together.
        Give(step, "program", Value.FromRecord(
        [
            ("info", Value.FromText(details.Path)),
            ("id", Value.FromNumber(details.Id)),
            ("name", Value.FromText(details.Name)),
            ("memoryMb", Value.FromNumber(details.MemoryMb)),
            ("cpuSeconds", Value.FromNumber(details.CpuSeconds)),
        ]));

        Log(LogLevel.Info, depth, step.Type, "Run.ProgramDetails", details.Name, details.Id);
    }

    /// <summary>Closes a program, by name or by process id.</summary>
    private void StopProgram(ExecutableStep step, int depth)
    {
        var target = Read(step.Text("target")).AsText().Trim();
        var force = Flag(step, "force", false);

        var stopped = int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? (_devices.Processes.StopById(id, force) ? 1 : 0)
            : _devices.Processes.StopByName(target, force);

        Give(step, "stopped", Value.FromNumber(stopped));
        Log(LogLevel.Info, depth, step.Type, "Run.StoppedProgram", target, stopped);
    }

    /// <summary>Runs a command line to the end and keeps what it printed.</summary>
    private void RunCommand(ExecutableStep step, int depth)
    {
        var program = Read(step.Text("file")).AsText();
        var arguments = Read(step.Text("arguments")).AsText();
        var folder = Read(step.Text("workingDirectory")).AsText();
        var timeout = OptionalNumber(step, "timeoutMs", 30000);

        // A command that takes its time is easier to trust when what it prints turns up while it is
        // still running, but one that prints thousands of lines would bury the log, so this is
        // asked for rather than assumed.
        var watching = Flag(step, "streamOutput", false);
        if (watching)
        {
            Log(LogLevel.Info, depth, step.Type, "Run.CommandStarted", program);
        }

        var result = _devices.Processes.Run(new CommandRequest(program, arguments, folder, timeout,
            EnvironmentOf(step), StandardInputOf(step),
            watching ? line => Log(LogLevel.Info, depth, step.Type, "Run.CommandOutput", line) : null,
            watching ? line => Log(LogLevel.Info, depth, step.Type, "Run.CommandError", line) : null,
            OutputEncodingOf(step)));

        Store(step, "output", Value.FromText(result.StandardOutput), depth);
        Store(step, "error", Value.FromText(result.StandardError), depth);
        Store(step, "exitCode", Value.FromNumber(result.ExitCode), depth);

        Log(LogLevel.Info, depth, step.Type, "Run.CommandFinished", program, result.ExitCode);
    }

    // --------------------------------------------------------------------- system

    /// <summary>Reads a fact about the machine into a variable.</summary>
    private void SystemInfo(ExecutableStep step, int depth)
    {
        var value = _devices.System.Info(step.Text("field").Trim());
        Store(step, "value", Value.FromText(value), depth);
    }

    /// <summary>
    /// How the desk is laid out, one record to a display, so a macro that has to work on the second
    /// screen can find out where it is instead of having the numbers written into it. The rectangle
    /// is in desktop coordinates, which is what every other coordinate in a macro is written in —
    /// a display to the left of the primary one starts at a negative number, and that is exactly the
    /// number a step aiming at it needs.
    /// </summary>
    private void ListDisplays(ExecutableStep step, int depth)
    {
        var desk = _devices.Screen.Desktop;

        Give(step, "displays", Value.FromList(
        [
            .. desk.Displays.Select(display => Value.FromRecord(
            [
                ("left", Value.FromNumber(display.Bounds.Left)),
                ("top", Value.FromNumber(display.Bounds.Top)),
                ("width", Value.FromNumber(display.Bounds.Width)),
                ("height", Value.FromNumber(display.Bounds.Height)),
                ("right", Value.FromNumber(display.Bounds.Right)),
                ("bottom", Value.FromNumber(display.Bounds.Bottom)),
                ("primary", Value.FromBool(display.Primary)),
                ("box", Value.FromText(
                    $"{display.Bounds.Left},{display.Bounds.Top},{display.Bounds.Width},{display.Bounds.Height}")),
            ])),
        ]));

        Log(LogLevel.Info, depth, step.Type, "Run.ListedDisplays",
            desk.Displays.Count, $"{desk.Bounds.Width}x{desk.Bounds.Height}");
    }

    /// <summary>Reads an environment variable into a variable.</summary>
    private void EnvironmentVariable(ExecutableStep step, int depth)
    {
        var name = Read(step.Text("name")).AsText();
        var value = _devices.System.Environment(name);
        Store(step, "value", Value.FromText(value), depth);
    }

    /// <summary>
    /// Asks the machine to do one of the things the Start menu's power button does. The name is
    /// read strictly rather than falling back to something: a macro that mistyped "shutdown" should
    /// not have its screen locked instead, and one that mistyped "lock" should certainly not be
    /// turning the machine off.
    /// </summary>
    private void Power(ExecutableStep step, int depth)
    {
        var wanted = step.Text("what").Trim().ToLowerInvariant();
        var action = wanted switch
        {
            "" or "lock" => PowerAction.Lock,
            "monitoroff" or "monitor" => PowerAction.MonitorOff,
            "signout" or "logoff" => PowerAction.SignOut,
            "sleep" => PowerAction.Sleep,
            "hibernate" => PowerAction.Hibernate,
            "restart" or "reboot" => PowerAction.Restart,
            "shutdown" => PowerAction.ShutDown,
            "abortshutdown" or "abort" => PowerAction.AbortShutdown,
            _ => throw new StepFailure("Run.UnknownPowerAction", wanted),
        };

        var grace = OptionalNumber(step, "graceSeconds", 0);
        _devices.System.Power(action, grace);
        Log(LogLevel.Info, depth, step.Type, "Run.Power", wanted.Length == 0 ? "lock" : wanted,
            grace);
    }

    /// <summary>
    /// Reads or changes the volume of the speakers Windows is using. Whatever the step asked for,
    /// what it leaves behind is the level the machine ended up at, so a macro that turned the sound
    /// down can put it back the way it found it.
    /// </summary>
    private void Volume(ExecutableStep step, int depth)
    {
        var what = step.Text("what").Trim().ToLowerInvariant();
        switch (what)
        {
            // A step written before this field existed means the same as the editor's own default:
            // read the level and leave the machine alone.
            case "":
            case "get":
                break;

            case "set":
                _devices.System.SetVolume(OptionalNumber(step, "percent", 50));
                break;

            case "up":
            case "down":
                var move = OptionalNumber(step, "stepPercent", 5);
                _devices.System.SetVolume(
                    _devices.System.Volume() + (what == "up" ? move : -move));
                break;

            case "mute":
                _devices.System.SetMuted(true);
                break;

            case "unmute":
                _devices.System.SetMuted(false);
                break;

            case "togglemute":
                _devices.System.SetMuted(!_devices.System.IsMuted());
                break;

            default:
                throw new StepFailure("Run.UnknownVolumeAction", what);
        }

        var level = _devices.System.Volume();
        Give(step, "volume", Value.FromNumber(level));
        Log(LogLevel.Info, depth, step.Type, "Run.Volume", level);

        // Switching the sound off does not change the level, so the level alone would leave a reader
        // wondering whether the macro had done anything at all.
        if (what is "mute" or "unmute" or "togglemute")
        {
            Log(LogLevel.Info, depth, step.Type,
                _devices.System.IsMuted() ? "Run.SoundOff" : "Run.SoundOn");
        }
    }

    /// <summary>
    /// Plays one of the machine's own event sounds, so a macro that has finished, or gone wrong,
    /// can say so where nobody is looking at the screen.
    /// </summary>
    private void PlaySound(ExecutableStep step, int depth)
    {
        var what = step.Text("what").Trim().ToLowerInvariant();
        var kind = what switch
        {
            "" or "default" => SoundKind.Default,
            "information" or "info" => SoundKind.Information,
            "warning" => SoundKind.Warning,
            "error" => SoundKind.Error,
            "question" => SoundKind.Question,
            _ => throw new StepFailure("Run.UnknownSound", what),
        };

        _devices.System.PlaySound(kind);
        Log(LogLevel.Info, depth, step.Type, "Run.PlayedSound",
            what.Length == 0 ? "default" : what);
    }

    /// <summary>
    /// Shows a notification beside the notification area, so a macro that has finished, or gone
    /// wrong, can say so to somebody who is not looking at the window. The words go through the
    /// usual reading, so a macro can put a variable in the title or the message.
    /// </summary>
    private void Notify(ExecutableStep step, int depth)
    {
        var title = Read(step.Text("heading")).AsText();
        var message = Read(step.Text("message")).AsText();
        var what = step.Text("what").Trim().ToLowerInvariant();

        var kind = what switch
        {
            "" or "information" or "info" => NotificationKind.Information,
            "warning" => NotificationKind.Warning,
            "error" => NotificationKind.Error,
            _ => throw new StepFailure("Run.UnknownNotification", what),
        };

        _devices.System.Notify(title, message, kind);
        Log(LogLevel.Info, depth, step.Type, "Run.Notified", what.Length == 0 ? "information" : what);
    }

    /// <summary>
    /// Reads or changes what the window with the focus is typing in. A macro that types Latin keys
    /// while a Chinese layout is in use gets Chinese candidates instead, so switching the window to
    /// its English layout first is often the difference between a macro that works and one that
    /// types 你好.
    /// </summary>
    private void InputMethod(ExecutableStep step, int depth)
    {
        var what = step.Text("what").Trim().ToLowerInvariant();
        Value answer;

        switch (what)
        {
            case "get":
                answer = Value.FromText(_devices.System.InputMethod());
                break;

            case "list":
                answer = Value.FromText(string.Join(';', _devices.System.InputMethods()));
                break;

            case "switch":
                var wanted = Read(step.Text("layout")).AsText().Trim();
                if (wanted.Length == 0)
                {
                    throw new StepFailure("Run.MissingName");
                }

                var switched = _devices.System.SwitchInputMethod(wanted);
                if (switched is null)
                {
                    throw new StepFailure("Run.LayoutNotFound", wanted);
                }

                answer = Value.FromText(switched);
                break;

            default:
                throw new StepFailure("Run.UnknownInputMethodAction", what);
        }

        Store(step, "ime", answer, depth);
    }

    /// <summary>
    /// Reads or changes how bright the screens are. Like the volume, the result is the brightness the
    /// machine was left at rather than the one that was asked for, so a macro can put it back.
    /// </summary>
    private void Brightness(ExecutableStep step, int depth)
    {
        var what = step.Text("what").Trim().ToLowerInvariant();
        switch (what)
        {
            // A step written before this field existed means the same as the editor's own default:
            // read the brightness and leave the screens alone.
            case "":
            case "get":
                break;

            case "set":
                _devices.System.SetBrightness(OptionalNumber(step, "percent", 50));
                break;

            case "up":
            case "down":
                var move = OptionalNumber(step, "stepPercent", 10);
                _devices.System.SetBrightness(
                    _devices.System.Brightness() + (what is "up" ? move : -move));
                break;

            default:
                throw new StepFailure("Run.UnknownBrightnessAction", what);
        }

        var level = _devices.System.Brightness();
        Give(step, "brightness", Value.FromNumber(level));
        Log(LogLevel.Info, depth, step.Type, "Run.Brightness", level);
    }

    /// <summary>The process id a step names, which is usually a variable.</summary>
    private int ProcessId(ExecutableStep step)
    {
        var text = Read(step.Text("id")).AsText().Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            throw new StepFailure("Run.BadProcessId", text);
        }

        return id;
    }

    // --------------------------------------------------------------------- window

    /// <summary>
    /// Hands one of a step's results over under the name the step gave it, and does nothing at all
    /// when the step did not ask for that result. An unticked result is not an empty variable, it is
    /// one this step never makes: a macro that reads it is reading a name no run wrote. What the step
    /// worked out is kept for the debugger either way, under the name the macro keeps it by when
    /// there is one, so a result nobody ticked can still be read after the run.
    /// </summary>
    private string Give(ExecutableStep step, string key, Value value)
    {
        var name = step.Return(key);
        if (name.Length > 0)
        {
            Variables.Set(name, value);
        }

        if (_worked is { } worked)
        {
            var workedOut = new StepResult(key, name, value.AsText());
            var at = worked.FindIndex(result =>
                string.Equals(result.Key, key, StringComparison.OrdinalIgnoreCase));

            // A step that was retried works the same result out again, and the last one is what it
            // came to; a result that has never been worked out before goes on the end.
            if (at < 0)
            {
                worked.Add(workedOut);
            }
            else
            {
                worked[at] = workedOut;
            }
        }

        return name;
    }

    /// <summary>Hands one result over and says so in the log, the way storing a value does.</summary>
    private void Store(ExecutableStep step, string key, Value value, int depth)
    {
        var name = Give(step, key, value);
        if (name.Length > 0)
        {
            Log(LogLevel.Info, depth, step.Type, "Run.Set", name, value.AsText());
        }
    }

    /// <summary>True when the step keeps this result, so working it out is worth anything.</summary>
    private static bool Wants(ExecutableStep step, string key) => step.Return(key).Length > 0;

    /// <summary>
    /// Hands over what a search found as one record: where it was, the four sides of the rectangle
    /// it was found in, how sure the reading was, and the writing that was read where there was any.
    /// All of it about one hit, so it travels together under the names a macro reads with
    /// <c>field($hit, "x")</c> rather than as a variable of its own for each part.
    ///
    /// The rectangle is named by its top-left corner, because that is the corner a search region
    /// counts from: what a hit hands over has to be usable as where to look next without the macro
    /// working out an offset first. The middle is in the record beside it under names of its own,
    /// because that is the point a click or a move aims at.
    /// </summary>
    private void GiveMatch(ExecutableStep step, ScreenPoint corner, ScreenSize size, double score,
        string text = "")
    {
        Give(step, "match", Hit(corner, size, score, text));
        GiveTaken(step);
    }

    /// <summary>
    /// One hit as a record. The fields are the same whatever was found, so a macro that reads the
    /// size of a picture reads the size of a line of writing the same way; a hit that read no
    /// writing — a picture, a colour — simply has no text field.
    /// </summary>
    private static Value Hit(ScreenPoint corner, ScreenSize size, double score, string text = "")
    {
        var centre = Middle(corner, size);
        var fields = new List<(string, Value)>
        {
            ("x", Value.FromNumber(centre.X)),
            ("y", Value.FromNumber(centre.Y)),
            ("left", Value.FromNumber(corner.X)),
            ("top", Value.FromNumber(corner.Y)),
            ("width", Value.FromNumber(size.Width)),
            ("height", Value.FromNumber(size.Height)),
            ("score", Value.FromNumber(score)),
            ("box", Value.FromText($"{corner.X},{corner.Y},{size.Width},{size.Height}")),
        };

        if (text.Length > 0)
        {
            fields.Add(("text", Value.FromText(text)));
        }

        return Value.FromRecord(fields);
    }

    /// <summary>The middle of a rectangle, which is the point an action aims at.</summary>
    private static ScreenPoint Middle(ScreenPoint corner, ScreenSize size)
        => new(corner.X + (size.Width / 2), corner.Y + (size.Height / 2));

    /// <summary>
    /// Empties what a search would have handed over, so a step inside a loop never reads the place
    /// the pass before it found. Nothing found is an empty hit and no hits at all, rather than the
    /// hit of the pass before: an empty list counts as nought, which is what "found nothing" means.
    /// </summary>
    private void GiveNothing(ExecutableStep step)
    {
        Give(step, "match", Value.FromText(string.Empty));
        Give(step, "every", Value.FromList([]));
        Give(step, "window", Value.FromText(string.Empty));
        Give(step, "method", Value.FromText(string.Empty));
    }

    /// <summary>
    /// Hands over which window the picture came from and which way it was taken. That answers a
    /// different question from where the hit was: not "where is it", but "was it looking at the
    /// right thing at all".
    /// </summary>
    private void GiveTaken(ExecutableStep step)
    {
        Give(step, "window", Value.FromText(_takenWindow));
        Give(step, "method", Value.FromText(
            _takenMethod is { } method ? CaptureWayNames.Written(method) : string.Empty));
    }

    /// <summary>
    /// Hands over every hit there was, each a record shaped like the one that was taken, so a macro
    /// can count them, take one apart and loop over them all with the list functions. Only the steps
    /// that asked for them pay for the list.
    /// </summary>
    private void GiveEvery(ExecutableStep step, IEnumerable<Value> hits)
    {
        if (Wants(step, "every"))
        {
            Give(step, "every", Value.FromList(hits));
        }
    }

    /// <summary>Every picture hit as a record, in the order the step counts them in.</summary>
    private static IEnumerable<Value> HitRecords(IReadOnlyList<ImageMatch> hits)
        => hits.Select(hit => Hit(hit.Location, hit.Size, hit.Score));

    /// <summary>Every reading of the screen as a record, keeping what each of them said.</summary>
    private static IEnumerable<Value> HitRecords(IReadOnlyList<TextSpan> hits)
        => hits.Select(hit => Hit(hit.Location, hit.Size, hit.Confidence, hit.Text));

    /// <summary>Leaves true or false behind, depending on whether the window is open.</summary>
    private void WindowExists(ExecutableStep step, int depth)
    {
        var found = Lookup(step) is not null;
        Store(step, "found", Value.FromBool(found), depth);
    }

    /// <summary>Waits until a window the step names appears.</summary>
    private async Task WaitForWindow(ExecutableStep step, int depth, CancellationToken token)
    {
        var timeout = OptionalNumber(step, "timeoutMs", 10000);

        var window = await WaitForValueAsync(() => Lookup(step), timeout, 100, token);
        if (window is null)
        {
            throw new StepFailure("Run.WindowTimeout", WindowText(step));
        }

        Give(step, "title", Value.FromText(window.Title));
        Log(LogLevel.Info, depth, step.Type, "Run.WindowAppeared", window.Title);
    }

    /// <summary>
    /// Collects the open windows into a list, narrowed down to the ones a step names, each window as
    /// a record of its own: its title, where it sits, what the system knows it by, and — when the
    /// step asks for them — the program and the class behind it. Two windows with the same title are
    /// two windows, so each gets a record of its own. A text nobody wrote means every window here,
    /// where it means the window in front to a step that acts on one of them.
    /// </summary>
    private void ListWindows(ExecutableStep step, int depth)
    {
        var windows = Matching(step);

        // The program and the class of a window are read only when the step keeps the list and asks
        // for them, because looking either of them up costs a look at the window. Every entry is
        // then the whole of one window, the same record window.info hands over for a single one; a
        // step that leaves them out gets two empty fields rather than missing ones.
        if (Wants(step, "windows"))
        {
            var details = Flag(step, "details", false);
            Store(step, "windows", Value.FromList(windows.Select(window => WindowRecord(window,
                details ? _devices.Windows.ProcessOf(window.Handle) : string.Empty,
                details ? _devices.Windows.ClassOf(window.Handle) : string.Empty,
                string.Empty))), depth);
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ListedWindows", windows.Count);
    }

    /// <summary>
    /// The windows a listing hands over, in the order the system gives them: the ones the step's
    /// text picks out, or every window there is when it names none.
    /// </summary>
    private IReadOnlyList<WindowInfo> Matching(ExecutableStep step)
    {
        var rule = WindowRuleOf(step, "window", "how");
        return _devices.Windows.Filter(rule.Text, rule.Match, rule.Compare, everyWhenEmpty: true);
    }

    /// <summary>Does something to the window a step names, or fails the step when it is not open.</summary>
    private void Act(ExecutableStep step, int depth, string message, Func<WindowInfo, bool> action)
    {
        var window = Locate(step);

        if (!action(window))
        {
            throw new StepFailure("Run.WindowFailed", window.Title);
        }

        Log(LogLevel.Info, depth, step.Type, message, window.Title);
    }

    /// <summary>
    /// The window a step names, or a failure saying which one could not be found. "Match by" says
    /// what the text is held up against and how; an empty text means whatever window is in front.
    /// </summary>
    private WindowInfo Locate(ExecutableStep step)
        => Lookup(step) ?? throw new StepFailure("Run.WindowNotFound", WindowText(step));

    /// <summary>The same, for the steps that report rather than fail when no window matches.</summary>
    private WindowInfo? Lookup(ExecutableStep step)
    {
        var rule = WindowRuleOf(step, "window", "how");
        return _devices.Windows.Find(rule.Text, rule.Match, rule.Compare);
    }

    /// <summary>
    /// The window a step names, read off the two fields the rule is made of: the text, with the
    /// variables in it filled in, and the one choice that says what the text is held up against and
    /// how. The step families that name a window — a window action, a step that reads the screen
    /// and one that posts its input at a window — say it in these same two fields, so they are read
    /// in this one place.
    /// </summary>
    private (string Text, WindowMatch Match, WindowCompare Compare) WindowRuleOf(ExecutableStep step,
        string textField, string howField)
    {
        var text = Read(step.Text(textField)).AsText().Trim();
        var (match, compare) = WindowHow.Read(step.Text(howField));

        // A pattern the machine cannot read is the macro's problem to see, not a stack trace to
        // puzzle over, so it comes back as a failed step before anything is looked at — the same
        // words whichever step asked.
        try
        {
            WindowHow.Check(text, compare);
        }
        catch (ArgumentException)
        {
            throw new StepFailure("Run.BadPattern", text);
        }

        return (text, match, compare);
    }

    /// <summary>What a step wrote into its window field, with the variables in it resolved.</summary>
    private string WindowText(ExecutableStep step) => Read(step.Text("window")).AsText().Trim();

    /// <summary>
    /// Reads where a window is and how big it is. The rectangle is written the way a search region
    /// is spelled, so it can be handed straight to the vision actions, and each part gets a name of
    /// its own for the arithmetic a macro wants to do with it.
    /// </summary>
    /// <summary>
    /// Where a window sits and what it is, handed over as one record — the same shape a listing
    /// gives each window it finds, so the rectangle goes into a search region as it stands and one
    /// part of it is read by name.
    /// </summary>
    private void WindowBox(ExecutableStep step, int depth)
    {
        var window = Locate(step);
        var box = $"{window.Location.X},{window.Location.Y},{window.Size.Width},{window.Size.Height}";

        if (Wants(step, "window"))
        {
            // The program, the class and the part inside the border cost a look at the window each,
            // so they are worked out only when the record is kept at all.
            var client = _devices.Windows.ClientOrigin(window.Handle);
            var inner = _devices.Windows.ClientSize(window.Handle);
            Give(step, "window", WindowRecord(window,
                _devices.Windows.ProcessOf(window.Handle),
                _devices.Windows.ClassOf(window.Handle),
                $"{client.X},{client.Y},{inner.Width},{inner.Height}"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.WindowBox", window.Title, box);
    }

    /// <summary>
    /// One window as a record: what it is — the title, the program and the class behind it, what the
    /// system knows it by, whether it is shrunk or spread — and where it sits, both as the four
    /// numbers of its rectangle and as the text a search region is spelled with. The part inside
    /// the border goes in when the caller has read it; a listing leaves it out, because reading it
    /// costs a look at every window found.
    /// </summary>
    private static Value WindowRecord(WindowInfo window, string program, string kind, string clientBox)
    {
        var left = window.Location.X;
        var top = window.Location.Y;
        var width = window.Size.Width;
        var height = window.Size.Height;

        List<(string Name, Value Value)> fields =
        [
            ("title", Value.FromText(window.Title)),
            ("process", Value.FromText(program)),
            ("class", Value.FromText(kind)),
            ("handle", Value.FromNumber(window.Handle)),
            ("minimized", Value.FromBool(window.Minimized)),
            ("maximized", Value.FromBool(window.Maximized)),
            ("left", Value.FromNumber(left)),
            ("top", Value.FromNumber(top)),
            ("width", Value.FromNumber(width)),
            ("height", Value.FromNumber(height)),
            ("box", Value.FromText($"{left},{top},{width},{height}")),
        ];

        if (clientBox.Length > 0)
        {
            fields.Add(("clientBox", Value.FromText(clientBox)));
        }

        return Value.FromRecord(fields);
    }

    // ---------------------------------------------------------------------- ocr

    /// <summary>
    /// Reads the text in a step's search areas, with the positions in screen coordinates. Several
    /// areas are read one after another and what they hold is handed back together, and the areas
    /// themselves are kept so that a look at this reading can draw where it was taken from.
    /// </summary>
    private IReadOnlyList<TextSpan> ReadSpans(ExecutableStep step)
    {
        var areas = SearchAreas(step);
        _read = areas;

        var spans = new List<TextSpan>();
        foreach (var (area, origin) in areas)
        {
            spans.AddRange(ReadFrame(step, area).Select(span => span with
            {
                Location = new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y),
            }));
        }

        Sorted(spans, step);
        return spans;
    }

    /// <summary>
    /// Reads one picture, with whatever tidying up the step asked for, and hands the writing back
    /// in the coordinates of the picture that was given: one that was made bigger to be read has
    /// its positions made smaller again, so they still mean screen pixels.
    /// </summary>
    private IReadOnlyList<TextSpan> ReadFrame(ExecutableStep step, ImageFrame frame)
    {
        var (prepared, scale) = OcrPreprocess.Apply(Isolated(step, frame), step.Text("preprocess"));
        var readings = _devices.Ocr.Recognize(prepared)
            .Select(span => span with { Text = Corrected(step, span.Text) });
        var spans = Wanted(step, [.. readings]);

        if (scale == 1)
        {
            return spans;
        }

        return [.. spans.Select(span => span with
        {
            Location = new ScreenPoint(Smaller(span.Location.X, scale), Smaller(span.Location.Y, scale)),
            Size = new ScreenSize(Smaller(span.Size.Width, scale), Smaller(span.Size.Height, scale)),
        })];
    }

    /// <summary>A measurement taken from a picture that was made bigger, put back to screen pixels.</summary>
    private static int Smaller(int value, double scale) => (int)Math.Round(value / scale);

    /// <summary>
    /// The picture with everything that is not the writing thrown away, when the step said what
    /// colour the writing is in. Writing drawn with an outline or over a picture is where the model
    /// is handed too much to read; leaving only the writing is what makes it readable.
    /// </summary>
    private ImageFrame Isolated(ExecutableStep step, ImageFrame frame)
    {
        var colour = Read(step.Text("colorFilter")).AsText().Trim();
        return colour.Length == 0
            ? frame
            : OcrPreprocess.ByColour(frame, PixelColor.Parse(colour), Number(step, "colorTolerance"));
    }

    /// <summary>
    /// The reading with the step's own corrections applied, written one to a line as what the model
    /// read = what it says. A model that keeps misreading a stylised font is a mistake the user can
    /// see and write down, and writing it down here is what keeps the macro working.
    /// </summary>
    private static string Corrected(ExecutableStep step, string text)
    {
        var corrections = step.Text("fixText");
        if (text.Length == 0 || corrections.Trim().Length == 0)
        {
            return text;
        }

        var fixedUp = text;
        foreach (var line in corrections.Split('\n'))
        {
            var pair = line.Split('=', 2);
            if (pair.Length != 2 || pair[0].Trim().Length == 0)
            {
                continue;
            }

            fixedUp = fixedUp.Replace(pair[0].Trim(), pair[1].Trim(), StringComparison.Ordinal);
        }

        return fixedUp;
    }

    /// <summary>
    /// The rectangle a reading step reads: where it starts on screen, the picture taken of it, and
    /// what was read off that picture.
    /// </summary>
    private (ScreenPoint Corner, ImageFrame Area, IReadOnlyList<TextSpan> Spans) ReadBox(
        ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        var corner = Placed(anchor.Corner, Number(step, "x"), Number(step, "y"));
        var (width, height) = Extent(step, corner);
        var shot = Shot(step, corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Origin, shot.Frame, ReadFrame(step, shot.Frame));
    }

    /// <summary>
    /// The readings of a step's areas that it would act on, and which of them it would pick,
    /// counted from one inside the readings it was given.
    /// </summary>
    private (List<TextSpan> Hits, int Chosen) Counting(ExecutableStep step,
        IReadOnlyList<TextSpan> readings, string wanted, string mode)
    {
        var hits = new List<TextSpan>();
        var chosen = 0;
        for (var index = 0; index < readings.Count; index++)
        {
            if (!Sure(step, readings[index]) || !ActsOn(step, readings[index].Text, wanted, mode))
            {
                continue;
            }

            hits.Add(readings[index]);
            if (chosen == 0)
            {
                chosen = index + 1;
            }
        }

        return (hits, chosen);
    }

    /// <summary>
    /// Whether a reading is one the step would act on: it says what was asked for, and it has the
    /// shape the step asked for. The shape is what narrows a screen full of the same word down to
    /// the one line a macro wants — a level, an amount, a count — where looking for the word alone
    /// would take the first line that happens to say it.
    /// </summary>
    private bool ActsOn(ExecutableStep step, string text, string wanted, string mode)
    {
        if (!Matches(text, wanted, mode))
        {
            return false;
        }

        var shape = step.Text("expected").Trim();
        if (shape.Length == 0)
        {
            return true;
        }

        try
        {
            return Regex.IsMatch(text, shape, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            // A pattern the machine cannot read is the macro's problem to see, not a stack trace to
            // puzzle over, so it comes back as a failed step.
            throw new StepFailure("Run.BadPattern", shape);
        }
    }

    /// <summary>
    /// What a step keeps of what was read: the writing as it stands, one kind of character out of
    /// it, or what a pattern picks out. Cutting a screen down to the numbers, or to the letters, is
    /// how a screen full of labels and a screen full of codes are handled by the same kind of step.
    /// </summary>
    private IReadOnlyList<TextSpan> Wanted(ExecutableStep step, IReadOnlyList<TextSpan> spans)
        => step.Text("content").Trim().ToLowerInvariant() switch
        {
            "digits" => OcrReading.Numbers(spans),
            "letters" => OcrReading.Letters(spans),
            "chinese" => OcrReading.Chinese(spans),
            "lettersandchinese" => OcrReading.LettersAndChinese(spans),
            "regex" => Extracted(step, spans),
            _ => spans,
        };

    /// <summary>
    /// The pieces the step's own pattern picks out of what was read. A pattern with nothing in it
    /// asks for no cutting, which is what a step that chose the pattern and then left it empty is
    /// saying; a pattern the machine cannot read comes back as a failed step rather than as a look
    /// that found nothing.
    /// </summary>
    private IReadOnlyList<TextSpan> Extracted(ExecutableStep step, IReadOnlyList<TextSpan> spans)
    {
        var pattern = Read(step.Text("contentPattern")).AsText().Trim();
        if (pattern.Length == 0)
        {
            return spans;
        }

        try
        {
            return OcrReading.ByPattern(spans, new Regex(pattern));
        }
        catch (ArgumentException)
        {
            throw new StepFailure("Run.BadPattern", pattern);
        }
    }

    private void Recognize(ExecutableStep step, int depth)
    {
        var (corner, area, spans) = ReadBox(step);
        var text = string.Join(' ', spans.Select(span => span.Text));
        Saw(step, LookKind.Text, area, corner, Marks([(area, corner)], TextMarks(spans, corner), 0), 0,
            note: text, minimum: Number(step, "minScore"));

        // Read as a table, the result holds rows, each one a record keyed by where its cells sit —
        // the shape reading a table through UI Automation gives. Read as text it holds the writing,
        // and either way the writing itself is handed over as well, so a macro can take it both
        // ways round.
        if (Flag(step, "table", false))
        {
            // A page read off the screen has no strip of column names, so the places its cells sit
            // in name them: A, B, C, the same names a file with no header gets.
            var rows = OcrReading.Rows(spans);
            Give(step, "table", Value.FromList(rows.Select(row => Value.FromRecord(
                row.Select((cell, index) => (Letter(index), Value.FromText(cell)))))));
            text = string.Join('\n', rows.Select(row => string.Join('\t', row)));
            Log(LogLevel.Info, depth, step.Type, "Run.ReadTable", rows.Count);
        }

        Store(step, "text", Value.FromText(text), depth);
    }

    private void FindText(ExecutableStep step, int depth)
    {
        var wanted = Read(step.Text("text")).AsText();
        var mode = step.Text("matchMode");

        // Every reading is kept, not only the ones the step is willing to act on: a look that
        // shows what the model made of the screen is what tells a step that found nothing whether
        // the writing was missing or the reading was.
        var readings = ReadSpans(step);
        var (hits, chosen) = Counting(step, readings, wanted, mode);

        SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen, wanted,
            minimum: Number(step, "minScore"));

        var span = hits.Count > 0 ? hits[0] : null;

        if (span is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, span.Location, span.Size, span.Confidence, span.Text);
        }

        // Every reading, not only the one the step picked, so a macro can walk a list of them or
        // click through a column of them; what each of them said is in its own record.
        GiveEvery(step, HitRecords(hits));

        Log(LogLevel.Info, depth, step.Type, span is null ? "Run.TextMissing" : "Run.TextFound",
            span is null ? string.Empty : $"{span.Center.X},{span.Center.Y}",
            span is null ? string.Empty : span.Text);
    }

    /// <summary>What one reading of the screen reads as a mark: the writing, with its own score.</summary>
    private static List<(ImageMatch Match, string? Label)> TextMarks(
        IReadOnlyList<TextSpan> spans, ScreenPoint origin)
        => [.. spans.Select(span => (
            new ImageMatch(span.Confidence,
                new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y),
                span.Size),
            (string?)span.Text))];

    private bool Sure(ExecutableStep step, TextSpan candidate)
        => candidate.Confidence >= Number(step, "minScore");

    private async Task ClickText(ExecutableStep step, int depth, CancellationToken token)
    {
        var wanted = Read(step.Text("text")).AsText();
        var mode = step.Text("matchMode");
        var interval = LookBeat(step);

        // The readings of the last look are kept, so the look handed over at the end is of the
        // screen as it was when the step gave up or found what it wanted.
        IReadOnlyList<TextSpan> readings = [];
        List<TextSpan> hits = [];
        var chosen = 0;

        TextSpan? Look()
        {
            readings = ReadSpans(step);
            (hits, chosen) = Counting(step, readings, wanted, mode);
            return hits.Count > 0 ? hits[0] : null;
        }

        var span = await WaitForValueAsync(Look, interval, token);
        SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen, wanted,
            minimum: Number(step, "minScore"));

        // A step that asked to look again waits a moment and confirms the line is still there: a
        // line that has already been replaced is not clicked. The second look is a single look
        // rather than another wait: whether the line is still there is not a thing to wait for.
        var again = Pace(OptionalNumber(step, "recheckMs", 0));
        if (again > 0)
        {
            await Pause(again, token);
            span = Look() ?? throw new StepFailure("Run.TextNotFound", wanted);
            SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen, wanted,
                minimum: Number(step, "minScore"));
        }

        var at = Scattered(new ScreenPoint(
            span.Center.X + Number(step, "offsetX"),
            span.Center.Y + Number(step, "offsetY")), Spread(step));

        // Where the line was read, every other place it was read, and what each of them said — the
        // same answers the step that only looks for text hands over.
        GiveMatch(step, span.Location, span.Size, span.Confidence, span.Text);
        GiveEvery(step, HitRecords(hits));

        Aim(at);
        Input(step).Click(Button(step), at.X, at.Y, 1, 0);
        Log(LogLevel.Info, depth, step.Type, "Run.ClickedText", wanted, $"{at.X},{at.Y}");
    }

    /// <summary>Compares found text against what was asked for, the way the step chose.</summary>
    private static bool Matches(string found, string wanted, string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "exact" => string.Equals(found.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase),
        "regex" => Regex.IsMatch(found, wanted, RegexOptions.IgnoreCase),
        _ => found.Contains(wanted, StringComparison.OrdinalIgnoreCase),
    };

    // ---------------------------------------------------------------------- uia

    private void Exists(ExecutableStep step, int depth)
    {
        var found = _devices.Ui.Exists(Query(step), Number(step, "timeoutMs"));
        Store(step, "exists", Value.FromBool(found), depth);
    }

    private async Task WaitElement(ExecutableStep step, int depth, CancellationToken token)
    {
        var query = Query(step);
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var poll = Number(step, "pollMs");
        if (poll <= 0)
        {
            poll = 200;
        }

        if (!await WaitForFlagAsync(() => _devices.Ui.Exists(query, 0), timeout, poll, token))
        {
            throw new StepFailure("Run.ElementNotFound", step.Text("selector"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ElementFound", step.Text("selector"));
    }

    /// <summary>
    /// Where an element sits, written into a variable the way a found picture is: the centre as
    /// "x,y", and the parts as $name.x, $name.y, $name.width and $name.height. That is what lets a
    /// later step point at the element — or a little way from it — without hunting for it again.
    /// </summary>
    private void FindElement(ExecutableStep step, int depth)
    {
        var every = Wants(step, "every");
        var index = Index(step, "matchIndex", 1);

        // Asking for the whole list is what makes the list worth collecting; otherwise the search
        // only has to reach as far as the hit the step named.
        var hits = _devices.Ui.FindAll(Query(step), every ? MatchLimit : index);
        var match = index <= hits.Count ? hits[index - 1] : null;

        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, 1, match.Name);
        }

        GiveEvery(step, hits.Select(element => Hit(element.Location, element.Size, 1, element.Name)));
        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ElementMissing" : "Run.ElementWhere",
            match is null ? string.Empty : $"{Centre(match).X},{Centre(match).Y}");
    }

    /// <summary>The middle of an element, which is the point a click aimed at it would land on.</summary>
    private static ScreenPoint Centre(UiElementInfo element)
        => new(element.Location.X + (element.Size.Width / 2),
            element.Location.Y + (element.Size.Height / 2));

    private async Task ClickElement(ExecutableStep step, int depth, CancellationToken token)
    {
        var query = Query(step);
        var timeout = Math.Max(0, Number(step, "timeoutMs"));

        if (!await WaitForFlagAsync(() => _devices.Ui.Exists(query, 0), timeout, 200, token))
        {
            throw new StepFailure("Run.ElementNotFound", step.Text("selector"));
        }

        if (!_devices.Ui.Click(query, Button(step)))
        {
            throw new StepFailure("Run.ElementNotClickable", step.Text("selector"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ClickedElement", step.Text("selector"));
    }

    private void SetElementText(ExecutableStep step, int depth)
    {
        var query = Query(step);
        var text = Read(step.Text("text")).AsText();
        var clear = !string.Equals(step.Text("clearFirst").Trim(), "false", StringComparison.OrdinalIgnoreCase);

        if (!_devices.Ui.SetText(query, text, clear))
        {
            throw new StepFailure("Run.ElementNotWritable", step.Text("selector"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ElementFilled", step.Text("selector"), text);
    }

    /// <summary>
    /// Works the element the way the element itself knows how, which is what a step wants when the
    /// pointer has to stay where it is: a background window's button can be pressed without the
    /// window coming forward and without the mouse moving.
    /// </summary>
    private async Task InvokeElement(ExecutableStep step, int depth, CancellationToken token)
    {
        var query = Query(step);
        var timeout = Math.Max(0, Number(step, "timeoutMs"));

        if (!await WaitForFlagAsync(() => _devices.Ui.Exists(query, 0), timeout, 200, token))
        {
            throw new StepFailure("Run.ElementNotFound", step.Text("selector"));
        }

        if (!_devices.Ui.Invoke(query))
        {
            throw new StepFailure("Run.ElementNotInvocable", step.Text("selector"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.InvokedElement", step.Text("selector"));
    }

    /// <summary>
    /// Everything an element says about itself, as one record: whether it can be used, whether it
    /// is showing, what it holds, what it is called and where it sits. One record rather than a
    /// variable each is the same shape a hit and a window already come in, so a macro reads one
    /// part of it by name.
    /// </summary>
    private void ReadElement(ExecutableStep step, int depth)
    {
        var facts = _devices.Ui.Facts(Query(step))
            ?? throw new StepFailure("Run.ElementNotFound", step.Text("selector"));

        Give(step, "element", Value.FromRecord(
        [
            ("enabled", Value.FromBool(facts.Enabled)),
            ("visible", Value.FromBool(facts.Visible)),
            ("value", Value.FromText(facts.Value)),
            ("name", Value.FromText(facts.Name)),
            ("automationId", Value.FromText(facts.AutomationId)),
            ("controlType", Value.FromText(facts.ControlType)),
            ("left", Value.FromNumber(facts.Location.X)),
            ("top", Value.FromNumber(facts.Location.Y)),
            ("width", Value.FromNumber(facts.Size.Width)),
            ("height", Value.FromNumber(facts.Size.Height)),
            ("box", Value.FromText(
                $"{facts.Location.X},{facts.Location.Y},{facts.Size.Width},{facts.Size.Height}")),
        ]));

        Log(LogLevel.Info, depth, step.Type, "Run.ReadElement", step.Text("selector"), facts.Value);
    }

    private void FocusWindow(ExecutableStep step, int depth)
    {
        var title = Read(step.Text("window")).AsText();
        if (!_devices.Ui.FocusWindow(title))
        {
            throw new StepFailure("Run.WindowNotFound", title);
        }

        Log(LogLevel.Info, depth, step.Type, "Run.FocusedWindow", title);
    }

    /// <summary>
    /// Picks one entry of a list, a drop-down or a set of tabs. The entry is named either by the
    /// text it shows — which a variable may hold, so a value read somewhere else can be chosen
    /// straight away — or by its number, counted from one.
    /// </summary>
    private void SelectItem(ExecutableStep step, int depth)
    {
        var item = Read(step.Text("item")).AsText().Trim();
        var index = Number(step, "itemIndex");
        var which = item.Length > 0 ? item : index.ToString(CultureInfo.InvariantCulture);
        if (item.Length == 0 && index <= 0)
        {
            throw new StepFailure("Run.MissingItem");
        }

        if (!_devices.Ui.Select(Query(step), item, index))
        {
            throw new StepFailure("Run.ItemNotFound", which);
        }

        Log(LogLevel.Info, depth, step.Type, "Run.SelectedItem", step.Text("selector"), which);
    }

    /// <summary>Turns a check box on, off, or the other way round.</summary>
    private void SetElementChecked(ExecutableStep step, int depth)
    {
        if (!_devices.Ui.SetChecked(Query(step), Switch(step.Text("state"))))
        {
            throw new StepFailure("Run.ElementNotCheckable", step.Text("selector"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ElementChecked", step.Text("selector"));
    }

    /// <summary>Opens or closes a node: a tree branch, an accordion, a collapsed panel.</summary>
    private void SetElementExpanded(ExecutableStep step, int depth)
    {
        var action = step.Text("state").Trim();
        if (!_devices.Ui.SetExpanded(Query(step), action.Length == 0 ? "expand" : action))
        {
            throw new StepFailure("Run.ElementNotExpandable", step.Text("selector"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ElementExpanded", step.Text("selector"));
    }

    /// <summary>Scrolls an element into view inside whatever list or panel holds it.</summary>
    private void ScrollElementIntoView(ExecutableStep step, int depth)
    {
        if (!_devices.Ui.ScrollIntoView(Query(step)))
        {
            throw new StepFailure("Run.ElementNotScrollable", step.Text("selector"));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ScrolledElement", step.Text("selector"));
    }

    /// <summary>
    /// Reads a table into a variable as a list of rows, each row a record whose keys are the names
    /// the table writes above its columns — the same shape the CSV reader hands back, so the same
    /// steps walk either one and a cell is read by name rather than by counting along the line.
    /// </summary>
    private void ReadElementTable(ExecutableStep step, int depth)
    {
        // A step with no row limit at all still gets one, the same default the editor shows, so a
        // table nobody bounded cannot be read forever.
        var limit = Math.Clamp(OptionalNumber(step, "maxRows", DefaultRows), 1, MaxRows);
        var table = _devices.Ui.ReadTable(Query(step), limit);

        // A grid that keeps no strip of column names still has columns: they are the places its
        // cells sit in, so the widest row says how many there are.
        var width = table.Columns.Count > 0
            ? table.Columns.Count
            : table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count);
        var names = ColumnNames([.. table.Columns.Select(Value.FromText)], width);

        var name = Give(step, "table", Value.FromList(
            table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    Value.FromText(index < row.Count ? row[index] : string.Empty)))))));

        Log(LogLevel.Info, depth, step.Type, "Run.ReadTable", name, table.Rows.Count);
    }

    /// <summary>The most rows one step reads, so a grid that keeps growing cannot fill memory.</summary>
    private const int MaxRows = 10000;

    /// <summary>How many rows a step reads when it does not say.</summary>
    private const int DefaultRows = 100;

    /// <summary>
    /// What a three-way setting means for something that can be on, off, or left to decide itself:
    /// true, false, or nothing at all for "the other way round".
    /// </summary>
    private static bool? Switch(string text) => text.Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "yes" => true,
        "off" or "false" or "no" => false,
        _ => null,
    };

    // --------------------------------------------------------------------- script

    /// <summary>
    /// Opens a browser, after checking the one Playwright drives is on the machine. The check
    /// happens here rather than inside the driver so the user is told what to install instead of
    /// being handed whatever the driver says when it finds nothing to start.
    /// </summary>
    private void BrowserOpen(ExecutableStep step, int depth)
    {
        var browser = _devices.Browser;
        var engine = step.Text("browser").Trim();
        if (!browser.Ready(engine))
        {
            throw new StepFailure("Run.BrowserMissing", browser.InstallHint);
        }

        var url = Read(step.Text("url")).AsText();
        browser.Open(engine, url, Flag(step, "headless", false));
        Log(LogLevel.Info, depth, step.Type, "Run.OpenedBrowser", engine, browser.Url);
    }

    /// <summary>Reads the page, or one element of it, into a variable.</summary>
    private void BrowserReadText(ExecutableStep step, int depth)
    {
        var text = _devices.Browser.Text(Read(step.Text("target")).AsText());
        Give(step, "text", Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, "Run.BrowserRead", text.Length);
    }

    /// <summary>
    /// Picks an option of a dropdown. The position is turned into a number here, where the step's
    /// own name can be put on the failure: a box holding something that is not a whole number is a
    /// mistake in the step, and the person reading it wants to see which step.
    /// </summary>
    private void BrowserSelect(ExecutableStep step, int depth)
    {
        var value = Read(step.Text("value")).AsText();
        var byIndex = step.Text("by").Trim().Equals("index", StringComparison.OrdinalIgnoreCase);
        if (byIndex && !Position(value))
        {
            throw new StepFailure("Run.BadPosition", value);
        }

        _devices.Browser.Select(Read(step.Text("target")).AsText(), value, byIndex);
        Log(LogLevel.Info, depth, step.Type, "Run.BrowserSelected", step.Text("target"), value);
    }

    /// <summary>
    /// Whether a written place in a list is one a person could read off the page: a whole number,
    /// counted from the top starting at 1.
    /// </summary>
    private static bool Position(string value)
        => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)
            && at >= 1;

    /// <summary>
    /// Sends one key, or one chord, into the page. The name is checked against the same list the
    /// keyboard picker offers before it goes anywhere, so a key name that means nothing is refused
    /// here rather than arriving as a letter that happens to share its first character.
    /// </summary>
    private void BrowserPress(ExecutableStep step, int depth)
    {
        var key = step.Text("key").Trim();
        var keys = KeyNames.ResolveChord(key, out var unknown);
        if (keys.Count == 0 || unknown is not null)
        {
            throw new StepFailure("Run.UnknownKey", unknown ?? key);
        }

        _devices.Browser.Press(Read(step.Text("target")).AsText(), key);
        Log(LogLevel.Info, depth, step.Type, "Run.BrowserPressed", key);
    }

    /// <summary>
    /// Waits for an element of the page to be in the state the step names. The step's own clock is
    /// what the wait is given, so the browser gives up at the same moment the step would rather
    /// than half a minute later, and running out is reported as the step having waited too long.
    /// </summary>
    private void BrowserWaitFor(ExecutableStep step, int depth)
    {
        var state = step.Text("state").Trim().ToLowerInvariant() switch
        {
            "hidden" => ElementWait.Hidden,
            "attached" => ElementWait.Attached,
            "detached" => ElementWait.Detached,
            _ => ElementWait.Visible,
        };

        var selector = Read(step.Text("target")).AsText();
        _devices.Browser.WaitFor(selector, state, step.Meta.TimeoutMs);
        Log(LogLevel.Info, depth, step.Type, "Run.BrowserWaited", selector);
    }

    /// <summary>
    /// Hands files to a file input, one path to a line. A relative path is read from the folder the
    /// macro file is in, the same as every other path a step is given, so a macro that hands over a
    /// file beside it can be moved to another machine as it stands.
    /// </summary>
    private void BrowserUpload(ExecutableStep step, int depth)
    {
        var paths = Lines(Read(step.Text("files")).AsText())
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(_devices.Files.Resolve)
            .ToList();
        if (paths.Count == 0)
        {
            throw new StepFailure("Run.MissingPath");
        }

        _devices.Browser.Upload(Read(step.Text("target")).AsText(), paths);
        Log(LogLevel.Info, depth, step.Type, "Run.BrowserUploaded", paths.Count);
    }

    /// <summary>
    /// Moves the macro to another tab of the browser that is open. Many sites hand the page the
    /// macro came for to a new tab, and the macro would otherwise go on aiming its steps at the tab
    /// it was on — where, on a site whose pages look alike, a step finds something that matches and
    /// works on the wrong page without anything looking wrong.
    /// </summary>
    private void BrowserSwitchTab(ExecutableStep step, int depth)
    {
        var choice = step.Text("how").Trim().ToLowerInvariant() switch
        {
            "index" => TabChoice.Index,
            "title" => TabChoice.Title,
            "url" or "address" => TabChoice.Address,
            _ => TabChoice.Newest,
        };

        _devices.Browser.SwitchTab(choice, Number(step, "index"), Read(step.Text("match")).AsText());
        Log(LogLevel.Info, depth, step.Type, "Run.BrowserTab", _devices.Browser.Url);
    }

    /// <summary>
    /// Runs a short script through an interpreter the machine already has. This is the block that
    /// covers whatever the catalogue does not: anything with a command line of its own can be
    /// scripted, and the script talks to the macro both ways — values go in as <c>{{name}}</c> in
    /// the text, and whatever the script prints comes back in the result variable.
    /// </summary>
    private void RunScript(ExecutableStep step, int depth)
    {
        var language = step.Text("language").Trim().ToLowerInvariant();
        var extension = ScriptExtension(step, language);
        var temp = Path.Combine(Path.GetTempPath(), "WhaleGenie");
        var path = Path.Combine(temp, $"script-{Guid.NewGuid():N}{extension}");
        var script = Template(step.Text("script"));
        if (script.Trim().Length == 0)
        {
            throw new StepFailure("Run.MissingScript");
        }

        // A script that reads or writes files next to the macro should not have to be told where
        // that is, so the macros folder is what an empty working folder means — but only when that
        // folder is really there: a program cannot be started in a folder that does not exist, and
        // an empty folder field is better than a script that never runs.
        var folder = Read(step.Text("folder")).AsText().Trim();
        if (folder.Length == 0 && _devices.Files.Exists(_devices.Files.BaseFolder))
        {
            folder = _devices.Files.BaseFolder;
        }

        var extra = Template(step.Text("arguments")).Trim();
        var (program, arguments) = ScriptCommand(step, language, path, extra);

        // A step that says nothing about time still gets one, the same way running a command does:
        // a timeout of nothing would end the script the moment it started.
        var timeout = Math.Max(1, OptionalNumber(step, "timeoutMs", DefaultScriptMs));

        _devices.Files.WriteText(path, script, false, ScriptEncoding(step, extension));

        CommandResult result;
        try
        {
            result = _devices.Processes.Run(new CommandRequest(program, arguments, folder, timeout,
                OutputEncoding: ScriptOutput(language)));
        }
        finally
        {
            // The script itself lives in the macro, so the copy on disk is only there for as long
            // as the interpreter needs it. A tidy-up that fails is not worth failing the step over.
            try
            {
                _devices.Files.Delete(path, false);
            }
            catch (Exception)
            {
                Log(LogLevel.Debug, depth, step.Type, "Run.ScriptNotTidied", path);
            }
        }

        if (result.ExitCode != 0)
        {
            throw new StepFailure("Run.ScriptFailed",
                $"{result.ExitCode}: {Careful(result.StandardError)}");
        }

        var said = result.StandardOutput.Trim('\r', '\n');
        Give(step, "output", Value.FromText(said));
        Log(LogLevel.Info, depth, step.Type, "Run.RanScript", language, said.Length);
    }

    /// <summary>
    /// The command line that runs a script of this kind. PowerShell is given the flags that stop it
    /// from loading a profile or asking anything, so a script that runs inside a macro behaves the
    /// same way every time it runs. A program of the macro's own choosing gets the script's path
    /// after whatever the macro wrote, which is where an interpreter expects to find it and where
    /// a person typing the same command line would put it.
    /// </summary>
    private static (string Program, string Arguments) ScriptCommand(ExecutableStep step,
        string language, string path, string extra)
    {
        var quoted = $"\"{path}\"";
        var tail = extra.Length == 0 ? string.Empty : $" {extra}";

        if (language is "custom")
        {
            var named = step.Text("interpreter").Trim();
            if (named.Length == 0)
            {
                throw new StepFailure("Run.MissingInterpreter");
            }

            var (program, flags) = SplitCommand(named);
            var start = flags.Length == 0 ? string.Empty : $"{flags} ";
            return (program, $"{start}{quoted}{tail}");
        }

        return language switch
        {
            "" or "powershell" => ("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {quoted}{tail}"),
            "cmd" => ("cmd.exe", $"/c {quoted}{tail}"),
            "node" => ("node", $"{quoted}{tail}"),
            "python" => ("python", $"{quoted}{tail}"),
            _ => throw new StepFailure("Run.UnknownInterpreter", language),
        };
    }

    /// <summary>
    /// Splits what a macro wrote into the program and the flags in front of the script's path. A
    /// program whose path has spaces in it is written in quotes, which is how it would be typed on
    /// a command line anyway, so a quote at the front takes everything up to the closing one.
    /// </summary>
    private static (string Program, string Flags) SplitCommand(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 0)
            {
                return (trimmed[1..end], trimmed[(end + 1)..].Trim());
            }
        }

        var space = trimmed.IndexOf(' ');
        return space < 0
            ? (trimmed, string.Empty)
            : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }

    /// <summary>
    /// The file name ending the interpreter recognises, which is how it knows what it is reading.
    /// The four built-in ones have theirs; a program of the macro's own choosing has to be told.
    /// </summary>
    private static string ScriptExtension(ExecutableStep step, string language)
    {
        if (language is not "custom")
        {
            return language switch
            {
                "cmd" => ".cmd",
                "node" => ".js",
                "python" => ".py",
                _ => ".ps1",
            };
        }

        var wanted = step.Text("extension").Trim();
        if (wanted.Length == 0)
        {
            throw new StepFailure("Run.MissingScriptExtension");
        }

        return wanted.StartsWith('.') ? wanted : $".{wanted}";
    }

    /// <summary>
    /// How the interpreter's copy of the script is written. Windows PowerShell reads a script as
    /// the system code page unless the file starts with a byte-order mark, so a script with Chinese
    /// text in it needs one; the other interpreters read UTF-8 and would rather not have a mark,
    /// and a batch file with one does not start. A macro that knows better can say so, and the
    /// reason it can is cscript: that one reads the system code page by design, so Chinese text in
    /// a .vbs has to be GBK.
    /// </summary>
    private static string ScriptEncoding(ExecutableStep step, string extension)
    {
        var chosen = step.Text("encoding").Trim();
        return chosen.Length == 0
               || string.Equals(chosen, "auto", StringComparison.OrdinalIgnoreCase)
            ? extension == ".ps1" ? "utf8bom" : TextEncoding.Default
            : chosen;
    }

    /// <summary>
    /// The code page an interpreter prints in. Node writes UTF-8 wherever it runs; the others —
    /// Windows PowerShell, the command prompt and Python alike — write in this machine's own code
    /// page, which is GBK on a Chinese Windows. Read with the wrong one and every Chinese word the
    /// script printed comes back as question marks, so the engine works it out rather than leaving
    /// the user to.
    /// </summary>
    private static string ScriptOutput(string language)
        => language is "node" ? TextEncoding.Default : TextEncoding.System;

    /// <summary>How long a script may run when the step does not say.</summary>
    private const int DefaultScriptMs = 60000;

    /// <summary>
    /// Fills the <c>{{name}}</c> placeholders with what those variables hold. A script is written in
    /// a language of its own — PowerShell, JavaScript, a batch file — and every one of them gives
    /// its own meaning to <c>$</c> and <c>%</c>, so a macro value is spelled differently in here on
    /// purpose. A name the macro does not know is left as it was written, where it is easy to see
    /// rather than quietly replaced by nothing.
    /// </summary>
    private string Template(string text)
        => Regex.Replace(text, @"\{\{(.+?)\}\}", match =>
        {
            var name = match.Groups[1].Value.Trim();
            return Variables.TryGet(name, out var value) ? value.AsText() : match.Value;
        });

    /// <summary>What a script said on its error output, kept short enough to read in a message.</summary>
    private static string Careful(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }

    /// <summary>
    /// Reads a selector such as <c>Button[name='Save']</c> or <c>Edit[automationId='input']</c>,
    /// together with the optional window title beside it.
    /// </summary>
    private UiQuery Query(ExecutableStep step)
        => UiQuery.Parse(
            step.Text("selector"),
            Read(step.Text("window")).AsText(),
            Index(step, "matchIndex", 1));

    /// <summary>
    /// The same, for the control a step measures its coordinates from. A step that is anchored to a
    /// control keeps its own pair of fields, so the two readings never take each other's values.
    /// </summary>
    private UiQuery AnchorQuery(ExecutableStep step)
        => UiQuery.Parse(step.Text("anchorSelector"), Read(step.Text("anchorWindow")).AsText());

    /// <summary>
    /// Reads a value: a bare variable name, then an expression, then plain text. That is
    /// what lets the fields accept <c>count</c>, <c>$count + 1</c> and <c>hello</c> alike.
    /// </summary>
    private Value Read(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Value.Null;
        }

        if (Variables.TryGet(trimmed, out var direct))
        {
            return direct;
        }

        if (Expression.TryEvaluate(trimmed, Variables, out var value, out var error))
        {
            return value;
        }

        // Text that was written as an expression has to work, so a typo shows up as a
        // failure rather than quietly becoming a literal.
        if (LooksLikeExpression(trimmed))
        {
            throw error!;
        }

        // Numbers are read without a thousands separator: text like "10,20,30,40" or "1,000"
        // is text, not a number, and a comma means a separator rather than a digit group.
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return Value.FromNumber(number);
        }

        return Value.FromText(Interpolate(trimmed));
    }

    /// <summary>
    /// True when the text can only be meant as an expression: it opens with the variable sign, so
    /// the author was writing a value rather than words.
    /// </summary>
    /// <remarks>
    /// Brackets and parentheses used to count as the same sign, and they are exactly what a
    /// selector or a path is made of — <c>[data-testid="row-2"]</c>, <c>div &gt; p:nth-of-type(2)</c>,
    /// <c>C:\Program Files (x86)\a.log</c> — so a value that was plainly one of those came back as
    /// "not a valid expression" instead of being used. A "$" at the front is the one opening that
    /// cannot be anything else; a "$" in the middle is left alone, because a written-out dollar
    /// sign looks like that too.
    /// </remarks>
    private static bool LooksLikeExpression(string text) => text[0] == '$';

    /// <summary>Replaces every <c>$name</c> that names a known variable with its text.</summary>
    private string Interpolate(string text)
    {
        var result = text;
        foreach (var name in Expression.ReferencedNames(text))
        {
            if (Variables.TryGet(name, out var value))
            {
                result = result.Replace("$" + name, value.AsText(), StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private int Number(ExecutableStep step, string name)
    {
        try
        {
            return Vary(step, name, (int)Read(step.Text(name)).AsNumber());
        }
        catch (ExpressionException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Bends a number by whatever slack the step asked for. Only lengths of time are written with
    /// one, so this is what stops a macro's waits from looking like a machine's: 500 written with
    /// 20% of give comes out anywhere from 400 to 600, a fresh number every time the step runs.
    /// </summary>
    private static int Vary(ExecutableStep step, string name, int value)
    {
        var jitter = Math.Min(1m, Math.Max(0m, step.Jitter(name)));
        if (jitter == 0m || value == 0)
        {
            return value;
        }

        var bend = 1 + (((System.Random.Shared.NextDouble() * 2) - 1) * (double)jitter);
        return (int)Math.Round(value * bend);
    }

    /// <summary>A number a step may leave out, taking <paramref name="fallback"/> when it does.</summary>
    private int OptionalNumber(ExecutableStep step, string name, int fallback)
        => step.Text(name).Trim().Length == 0 ? fallback : Number(step, name);

    /// <summary>
    /// A yes/no setting. A step that says nothing takes the catalogue's own default, so a
    /// missing switch behaves the way the action editor showed it rather than always being on.
    /// </summary>
    private static bool Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static LogLevel Level(string level) => level.ToLowerInvariant() switch
    {
        "warn" => LogLevel.Warn,
        "error" => LogLevel.Error,
        "debug" => LogLevel.Debug,
        _ => LogLevel.Info,
    };

    private static async Task Pause(int milliseconds, CancellationToken token)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking again every <paramref name="pollMs"/> and giving
    /// up after <paramref name="timeoutMs"/>. Every wait that carries its own length comes through
    /// here: waiting is the same thing whether it is an element, a window, a program or a condition
    /// the macro wrote itself, so the pacing and the way a timeout is counted are written once. The
    /// question is asked before the clock is read, so a wait that already holds costs nothing, and
    /// a timeout of zero means "look once and give up".
    /// </summary>
    /// <summary>
    /// The beat a step that waits for something looks at the screen on when it was not given one:
    /// one refresh of the screen it is running on, because a picture that has not been drawn again
    /// cannot differ from the one just read, and a look that comes sooner than the next refresh
    /// reads the same frame twice. That is 16 ms on a 60 Hz display and 4 ms on a 240 Hz one, which
    /// is what makes the default follow the screen rather than the machine the macro was written
    /// on. A number written into the step is used as it stands, and 0 means "as soon as the look
    /// before it is done".
    /// </summary>
    private int LookBeat(ExecutableStep step)
        => Math.Max(1, OptionalNumber(step, "intervalMs", _devices.Screen.RefreshMilliseconds));

    private async Task<bool> WaitForFlagAsync(Func<bool> ready, int timeoutMs, int pollMs,
        CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (ready())
            {
                return true;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return false;
            }

            await Pause(pollMs, token);
        }
    }

    /// <summary>The same wait, for something that turns up rather than becomes true.</summary>
    private async Task<T?> WaitForValueAsync<T>(Func<T?> probe, int timeoutMs, int pollMs,
        CancellationToken token)
        where T : class
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return null;
            }

            await Pause(pollMs, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking again every <paramref name="pollMs"/> until it
    /// does. How long that may take is not the wait's to say: the step's own timeout is what ends
    /// it, which is what makes the length of a wait a setting of the step rather than a field that
    /// says the same thing a second time.
    /// </summary>
    private static async Task WaitForFlagAsync(Func<bool> ready, int pollMs, CancellationToken token)
    {
        while (!ready())
        {
            await Pause(pollMs, token);
        }
    }

    /// <summary>The same, for something that turns up rather than becomes true.</summary>
    private static async Task<T> WaitForValueAsync<T>(Func<T?> probe, int pollMs,
        CancellationToken token)
        where T : class
    {
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await Pause(pollMs, token);
        }
    }

    private void Log(LogLevel level, int depth, string step, string key, params object[] arguments)
        => _host.Log(new LogEntry(DateTimeOffset.Now, level, depth, step, key, arguments));

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
