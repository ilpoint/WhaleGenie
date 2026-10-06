using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Viktor.Core.Devices;
using Viktor.Core.Expressions;
using Viktor.Core.Variables;

namespace Viktor.Core.Execution;

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

    /// <summary>Pictures captured during the run, kept under the name they were saved to.</summary>
    private readonly Dictionary<string, ImageFrame> _images = new(StringComparer.OrdinalIgnoreCase);

    private int _executed;
    private int _callDepth;
    private StepFailure? _failure;

    public MacroRunner(VariableStore variables, IRunHost? host = null, IDeviceLayer? devices = null,
        double delayScale = 1, IMacroLibrary? macros = null)
    {
        Variables = variables;
        _host = host ?? new SilentRunHost();
        _devices = devices ?? NullDeviceLayer.Instance;
        _macros = macros ?? EmptyMacroLibrary.Instance;
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

    /// <summary>Runs the steps and reports how the run ended.</summary>
    public async Task<RunResult> RunAsync(IReadOnlyList<ExecutableStep> steps,
        CancellationToken token = default)
    {
        _executed = 0;
        _failure = null;
        _images.Clear();
        Log(LogLevel.Info, 0, string.Empty, "Run.Start", steps.Count);
        if (Math.Abs(DelayScale - 1) > 0.001)
        {
            Log(LogLevel.Info, 0, string.Empty, "Run.Speed", DelayScale);
        }

        try
        {
            var signal = await RunSteps(steps, 0, token);
            var outcome = signal switch
            {
                Signal.Stop => new RunResult(RunStatus.Stopped, "Run.Stopped", string.Empty, _executed),
                Signal.Failed => new RunResult(RunStatus.Failed, _failure?.Key ?? "Run.Failed",
                    _failure?.Detail ?? string.Empty, _executed),
                _ => new RunResult(RunStatus.Completed, "Run.Finished", string.Empty, _executed),
            };

            Log(LogLevel.Info, 0, string.Empty, SummaryKey(outcome.Status), _executed);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warn, 0, string.Empty, "Run.Stopped", _executed);
            return new RunResult(RunStatus.Stopped, "Run.Stopped", string.Empty, _executed);
        }
    }

    /// <summary>The line written once a run is over.</summary>
    private static string SummaryKey(RunStatus status) => status switch
    {
        RunStatus.Stopped => "Run.Stopped",
        RunStatus.Failed => "Run.Aborted",
        _ => "Run.Finished",
    };

    /// <summary>What a step tells the runner to do next.</summary>
    private enum Signal
    {
        Normal,
        Break,
        Continue,
        Stop,
        Failed,
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
    /// Runs another macro's steps as though they had been written here. The called macro shares
    /// this run's variables, which is what makes a sub-macro worth having: it can read what the
    /// caller set up and leave its answer where the caller will look for it.
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

        // The loop counters belong to whoever is doing the looping, so the called macro gets
        // its own and the caller's are put back exactly as they were.
        var saved = RememberFrame();
        _callDepth++;
        try
        {
            Log(LogLevel.Info, depth, step.Type, "Run.CalledMacro", name, steps.Count);
            var signal = await RunSteps(steps, depth + 1, token);

            // A break or a continue with no loop of its own to act on ends the called macro,
            // rather than reaching out and cutting a loop in the caller short.
            if (signal is Signal.Break or Signal.Continue)
            {
                Log(LogLevel.Warn, depth, step.Type, "Run.MacroBreak", name);
                return Signal.Normal;
            }

            return signal;
        }
        finally
        {
            _callDepth--;
            RestoreFrame(saved);
        }
    }

    /// <summary>
    /// The bookkeeping a called macro must not carry away: the counter of the loop it was
    /// called from, and which of those there were to begin with.
    /// </summary>
    private (Dictionary<string, Value> Values, HashSet<string> Names) RememberFrame()
    {
        var values = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (local, value) in Variables.Local.Values)
        {
            if (local.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(local);
                values[local] = value;
            }
        }

        return (values, names);
    }

    /// <summary>Puts the caller's own bookkeeping back and drops anything the call left behind.</summary>
    private void RestoreFrame((Dictionary<string, Value> Values, HashSet<string> Names) frame)
    {
        var extra = Variables.Local.Values.Keys
            .Where(local => local.StartsWith("sys.", StringComparison.OrdinalIgnoreCase)
                            && !frame.Names.Contains(local))
            .ToList();

        foreach (var local in extra)
        {
            Variables.Local.Remove(local);
        }

        foreach (var (local, value) in frame.Values)
        {
            Variables.Local.Set(local, value);
        }
    }

    private async Task<Signal> RunStep(ExecutableStep step, int depth, CancellationToken token)
    {
        if (!step.Meta.IsEnabled)
        {
            Log(LogLevel.Debug, depth, step.Type, "Run.Skipped");
            return Signal.Normal;
        }

        await _host.BeforeStep(step, depth, token);
        await Pause(Pace(step.Meta.DelayBeforeMs), token);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var started = Stopwatch.GetTimestamp();
                var signal = await ExecuteChecked(step, depth, token);

                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (step.Meta.TimeoutMs > 0 && elapsed > step.Meta.TimeoutMs)
                {
                    throw new StepFailure("Run.Timeout",
                        step.Meta.TimeoutMs.ToString(CultureInfo.InvariantCulture));
                }

                _executed++;
                await Pause(Pace(step.Meta.DelayAfterMs), token);
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

                var decision = step.Meta.OnError switch
                {
                    StepErrorAction.Continue => Decision.Skip,
                    StepErrorAction.NextIteration => Decision.NextIteration,
                    StepErrorAction.AskUser => await Ask(step, failure, token),
                    _ => Decision.Stop,
                };

                if (decision is Decision.Retry)
                {
                    await Pause(RetryPause(step, attempt + 1), token);
                    continue;
                }

                return decision switch
                {
                    Decision.Skip => Signal.Normal,
                    Decision.NextIteration => Signal.Continue,
                    _ => Signal.Failed,
                };
            }
        }
    }

    private async Task<Decision> Ask(ExecutableStep step, StepFailure failure, CancellationToken token)
    {
        var choice = await _host.Ask(step.Type, failure.Key, failure.Detail, token);
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

            case "control.if":
                return await RunIf(step, depth, token);

            case "control.try":
                return await RunTry(step, depth, token);

            case "control.break":
                return Signal.Break;

            case "control.continue":
                return Signal.Continue;

            case "control.stop":
                Log(LogLevel.Warn, depth, step.Type, "Run.StopRequested", step.Text("reason"));
                return Signal.Stop;

            case "control.setVariable":
                SetVariable(step, depth);
                return Signal.Normal;

            case "control.listCreate":
                CreateList(step, depth);
                return Signal.Normal;

            case "control.listAdd":
            case "control.listInsert":
            case "control.listRemoveAt":
            case "control.listClear":
                ChangeList(step, depth);
                return Signal.Normal;

            case "control.log":
                Log(Level(step.Text("level")), depth, step.Type, "Run.Message", Read(step.Text("message")).AsText());
                return Signal.Normal;

            // ------------------------------------------------------------------- file
            case "file.readText":
                ReadTextFile(step, depth);
                return Signal.Normal;

            case "file.writeText":
                WriteTextFile(step, depth);
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

            // -------------------------------------------------------------- clipboard
            case "clipboard.writeText":
                ClipboardWrite(step, depth);
                return Signal.Normal;

            case "clipboard.readText":
                ClipboardRead(step, depth);
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

            case "system.environment":
                EnvironmentVariable(step, depth);
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

            // ------------------------------------------------------------------ input
            case "input.keyPress":
                Input(step).KeyPress(step.Text("key"), Pace(Number(step, "holdMs")));
                return Signal.Normal;

            case "input.keyDown":
                Input(step).KeyDown(step.Text("key"));
                return Signal.Normal;

            case "input.keyUp":
                Input(step).KeyUp(step.Text("key"));
                return Signal.Normal;

            case "input.hotkey":
                Input(step).Hotkey(Keys(step), Pace(Number(step, "holdMs")));
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
                {
                    var point = Point(step, "x", "y");
                    Input(step).Click(Button(step), point.X, point.Y,
                        Math.Max(1, Number(step, "clicks")), Pace(Number(step, "intervalMs")));
                    return Signal.Normal;
                }

            case "input.mouseDoubleClick":
                {
                    var point = Point(step, "x", "y");
                    Input(step).Click(Button(step), point.X, point.Y, 2, 0);
                    return Signal.Normal;
                }

            case "input.mouseDown":
                {
                    var point = Point(step, "x", "y");
                    Input(step).MouseDown(Button(step), point.X, point.Y);
                    return Signal.Normal;
                }

            case "input.mouseUp":
                {
                    var point = Point(step, "x", "y");
                    Input(step).MouseUp(Button(step), point.X, point.Y);
                    return Signal.Normal;
                }

            case "input.mouseScroll":
                {
                    var point = Point(step, "x", "y");
                    Input(step).Scroll(step.Text("direction"), Math.Max(1, Number(step, "amount")),
                        point.X, point.Y);
                    return Signal.Normal;
                }

            case "input.mouseDrag":
                DragPointer(step);
                return Signal.Normal;

            // ----------------------------------------------------------------- vision
            case "vision.capture":
                Capture(step, depth);
                return Signal.Normal;

            case "vision.getPixel":
                GetPixel(step, depth);
                return Signal.Normal;

            case "vision.waitColor":
                await WaitColor(step, depth, token);
                return Signal.Normal;

            case "vision.findColor":
                await FindColor(step, depth, token);
                return Signal.Normal;

            case "vision.findImage":
                LookFor(step, depth);
                return Signal.Normal;

            case "vision.waitImage":
                await WaitForImage(step, depth, token);
                return Signal.Normal;

            case "vision.clickImage":
                await ClickImage(step, depth, token);
                return Signal.Normal;

            // -------------------------------------------------------------------- ocr
            case "ocr.recognize":
                Recognize(step, depth);
                return Signal.Normal;

            case "ocr.findText":
                FindText(step, depth);
                return Signal.Normal;

            case "ocr.clickText":
                await ClickText(step, depth, token);
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

            case "uia.setText":
                SetElementText(step, depth);
                return Signal.Normal;

            case "uia.getText":
                GetElementText(step, depth);
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
    /// the question already written in.
    /// </summary>
    private async Task<Signal> RunWaitUntil(ExecutableStep step, int depth, CancellationToken token)
    {
        var condition = step.Condition("condition")
                        ?? throw new StepFailure("Run.MissingCondition");
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var poll = Number(step, "pollMs");
        if (poll <= 0)
        {
            poll = 200;
        }

        var started = Stopwatch.GetTimestamp();
        var held = await WaitForFlagAsync(() => Check(condition, depth, token), timeout, poll, token);
        var waited = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // How long it waited is worth keeping even when it gave up, which is what makes a macro
        // that is too slow to catch something diagnosable.
        var name = step.Text("elapsedVariable").Trim();
        if (name.Length > 0)
        {
            Variables.Set(name, Value.FromNumber(waited));
        }

        if (held)
        {
            Log(LogLevel.Info, depth, step.Type, "Run.WaitedFor", waited, condition.Type);
            return Signal.Normal;
        }

        if (string.Equals(step.Text("onTimeout").Trim(), "continue", StringComparison.OrdinalIgnoreCase))
        {
            Log(LogLevel.Warn, depth, step.Type, "Run.WaitGaveUp", waited, condition.Type);
            return Signal.Normal;
        }

        throw new StepFailure("Run.WaitTimeout", condition.Type);
    }

    private async Task<Signal> RunRepeat(ExecutableStep step, int depth, CancellationToken token)
    {
        var times = Math.Max(0, Number(step, "times"));
        var interval = Pace(Number(step, "intervalMs"));

        for (var round = 0; round < times; round++)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set("sys.loopIndex", Value.FromNumber(round));
            var signal = await RunSteps(step.Children("body"), depth + 1, token);
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

            var signal = await RunSteps(step.Children("body"), depth + 1, token);
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

        var index = 0;
        foreach (var item in order)
        {
            token.ThrowIfCancellationRequested();
            Variables.Local.Set("sys.loopIndex", Value.FromNumber(index));
            Variables.Set(itemName, item);

            var signal = await RunSteps(step.Children("body"), depth + 1, token);
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

            // Stopping or failing while tidying up beats whatever the attempt did.
            if (closingSignal is Signal.Stop or Signal.Failed)
            {
                return closingSignal;
            }
        }

        return signal;
    }

    /// <summary>Records why the attempt failed, or empties the variable when it did not.</summary>
    private void Remember(ExecutableStep step, StepFailure? failure)
    {
        var name = step.Text("errorVariable").Trim();
        if (name.Length == 0)
        {
            return;
        }

        var reason = failure switch
        {
            null => string.Empty,
            { Detail.Length: > 0 } => failure.Detail,
            _ => failure.Key,
        };

        Variables.Set(name, Value.FromText(reason));
    }

    /// <summary>
    /// Answers one condition. Nothing here waits or hands work to another thread, so a condition
    /// can be asked again and again without holding anything up — which is what the wait block
    /// does with it.
    /// </summary>
    private bool Check(ExecutableStep condition, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

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

            case "condition.colorEquals":
                {
                    var target = PixelColor.Parse(condition.Text("color"));
                    return PixelAt(condition).Matches(target, Read(condition.Text("tolerance")).AsNumber());
                }

            case "condition.colorsMatch":
                return ColoursMatch(condition);

            default:
                Log(LogLevel.Warn, depth, condition.Type, "Run.UnknownCondition", condition.Type);
                return false;
        }
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

        var matched = Points(condition)
            .Select(point => _devices.Screen.PixelAt(point.Where.X, point.Where.Y)
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

        // Posting messages needs a window to post them at, and the step names it by title.
        var title = Read(step.Text("targetWindow")).AsText().Trim();
        if (title.Length == 0)
        {
            throw new StepFailure("Run.MissingTargetWindow");
        }

        var window = _devices.Windows.Find(title)
                     ?? throw new StepFailure("Run.WindowNotFound", title);

        return new InputRoute(InputDelivery.Background, window.Handle);
    }

    /// <summary>
    /// The corner a step's numbers are measured from, or <c>null</c> when they are screen pixels
    /// already. It is looked up afresh every time the step runs, which is what keeps a macro that
    /// names a window or a control pointing at the same place after the window has been dragged
    /// elsewhere or the list under the control has been scrolled.
    /// </summary>
    private ScreenPoint? Anchor(ExecutableStep step)
    {
        var mode = step.Text("anchorMode").Trim().ToLowerInvariant();

        if (mode == "element")
        {
            var selector = step.Text("anchorSelector");
            var anchor = _devices.Ui.FindAll(AnchorQuery(step), 1).FirstOrDefault()
                         ?? throw new StepFailure("Run.ElementNotFound", selector);

            return anchor.Location;
        }

        if (mode is not ("window" or "client"))
        {
            return null;
        }

        // No title means the window in front, the same way every other window field reads.
        var title = Read(step.Text("anchorWindow")).AsText().Trim();
        var window = _devices.Windows.Find(title)
                     ?? throw new StepFailure("Run.WindowNotFound", title);

        return mode == "client" ? _devices.Windows.ClientOrigin(window.Handle) : window.Location;
    }

    /// <summary>One written position, in the screen pixels the devices ask for.</summary>
    private ScreenPoint Place(ExecutableStep step, int x, int y) => Placed(Anchor(step), x, y);

    private static ScreenPoint Placed(ScreenPoint? origin, int x, int y)
        => origin is { } corner ? new ScreenPoint(corner.X + x, corner.Y + y) : new ScreenPoint(x, y);

    /// <summary>The colour of the pixel a step points at, counted from wherever it anchors.</summary>
    private PixelColor PixelAt(ExecutableStep step)
    {
        var corner = Place(step, Number(step, "x"), Number(step, "y"));
        return _devices.Screen.PixelAt(corner.X, corner.Y);
    }

    /// <summary>
    /// Where a mouse action lands. An empty position means "wherever the pointer already is",
    /// which is what a click or a press with no coordinates written down means.
    /// </summary>
    private ScreenPoint Point(ExecutableStep step, string xName, string yName)
    {
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
    private void DragPointer(ExecutableStep step)
    {
        // Both ends are measured from the same corner, so a drag across a window stays inside it.
        var origin = Anchor(step);
        var from = Placed(origin, Number(step, "startX"), Number(step, "startY"));
        var to = Placed(origin, Number(step, "endX"), Number(step, "endY"));
        var duration = Pace(Number(step, "durationMs"));
        var steps = Math.Clamp(Number(step, "steps"), 1, 200);
        var route = MousePath.Route(step.Text("style"));
        if (route is MouseRoute.Direct)
        {
            Input(step).Drag(Button(step), from.X, from.Y, to.X, to.Y, duration, steps);
            return;
        }

        if (duration <= 0)
        {
            duration = Pace(BorrowedMoveMs);
        }

        Input(step).DragAlong(Button(step), MousePath.Plan(route, from, to, steps), duration);
    }

    private static IReadOnlyList<string> Keys(ExecutableStep step)
        => [.. step.Text("keys").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Copies a region of the screen into a variable the macro can look at again.</summary>
    private void Capture(ExecutableStep step, int depth)
    {
        var name = step.Text("saveTo").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure("Run.MissingVariable");
        }

        // The corner is worked out first so that the rectangle written into the variables is the
        // one on screen, which is what a later "region" that names this picture has to parse.
        var corner = Place(step, Number(step, "x"), Number(step, "y"));
        var width = Math.Max(1, Number(step, "width"));
        var height = Math.Max(1, Number(step, "height"));
        var frame = _devices.Screen.Capture(corner.X, corner.Y, width, height);

        _images[name] = frame;
        Variables.Set(name, Value.FromText($"<image {frame.Width}x{frame.Height}>"));

        // The rectangle the picture covers, so it can be searched or compared by name.
        Variables.Set(name + ".x", Value.FromNumber(corner.X));
        Variables.Set(name + ".y", Value.FromNumber(corner.Y));
        Variables.Set(name + ".width", Value.FromNumber(width));
        Variables.Set(name + ".height", Value.FromNumber(height));
        Log(LogLevel.Info, depth, step.Type, "Run.Capture", name, $"{frame.Width}x{frame.Height}");
    }

    private void GetPixel(ExecutableStep step, int depth)
    {
        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "color";
        }

        var colour = PixelAt(step);
        var asHex = !string.Equals(step.Text("asHex").Trim(), "false", StringComparison.OrdinalIgnoreCase);
        Variables.Set(name, asHex
            ? Value.FromText(colour.ToHex())
            : Value.FromNumber((colour.R) | (colour.G << 8) | (colour.B << 16)));

        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, colour.ToHex());
    }

    private async Task WaitColor(ExecutableStep step, int depth, CancellationToken token)
    {
        var target = PixelColor.Parse(step.Text("color"));
        var tolerance = Read(step.Text("tolerance")).AsNumber();
        var timeout = Math.Max(0, Number(step, "timeoutMs"));

        // The corner is looked up again on every check, so a wait that watches a window keeps
        // watching the same spot inside it even if the window is moved while the macro waits.
        var seen = await WaitForFlagAsync(
            () => PixelAt(step).Matches(target, tolerance), timeout, 50, token);
        if (!seen)
        {
            throw new StepFailure("Run.WaitColorTimeout", target.ToHex());
        }

        Log(LogLevel.Info, depth, step.Type, "Run.SawColor", target.ToHex());
    }

    /// <summary>
    /// Looks for a colour in the search areas and writes where it was found. A step with a timeout
    /// waits for the colour to turn up and fails when it never does, the way waiting for a picture
    /// does; one without looks once and empties the result instead, the way finding a picture does.
    /// </summary>
    private async Task FindColor(ExecutableStep step, int depth, CancellationToken token)
    {
        var target = PixelColor.Parse(step.Text("color"));
        var tolerance = Read(step.Text("tolerance")).AsNumber();
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var name = VariableName(step, "resultVariable", "match");

        var hits = await ColourHits(step, target, tolerance, timeout, token);
        if (hits.Count == 0 && timeout > 0)
        {
            throw new StepFailure("Run.WaitColorTimeout", target.ToHex());
        }

        var match = Chosen(hits, step);
        if (match is null)
        {
            StoreMiss(name);
        }
        else
        {
            StoreMatch(name, match.Center, match.Size, match.Score);
        }

        Remember(name, hits, Flag(step, "allMatches", false));
        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ImageMissing" : "Run.ImageFound",
            name, match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    /// <summary>
    /// The pixels of the search areas that show the colour, in reading order, waited for when the
    /// step asked for one. Each hit is a single pixel: its centre is the pixel itself, and its score
    /// says how close the colour was, one being exact.
    /// </summary>
    private async Task<List<ImageMatch>> ColourHits(ExecutableStep step, PixelColor target,
        double tolerance, int timeout, CancellationToken token)
    {
        var hits = Scan(step, target, tolerance);
        if (hits.Count > 0 || timeout <= 0)
        {
            return hits;
        }

        var interval = Number(step, "intervalMs");
        if (interval <= 0)
        {
            interval = 200;
        }

        // The areas are captured again on every pass, so a wait keeps watching a window that is
        // moving rather than the place it used to be.
        List<ImageMatch>? Look() => Scan(step, target, tolerance) is { Count: > 0 } found ? found : null;

        return await WaitForValueAsync(Look, timeout, interval, token) ?? hits;
    }

    /// <summary>One pass over the search areas, in reading order.</summary>
    private List<ImageMatch> Scan(ExecutableStep step, PixelColor target, double tolerance)
    {
        var wanted = Wanted(step);
        var hits = new List<ImageMatch>();
        foreach (var (area, origin) in SearchAreas(step))
        {
            foreach (var point in PixelSearch.Find(area, target, tolerance, wanted))
            {
                hits.Add(new ImageMatch(
                    1 - area[point.X, point.Y].DistanceTo(target),
                    new ScreenPoint(origin.X + point.X, origin.Y + point.Y),
                    new ScreenSize(1, 1)));
            }
        }

        hits.Sort(Reading);
        return hits;
    }

    /// <summary>Looks once and writes where the picture was, or an empty value when it was not.</summary>
    private void LookFor(ExecutableStep step, int depth)
    {
        var hits = Hits(step);
        var match = Chosen(hits, step);
        var name = VariableName(step, "resultVariable", "match");

        if (match is null)
        {
            StoreMiss(name);
        }
        else
        {
            StoreMatch(name, match.Center, match.Size, match.Score);
        }

        Remember(name, hits, Flag(step, "allMatches", false));
        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ImageMissing" : "Run.ImageFound",
            name, match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    private async Task WaitForImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var found = await HitsUntil(step, token)
                    ?? throw new StepFailure("Run.ImageNotFound", step.Text("image"));

        var name = VariableName(step, "resultVariable", "match");
        StoreMatch(name, found.Match.Center, found.Match.Size, found.Match.Score);
        Remember(name, found.Hits, Flag(step, "allMatches", false));
        Log(LogLevel.Info, depth, step.Type, "Run.ImageFound", name,
            $"{found.Match.Center.X},{found.Match.Center.Y}");
    }

    private async Task ClickImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var found = await HitsUntil(step, token)
                    ?? throw new StepFailure("Run.ImageNotFound", step.Text("image"));

        var x = found.Match.Center.X + Number(step, "offsetX");
        var y = found.Match.Center.Y + Number(step, "offsetY");
        Input(step).Click(Button(step), x, y, 1, 0);
        Log(LogLevel.Info, depth, step.Type, "Run.ClickedImage", x, y);
    }

    /// <summary>One attempt at finding a reference picture: the hit the step asked for.</summary>
    private ImageMatch? Search(ExecutableStep step) => Chosen(Hits(step), step);

    /// <summary>
    /// Every place the reference picture appears in the search areas, in screen coordinates and in
    /// reading order: down the screen first, then across. That is the order a person counts them in
    /// when looking at a screenshot, which is what "the third one" has to mean to be useful.
    /// </summary>
    private List<ImageMatch> Hits(ExecutableStep step)
    {
        var needle = Reference(step);
        var confidence = Number(step, "confidence");
        if (confidence <= 0)
        {
            confidence = 90;
        }

        var wanted = Wanted(step);
        var hits = new List<ImageMatch>();
        foreach (var (area, origin) in SearchAreas(step))
        {
            foreach (var found in _devices.Vision.FindAll(area, needle, confidence, wanted))
            {
                hits.Add(found with
                {
                    Location = new ScreenPoint(found.Location.X + origin.X, found.Location.Y + origin.Y),
                });
            }
        }

        hits.Sort(Reading);
        return hits;
    }

    /// <summary>The hits a wait ended on: the whole set and the one the step asked for.</summary>
    private sealed record Found(List<ImageMatch> Hits, ImageMatch Match);

    /// <summary>The hits, once there are enough of them, or null when the wait ran out.</summary>
    private async Task<Found?> HitsUntil(ExecutableStep step, CancellationToken token)
    {
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var interval = Number(step, "intervalMs");
        if (interval <= 0)
        {
            interval = 200;
        }

        Found? Enough()
        {
            var hits = Hits(step);
            return Chosen(hits, step) is { } match ? new Found(hits, match) : null;
        }

        return await WaitForValueAsync(Enough, timeout, interval, token);
    }

    /// <summary>How many hits to look for: the one the step asked for, or the whole list.</summary>
    private int Wanted(ExecutableStep step) => Flag(step, "allMatches", false)
        ? MatchLimit
        : Index(step, "matchIndex", 1);

    /// <summary>The hit a step wants, counted from the top left, or null when there are not that many.</summary>
    private ImageMatch? Chosen(IReadOnlyList<ImageMatch> hits, ExecutableStep step)
    {
        var index = Index(step, "matchIndex", 1);
        return index <= hits.Count ? hits[index - 1] : null;
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
    /// Notes how many places matched and where they all are, for the steps that asked for the whole
    /// list. Each entry is one "x,y" centre, so the list can be counted, taken apart and looped over
    /// with the list functions.
    /// </summary>
    private void Remember(string name, IReadOnlyList<ImageMatch> hits, bool every)
        => Remember(name, [.. hits.Select(hit => hit.Center)], every);

    /// <summary>The same, for hits that are only places: the elements a search turned up.</summary>
    private void Remember(string name, IReadOnlyList<ScreenPoint> centres, bool every)
    {
        if (!every)
        {
            return;
        }

        Variables.Set(name + ".count", Value.FromNumber(centres.Count));
        Variables.Set(name + ".list", Value.FromList(
            centres.Select(centre => Value.FromText($"{centre.X},{centre.Y}"))));
    }

    /// <summary>
    /// The reference picture to look for: something an earlier Capture saved, or a file on disk.
    /// </summary>
    private ImageFrame Reference(ExecutableStep step)
    {
        var text = step.Text("image").Trim();
        if (text.Length == 0)
        {
            throw new StepFailure("Run.MissingImage", string.Empty);
        }

        // A picture an earlier Capture saved, named with or without the dollar sign so it reads
        // the same as every other field a variable can go in.
        var named = text.StartsWith('$') ? text[1..].Trim() : text;
        if (_images.TryGetValue(named, out var captured))
        {
            return captured;
        }

        // Otherwise it is a file, either written out or held in a variable.
        return _devices.Vision.Load(Read(text).AsText()) ?? throw new StepFailure("Run.MissingImage", text);
    }

    /// <summary>
    /// The places to search: the whole screen unless the step names rectangles, each of which may be
    /// counted from a window's corner when the step says so.
    /// </summary>
    private IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> SearchAreas(ExecutableStep step)
    {
        // The rectangles may be written out, held in a variable, or built from several of them.
        // They are only interpolated: the commas would stop an expression at the first number.
        var text = Interpolate(step.Text("region")).Trim();
        if (text.Length == 0)
        {
            var size = _devices.Screen.PrimarySize;
            return [(_devices.Screen.Capture(0, 0, size.Width, size.Height), new ScreenPoint(0, 0))];
        }

        // Several rectangles are separated by a semicolon or a line break, so one step can look at
        // two windows, or at two halves of one, without the macro having to become two steps.
        var areas = new List<(ImageFrame, ScreenPoint)>();
        foreach (var rectangle in text.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            areas.Add(Area(step, rectangle));
        }

        if (areas.Count == 0)
        {
            throw new StepFailure("Run.BadRegion", text);
        }

        return areas;
    }

    /// <summary>One written rectangle, captured where on the screen it says.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Area(ExecutableStep step, string rectangle)
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

        // A window-anchored rectangle is counted from that window's corner, and the origin handed
        // back is the one on screen, so the positions read out of the picture are pixels the rest
        // of the macro can click on.
        var corner = Place(step, x, y);
        return (_devices.Screen.Capture(corner.X, corner.Y, width, height), corner);
    }

    // --------------------------------------------------------------------- files

    /// <summary>A path a step named, with any <c>$variable</c> already filled in.</summary>
    private string PathOf(ExecutableStep step, string parameter = "path")
        => Read(step.Text(parameter)).AsText();

    private static string VariableName(ExecutableStep step, string parameter, string fallback)
    {
        var text = step.Text(parameter).Trim();
        return text.Length == 0 ? fallback : text;
    }

    private static readonly JsonSerializerOptions Writable = new() { WriteIndented = true };

    private void ReadTextFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var text = _devices.Files.ReadText(path);
        Variables.Set(VariableName(step, "resultVariable", "text"), Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, "Run.ReadFile", path, text.Length);
    }

    private void WriteTextFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var text = Read(step.Text("text")).AsText();
        var append = string.Equals(step.Text("mode").Trim(), "append", StringComparison.OrdinalIgnoreCase);

        _devices.Files.WriteText(path, text, append);
        Log(LogLevel.Info, depth, step.Type, append ? "Run.AppendedFile" : "Run.WroteFile", path, text.Length);
    }

    private void FileExists(ExecutableStep step, int depth)
    {
        var found = _devices.Files.Exists(PathOf(step));
        var name = VariableName(step, "resultVariable", "exists");
        Variables.Set(name, Value.FromBool(found));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, found ? "true" : "false");
    }

    private void DeleteFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        _devices.Files.Delete(path);
        Log(LogLevel.Info, depth, step.Type, "Run.DeletedFile", path);
    }

    private void CopyFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var to = PathOf(step, "to");
        var overwrite = !string.Equals(step.Text("overwrite").Trim(), "false", StringComparison.OrdinalIgnoreCase);

        _devices.Files.Copy(from, to, overwrite);
        Log(LogLevel.Info, depth, step.Type, "Run.CopiedFile", from, to);
    }

    private void ListFiles(ExecutableStep step, int depth)
    {
        var folder = PathOf(step, "folder");
        var recurse = Flag(step, "recurse", false);
        var files = _devices.Files.List(folder, step.Text("pattern").Trim(), recurse);

        Variables.Set(VariableName(step, "resultVariable", "files"),
            Value.FromList(files.Select(Value.FromText)));
        Log(LogLevel.Info, depth, step.Type, "Run.ListedFiles", folder, files.Count);
    }

    private void ReadJson(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var document = Json(path, _devices.Files.ReadText(path));
        var query = step.Text("query").Trim();
        var node = query.Length == 0
            ? document
            : Select(document, query) ?? throw new StepFailure("Run.BadJsonPath", query);

        var name = VariableName(step, "resultVariable", "value");
        var value = FromJson(node);
        Variables.Set(name, value);
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, value.AsText());
    }

    private void WriteJson(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var query = step.Text("query").Trim();
        if (query.Length == 0)
        {
            throw new StepFailure("Run.MissingJsonPath");
        }

        var document = _devices.Files.Exists(path)
            ? Json(path, _devices.Files.ReadText(path))
            : new JsonObject();

        Assign(document, query, ToJson(Read(step.Text("value"))));

        var text = document.ToJsonString(Writable);
        _devices.Files.WriteText(path, text, false);
        Log(LogLevel.Info, depth, step.Type, "Run.WroteFile", path, text.Length);
    }

    private void ReadCsv(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var rows = SplitCsv(_devices.Files.ReadText(path), Separator(step.Text("separator")));
        var skip = !string.Equals(step.Text("hasHeader").Trim(), "false", StringComparison.OrdinalIgnoreCase);
        var body = skip && rows.Count > 0 ? rows.Skip(1) : rows;

        Variables.Set(VariableName(step, "resultVariable", "rows"),
            Value.FromList(body.Select(row => Value.FromList(row.Select(Value.FromText)))));
        Log(LogLevel.Info, depth, step.Type, "Run.ReadFile", path, rows.Count);
    }

    private void WriteCsv(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var separator = Separator(step.Text("separator"));
        var rows = Read(step.Text("rows"));
        if (!rows.IsList)
        {
            throw new StepFailure("Run.NotAList", step.Text("rows"));
        }

        var text = string.Join(Environment.NewLine, rows.Items.Select(row => CsvRow(row, separator)));
        _devices.Files.WriteText(path, text, false);
        Log(LogLevel.Info, depth, step.Type, "Run.WroteFile", path, text.Length);
    }

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
        _devices.Files.WriteText(path, text, false);
        Log(LogLevel.Info, depth, step.Type, "Run.SavedVariables", path, document.Count);
    }

    private void LoadVariables(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var document = Json(path, _devices.Files.ReadText(path));
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
        var text = query.Trim();
        var index = 0;

        while (index < text.Length && node is not null)
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

                node = Step(node, text[(index + 1)..close].Trim().Trim('\'', '"'));
                index = close + 1;
                continue;
            }

            var end = index;
            while (end < text.Length && text[end] != '.' && text[end] != '[')
            {
                end++;
            }

            node = Step(node, text[index..end]);
            index = end;
        }

        return node;
    }

    private static JsonNode? Step(JsonNode? node, string key)
    {
        if (node is null)
        {
            return null;
        }

        if (int.TryParse(key, out var position))
        {
            return node is JsonArray array && position >= 0 && position < array.Count ? array[position] : null;
        }

        return node is JsonObject parent && parent.TryGetPropertyValue(key, out var child) ? child : null;
    }

    /// <summary>
    /// Sets a value at a dotted path, making the objects along the way. A path written with an
    /// index is refused: writing into an array is not something a macro can say clearly.
    /// </summary>
    private static void Assign(JsonNode root, string query, JsonNode? value)
    {
        var parts = query.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => part.Contains('[')))
        {
            throw new StepFailure("Run.BadJsonPath", query);
        }

        var node = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (node is not JsonObject parent)
            {
                throw new StepFailure("Run.BadJsonPath", query);
            }

            if (parent[parts[index]] is JsonObject child)
            {
                node = child;
                continue;
            }

            var created = new JsonObject();
            parent[parts[index]] = created;
            node = created;
        }

        if (node is not JsonObject target)
        {
            throw new StepFailure("Run.BadJsonPath", query);
        }

        target[parts[^1]] = value;
    }

    private static JsonNode? ToJson(Value value)
    {
        switch (value.Kind)
        {
            case ValueKind.Null:
                return null;
            case ValueKind.Number:
                return JsonValue.Create(value.Number);
            case ValueKind.Bool:
                return JsonValue.Create(value.Flag);
            case ValueKind.List:
                {
                    var array = new JsonArray();
                    foreach (var item in value.Items)
                    {
                        array.Add(ToJson(item));
                    }

                    return array;
                }
            default:
                return JsonValue.Create(value.Text);
        }
    }

    private static Value FromJson(JsonNode? node) => node switch
    {
        null => Value.Null,
        JsonArray array => Value.FromList(array.Select(FromJson)),
        JsonObject obj => Value.FromText(obj.ToJsonString()),
        JsonValue value => value.TryGetValue<bool>(out var flag) ? Value.FromBool(flag)
            : value.TryGetValue<double>(out var number) ? Value.FromNumber(number)
            : Value.FromText(value.TryGetValue<string>(out var text) ? text : value.ToJsonString()),
        _ => Value.Null,
    };

    /// <summary>The character between two CSV cells.</summary>
    private static char Separator(string text) => text.Trim().ToLowerInvariant() switch
    {
        "semicolon" or ";" => ';',
        "tab" or "\\t" => '\t',
        "pipe" or "|" => '|',
        _ => ',',
    };

    /// <summary>
    /// Splits CSV text into rows of cells, honouring quoted cells, doubled quotes inside them
    /// and line breaks that are part of a cell.
    /// </summary>
    private static List<List<string>> SplitCsv(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (quoted)
            {
                if (character != '"')
                {
                    cell.Append(character);
                }
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    if (character == separator)
                    {
                        row.Add(cell.ToString());
                        cell.Clear();
                    }
                    else
                    {
                        cell.Append(character);
                    }

                    break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static string CsvRow(Value row, char separator)
        => row.IsList
            ? string.Join(separator, row.Items.Select(cell => CsvCell(cell.AsText(), separator)))
            : CsvCell(row.AsText(), separator);

    private static string CsvCell(string text, char separator)
        => text.Contains(separator) || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;

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
        var name = ClipboardName(step);
        Variables.Set(name, Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, "Run.ReadClipboard", name, text.Length);
    }

    /// <summary>Waits for the clipboard to change, then keeps whatever landed on it.</summary>
    private async Task ClipboardWait(ExecutableStep step, int depth, CancellationToken token)
    {
        await AwaitClipboard(step, _devices.Clipboard.ChangeCount, token);

        var name = step.Text("resultVariable").Trim();
        var text = _devices.Clipboard.ReadText();
        if (name.Length > 0)
        {
            Variables.Set(name, Value.FromText(text));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ClipboardChanged", name, text.Length);
    }

    /// <summary>Presses the copy shortcut, then waits for what it put on the clipboard.</summary>
    private async Task ClipboardCopy(ExecutableStep step, int depth, CancellationToken token)
    {
        var before = _devices.Clipboard.ChangeCount;
        Input(step).Hotkey(Keys(step), 50);
        await AwaitClipboard(step, before, token);

        var text = _devices.Clipboard.ReadText();
        var name = step.Text("resultVariable").Trim();
        if (name.Length > 0)
        {
            Variables.Set(name, Value.FromText(text));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.CopiedFromWindow", name, text.Length);
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

    private static string ClipboardName(ExecutableStep step)
    {
        var text = step.Text("resultVariable").Trim();
        return text.Length == 0 ? "clipboard" : text;
    }

    // -------------------------------------------------------------------- process

    /// <summary>Starts another program and keeps its process id.</summary>
    private void StartProcess(ExecutableStep step, int depth)
    {
        var program = Read(step.Text("file")).AsText();
        var arguments = Read(step.Text("arguments")).AsText();
        var folder = Read(step.Text("workingDirectory")).AsText();
        var hidden = Flag(step, "hidden", true);

        var id = _devices.Processes.Start(program, arguments, folder, hidden);
        var name = VariableName(step, "resultVariable", "processId");
        Variables.Set(name, Value.FromNumber(id));
        Log(LogLevel.Info, depth, step.Type, "Run.StartedProgram", program, id);
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

        var variable = VariableName(step, "resultVariable", string.Empty);
        if (variable.Length > 0)
        {
            Variables.Set(variable, Value.FromNumber(ids[0]));
        }

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
        var name = VariableName(step, "resultVariable", "exitCode");
        Variables.Set(name, Value.FromNumber(code));
        Log(LogLevel.Info, depth, step.Type, "Run.ProcessExited", id, code);
    }

    /// <summary>Leaves true or false behind, depending on whether the program is running.</summary>
    private void ProgramRunning(ExecutableStep step, int depth)
    {
        var name = Read(step.Text("name")).AsText();
        var running = _devices.Processes.Find(name).Count > 0;
        var variable = VariableName(step, "resultVariable", "running");
        Variables.Set(variable, Value.FromBool(running));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", variable, running ? "true" : "false");
    }

    /// <summary>Collects the names of the running programs into a list.</summary>
    private void ListPrograms(ExecutableStep step, int depth)
    {
        var programs = _devices.Processes.List();
        var variable = VariableName(step, "resultVariable", "processes");
        Variables.Set(variable, Value.FromList(programs.Select(Value.FromText)));
        Log(LogLevel.Info, depth, step.Type, "Run.ListedPrograms", programs.Count);
    }

    /// <summary>Closes a program, by name or by process id.</summary>
    private void StopProgram(ExecutableStep step, int depth)
    {
        var target = Read(step.Text("target")).AsText().Trim();
        var force = Flag(step, "force", false);

        var stopped = int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? (_devices.Processes.StopById(id, force) ? 1 : 0)
            : _devices.Processes.StopByName(target, force);

        var variable = VariableName(step, "resultVariable", "stopped");
        Variables.Set(variable, Value.FromNumber(stopped));
        Log(LogLevel.Info, depth, step.Type, "Run.StoppedProgram", target, stopped);
    }

    /// <summary>Runs a command line to the end and keeps what it printed.</summary>
    private void RunCommand(ExecutableStep step, int depth)
    {
        var program = Read(step.Text("file")).AsText();
        var arguments = Read(step.Text("arguments")).AsText();
        var folder = Read(step.Text("workingDirectory")).AsText();
        var timeout = OptionalNumber(step, "timeoutMs", 30000);

        var result = _devices.Processes.Run(program, arguments, folder, timeout);

        Store(step, "resultVariable", "output", Value.FromText(result.StandardOutput));
        Store(step, "errorVariable", string.Empty, Value.FromText(result.StandardError));
        Store(step, "exitCodeVariable", "exitCode", Value.FromNumber(result.ExitCode));

        Log(LogLevel.Info, depth, step.Type, "Run.CommandFinished", program, result.ExitCode);
    }

    // --------------------------------------------------------------------- system

    /// <summary>Reads a fact about the machine into a variable.</summary>
    private void SystemInfo(ExecutableStep step, int depth)
    {
        var value = _devices.System.Info(step.Text("field").Trim());
        var name = VariableName(step, "resultVariable", "info");
        Variables.Set(name, Value.FromText(value));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, value);
    }

    /// <summary>Reads an environment variable into a variable.</summary>
    private void EnvironmentVariable(ExecutableStep step, int depth)
    {
        var name = Read(step.Text("name")).AsText();
        var value = _devices.System.Environment(name);
        var variable = VariableName(step, "resultVariable", "value");
        Variables.Set(variable, Value.FromText(value));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", variable, value);
    }

    /// <summary>Stores a value under a variable a step named, unless it named none.</summary>
    private void Store(ExecutableStep step, string parameter, string fallback, Value value)
    {
        var name = step.Text(parameter).Trim();
        if (name.Length == 0 && fallback.Length == 0)
        {
            return;
        }

        Variables.Set(name.Length == 0 ? fallback : name, value);
    }

    /// <summary>
    /// Writes a match so a macro can read its parts: the centre as <c>name</c> ("x,y", the way
    /// every coordinate field is written) and each part again as <c>name.x</c>, <c>name.y</c>,
    /// <c>name.width</c>, <c>name.height</c> and <c>name.score</c>.
    /// </summary>
    private void StoreMatch(string name, ScreenPoint centre, ScreenSize size, double score,
        string text = "")
    {
        Variables.Set(name, Value.FromText($"{centre.X},{centre.Y}"));
        Variables.Set(name + ".x", Value.FromNumber(centre.X));
        Variables.Set(name + ".y", Value.FromNumber(centre.Y));
        Variables.Set(name + ".width", Value.FromNumber(size.Width));
        Variables.Set(name + ".height", Value.FromNumber(size.Height));
        Variables.Set(name + ".score", Value.FromNumber(score));

        if (text.Length > 0)
        {
            Variables.Set(name + ".text", Value.FromText(text));
        }
    }

    /// <summary>
    /// Empties a match that was not found, parts included, so a step inside a loop never reads
    /// the position the previous pass left behind.
    /// </summary>
    private void StoreMiss(string name)
    {
        Variables.Set(name, Value.FromText(string.Empty));
        foreach (var part in MatchParts)
        {
            Variables.Set(name + part, Value.FromText(string.Empty));
        }
    }

    /// <summary>The parts a match is broken into, named after the dot in <c>match.x</c>.</summary>
    private static readonly string[] MatchParts =
        [".x", ".y", ".width", ".height", ".score", ".text", ".count", ".list"];

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

    /// <summary>Leaves true or false behind, depending on whether the window is open.</summary>
    private void WindowExists(ExecutableStep step, int depth)
    {
        var title = Read(step.Text("title")).AsText();
        var found = _devices.Windows.Find(title) is not null;
        var name = VariableName(step, "resultVariable", "found");

        Variables.Set(name, Value.FromBool(found));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, found ? "true" : "false");
    }

    /// <summary>Waits until a window with the given title appears.</summary>
    private async Task WaitForWindow(ExecutableStep step, int depth, CancellationToken token)
    {
        var title = Read(step.Text("title")).AsText();
        var timeout = OptionalNumber(step, "timeoutMs", 10000);

        var window = await WaitForValueAsync(() => _devices.Windows.Find(title), timeout, 100, token);
        if (window is null)
        {
            throw new StepFailure("Run.WindowTimeout", title);
        }

        var name = VariableName(step, "resultVariable", string.Empty);
        if (name.Length > 0)
        {
            Variables.Set(name, Value.FromText(window.Title));
        }

        Log(LogLevel.Info, depth, step.Type, "Run.WindowAppeared", window.Title);
    }

    /// <summary>Collects the titles of the open windows into a list.</summary>
    private void ListWindows(ExecutableStep step, int depth)
    {
        var titles = _devices.Windows
            .List()
            .Select(window => window.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var name = VariableName(step, "resultVariable", "windows");
        Variables.Set(name, Value.FromList(titles.Select(Value.FromText)));
        Log(LogLevel.Info, depth, step.Type, "Run.ListedWindows", titles.Count);
    }

    /// <summary>Does something to the window a step names, or fails the step when it is not open.</summary>
    private void Act(ExecutableStep step, int depth, string message, Func<WindowInfo, bool> action)
    {
        var title = Read(step.Text("title")).AsText();
        var window = _devices.Windows.Find(title)
                     ?? throw new StepFailure("Run.WindowNotFound", title);

        if (!action(window))
        {
            throw new StepFailure("Run.WindowFailed", window.Title);
        }

        Log(LogLevel.Info, depth, step.Type, message, window.Title);
    }

    // ---------------------------------------------------------------------- ocr

    /// <summary>
    /// Reads the text in a step's search areas, with the positions in screen coordinates. Several
    /// areas are read one after another and what they hold is handed back together.
    /// </summary>
    private IReadOnlyList<TextSpan> ReadSpans(ExecutableStep step)
    {
        var language = Language(step);
        var spans = new List<TextSpan>();
        foreach (var (area, origin) in SearchAreas(step))
        {
            spans.AddRange(_devices.Ocr.Recognize(area, language).Select(span => span with
            {
                Location = new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y),
            }));
        }

        return spans;
    }

    private void Recognize(ExecutableStep step, int depth)
    {
        var corner = Place(step, Number(step, "x"), Number(step, "y"));
        var area = _devices.Screen.Capture(corner.X, corner.Y,
            Math.Max(1, Number(step, "width")), Math.Max(1, Number(step, "height")));

        var spans = _devices.Ocr.Recognize(area, Language(step));
        var text = string.Join(' ', spans.Select(span => span.Text));

        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "text";
        }

        Variables.Set(name, Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, text);
    }

    private void FindText(ExecutableStep step, int depth)
    {
        var wanted = Read(step.Text("text")).AsText();
        var mode = step.Text("matchMode");
        var span = ReadSpans(step).FirstOrDefault(candidate => Matches(candidate.Text, wanted, mode));

        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "match";
        }

        if (span is null)
        {
            StoreMiss(name);
        }
        else
        {
            StoreMatch(name, span.Center, span.Size, span.Confidence, span.Text);
        }

        Log(LogLevel.Info, depth, step.Type, span is null ? "Run.TextMissing" : "Run.TextFound",
            name, span is null ? string.Empty : $"{span.Center.X},{span.Center.Y}");
    }

    private async Task ClickText(ExecutableStep step, int depth, CancellationToken token)
    {
        var wanted = Read(step.Text("text")).AsText();
        var mode = step.Text("matchMode");
        var timeout = Math.Max(0, Number(step, "timeoutMs"));

        var span = await WaitForValueAsync(
            () => ReadSpans(step).FirstOrDefault(candidate => Matches(candidate.Text, wanted, mode)),
            timeout, 200, token);
        if (span is null)
        {
            throw new StepFailure("Run.TextNotFound", wanted);
        }

        var x = span.Center.X + Number(step, "offsetX");
        var y = span.Center.Y + Number(step, "offsetY");
        Input(step).Click(Button(step), x, y, 1, 0);
        Log(LogLevel.Info, depth, step.Type, "Run.ClickedText", wanted, $"{x},{y}");
    }

    private static string Language(ExecutableStep step)
    {
        var language = step.Text("language").Trim();
        return language.Length == 0 ? "auto" : language;
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
        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "exists";
        }

        Variables.Set(name, Value.FromBool(found));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, found ? "true" : "false");
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
        var name = VariableName(step, "resultVariable", "element");
        var every = Flag(step, "allMatches", false);
        var index = Index(step, "matchIndex", 1);

        // Asking for the whole list is what makes the list worth collecting; otherwise the search
        // only has to reach as far as the hit the step named.
        var hits = _devices.Ui.FindAll(Query(step), every ? MatchLimit : index);
        var match = index <= hits.Count ? hits[index - 1] : null;

        if (match is null)
        {
            StoreMiss(name);
        }
        else
        {
            StoreMatch(name, Centre(match), match.Size, 1, match.Name);
        }

        Remember(name, [.. hits.Select(Centre)], every);
        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ElementMissing" : "Run.ElementWhere",
            name, match is null ? string.Empty : $"{Centre(match).X},{Centre(match).Y}");
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

    private void GetElementText(ExecutableStep step, int depth)
    {
        var text = _devices.Ui.GetText(Query(step)) ?? string.Empty;
        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "text";
        }

        Variables.Set(name, Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, "Run.Set", name, text);
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
    /// Reads a table into a variable as a list of rows, each row a list of cells, which is the
    /// same shape the CSV reader hands back so the same steps can walk either one.
    /// </summary>
    private void ReadElementTable(ExecutableStep step, int depth)
    {
        // A step with no row limit at all still gets one, the same default the editor shows, so a
        // table nobody bounded cannot be read forever.
        var limit = Math.Clamp(OptionalNumber(step, "maxRows", DefaultRows), 1, MaxRows);
        var rows = _devices.Ui.ReadTable(Query(step), limit);

        var name = VariableName(step, "resultVariable", "table");
        Variables.Set(name, Value.FromList(
            rows.Select(row => Value.FromList(row.Select(Value.FromText)))));
        Log(LogLevel.Info, depth, step.Type, "Run.ReadTable", name, rows.Count);
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
    /// Runs a short script through an interpreter the machine already has. This is the block that
    /// covers whatever the catalogue does not: anything with a command line of its own can be
    /// scripted, and the script talks to the macro both ways — values go in as <c>{{name}}</c> in
    /// the text, and whatever the script prints comes back in the result variable.
    /// </summary>
    private void RunScript(ExecutableStep step, int depth)
    {
        var language = step.Text("language").Trim().ToLowerInvariant();
        var temp = Path.Combine(Path.GetTempPath(), "Viktor");
        var path = Path.Combine(temp, $"script-{Guid.NewGuid():N}{ScriptExtension(language)}");
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
        var (program, arguments) = ScriptCommand(language, path, extra);

        // A step that says nothing about time still gets one, the same way running a command does:
        // a timeout of nothing would end the script the moment it started.
        var timeout = Math.Max(1, OptionalNumber(step, "timeoutMs", DefaultScriptMs));

        _devices.Files.WriteText(path, script, false);

        CommandResult result;
        try
        {
            result = _devices.Processes.Run(program, arguments, folder, timeout);
        }
        finally
        {
            // The script itself lives in the macro, so the copy on disk is only there for as long
            // as the interpreter needs it. A tidy-up that fails is not worth failing the step over.
            try
            {
                _devices.Files.Delete(path);
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
        Variables.Set(VariableName(step, "resultVariable", "output"), Value.FromText(said));
        Log(LogLevel.Info, depth, step.Type, "Run.RanScript", language, said.Length);
    }

    /// <summary>
    /// The command line that runs a script of this kind. PowerShell is given the flags that stop it
    /// from loading a profile or asking anything, so a script that runs inside a macro behaves the
    /// same way every time it runs.
    /// </summary>
    private static (string Program, string Arguments) ScriptCommand(string language, string path,
        string extra)
    {
        var quoted = $"\"{path}\"";
        var tail = extra.Length == 0 ? string.Empty : $" {extra}";

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

    /// <summary>The extension the interpreter expects, which is how it knows what it is reading.</summary>
    private static string ScriptExtension(string language) => language switch
    {
        "cmd" => ".cmd",
        "node" => ".js",
        "python" => ".py",
        _ => ".ps1",
    };

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

    /// <summary>True when the text can only be meant as an expression.</summary>
    private static bool LooksLikeExpression(string text)
        => text[0] is '$' or '[' or '(' || text.Contains('(');

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
            return (int)Read(step.Text(name)).AsNumber();
        }
        catch (ExpressionException)
        {
            return 0;
        }
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
    /// up after <paramref name="timeoutMs"/>. Every wait in the engine comes through here: waiting
    /// is the same thing whether it is a colour, a picture, an element, a window or a condition the
    /// macro wrote itself, so the pacing and the way a timeout is counted are written once. The
    /// question is asked before the clock is read, so a wait that already holds costs nothing, and
    /// a timeout of zero means "look once and give up".
    /// </summary>
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
