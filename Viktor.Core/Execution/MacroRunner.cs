using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                await Pause(Pace(step.Meta.RetryDelayMs), token);
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
                    await Pause(Pace(step.Meta.RetryDelayMs), token);
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
                _devices.Input.KeyPress(step.Text("key"), Pace(Number(step, "holdMs")));
                return Signal.Normal;

            case "input.keyDown":
                _devices.Input.KeyDown(step.Text("key"));
                return Signal.Normal;

            case "input.keyUp":
                _devices.Input.KeyUp(step.Text("key"));
                return Signal.Normal;

            case "input.hotkey":
                _devices.Input.Hotkey(Keys(step), Pace(Number(step, "holdMs")));
                return Signal.Normal;

            case "input.typeText":
                _devices.Input.TypeText(Read(step.Text("text")).AsText(), Pace(Number(step, "intervalMs")));
                return Signal.Normal;

            case "input.mouseMove":
                _devices.Input.MoveMouse(Number(step, "x"), Number(step, "y"),
                    Pace(Number(step, "durationMs")));
                return Signal.Normal;

            case "input.mouseMoveRelative":
                _devices.Input.MoveMouseRelative(Number(step, "dx"), Number(step, "dy"),
                    Pace(Number(step, "durationMs")));
                return Signal.Normal;

            case "input.mouseClick":
            {
                var point = Point(step, "x", "y");
                _devices.Input.Click(Button(step), point.X, point.Y,
                    Math.Max(1, Number(step, "clicks")), Pace(Number(step, "intervalMs")));
                return Signal.Normal;
            }

            case "input.mouseDoubleClick":
            {
                var point = Point(step, "x", "y");
                _devices.Input.Click(Button(step), point.X, point.Y, 2, 0);
                return Signal.Normal;
            }

            case "input.mouseDown":
            {
                var point = Point(step, "x", "y");
                _devices.Input.MouseDown(Button(step), point.X, point.Y);
                return Signal.Normal;
            }

            case "input.mouseUp":
            {
                var point = Point(step, "x", "y");
                _devices.Input.MouseUp(Button(step), point.X, point.Y);
                return Signal.Normal;
            }

            case "input.mouseScroll":
            {
                var point = Point(step, "x", "y");
                _devices.Input.Scroll(step.Text("direction"), Math.Max(1, Number(step, "amount")),
                    point.X, point.Y);
                return Signal.Normal;
            }

            case "input.mouseDrag":
                _devices.Input.Drag(Button(step), Number(step, "startX"), Number(step, "startY"),
                    Number(step, "endX"), Number(step, "endY"), Pace(Number(step, "durationMs")),
                    Number(step, "steps"));
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

            case "uia.focusWindow":
                FocusWindow(step, depth);
                return Signal.Normal;

            default:
                throw new StepFailure("Run.Unsupported", step.Type);
        }
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
            if (condition is not null && !await Check(condition, depth, token))
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
        var matched = condition is null || await Check(condition, depth, token);
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

    private async Task<bool> Check(ExecutableStep condition, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        switch (condition.Type)
        {
            case "condition.compare":
                return Compare(condition);
            case "condition.group":
                return await Group(condition, depth, token);
            case "condition.randomChance":
                return System.Random.Shared.NextDouble() * 100 < Number(condition, "percent");
            case "condition.imageExists":
                return Search(condition) is not null;
            case "condition.textExists":
            {
                var wanted = Read(condition.Text("text")).AsText();
                var mode = condition.Text("matchMode");
                return ReadSpans(condition).Any(span => Matches(span.Text, wanted, mode));
            }

            case "condition.uiaExists":
                return _devices.Ui.Exists(Query(condition), 0);

            case "condition.colorEquals":
            {
                var target = PixelColor.Parse(condition.Text("color"));
                var pixel = _devices.Screen.PixelAt(Number(condition, "x"), Number(condition, "y"));
                return pixel.Matches(target, Read(condition.Text("tolerance")).AsNumber());
            }

            default:
                Log(LogLevel.Warn, depth, condition.Type, "Run.UnknownCondition", condition.Type);
                return false;
        }
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

    private async Task<bool> Group(ExecutableStep step, int depth, CancellationToken token)
    {
        var children = step.Children("conditions");
        if (children.Count == 0)
        {
            return true;
        }

        var results = new List<bool>(children.Count);
        foreach (var child in children)
        {
            results.Add(await Check(child, depth, token));
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
    /// Where a mouse action lands. An empty position means "wherever the pointer already is",
    /// which is what a click or a press with no coordinates written down means.
    /// </summary>
    private ScreenPoint Point(ExecutableStep step, string xName, string yName)
    {
        var x = step.Text(xName).Trim();
        var y = step.Text(yName).Trim();
        return x.Length == 0 && y.Length == 0
            ? _devices.Input.Cursor
            : new ScreenPoint(Number(step, xName), Number(step, yName));
    }

    private static string Button(ExecutableStep step)
    {
        var button = step.Text("button").Trim();
        return button.Length == 0 ? "left" : button;
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

        var frame = _devices.Screen.Capture(Number(step, "x"), Number(step, "y"),
            Math.Max(1, Number(step, "width")), Math.Max(1, Number(step, "height")));

        _images[name] = frame;
        Variables.Set(name, Value.FromText($"<image {frame.Width}x{frame.Height}>"));
        Log(LogLevel.Info, depth, step.Type, "Run.Capture", name, $"{frame.Width}x{frame.Height}");
    }

    private void GetPixel(ExecutableStep step, int depth)
    {
        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "color";
        }

        var colour = _devices.Screen.PixelAt(Number(step, "x"), Number(step, "y"));
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
        var x = Number(step, "x");
        var y = Number(step, "y");
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var started = Stopwatch.GetTimestamp();

        while (!_devices.Screen.PixelAt(x, y).Matches(target, tolerance))
        {
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.WaitColorTimeout", target.ToHex());
            }

            await Pause(50, token);
        }

        Log(LogLevel.Info, depth, step.Type, "Run.SawColor", target.ToHex());
    }

    /// <summary>Looks once and writes where the picture was, or an empty value when it was not.</summary>
    private void LookFor(ExecutableStep step, int depth)
    {
        var match = Search(step);
        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "match";
        }

        Variables.Set(name, match is null
            ? Value.FromText(string.Empty)
            : Value.FromText($"{match.Center.X},{match.Center.Y}"));

        Log(LogLevel.Info, depth, step.Type, match is null ? "Run.ImageMissing" : "Run.ImageFound",
            name, match is null ? string.Empty : $"{match.Center.X},{match.Center.Y}");
    }

    private async Task WaitForImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var match = await SearchUntil(step, token)
                    ?? throw new StepFailure("Run.ImageNotFound", step.Text("image"));

        var name = step.Text("resultVariable").Trim();
        if (name.Length == 0)
        {
            name = "match";
        }

        Variables.Set(name, Value.FromText($"{match.Center.X},{match.Center.Y}"));
        Log(LogLevel.Info, depth, step.Type, "Run.ImageFound", name, $"{match.Center.X},{match.Center.Y}");
    }

    private async Task ClickImage(ExecutableStep step, int depth, CancellationToken token)
    {
        var match = await SearchUntil(step, token)
                    ?? throw new StepFailure("Run.ImageNotFound", step.Text("image"));

        var x = match.Center.X + Number(step, "offsetX");
        var y = match.Center.Y + Number(step, "offsetY");
        _devices.Input.Click(Button(step), x, y, 1, 0);
        Log(LogLevel.Info, depth, step.Type, "Run.ClickedImage", x, y);
    }

    /// <summary>One attempt at finding a reference picture, in screen coordinates.</summary>
    private ImageMatch? Search(ExecutableStep step)
    {
        var needle = Reference(step);
        var confidence = Number(step, "confidence");
        if (confidence <= 0)
        {
            confidence = 90;
        }

        var (area, origin) = SearchArea(step);
        var found = _devices.Vision.Find(area, needle, confidence);
        return found is null
            ? null
            : found with { Location = new ScreenPoint(found.Location.X + origin.X, found.Location.Y + origin.Y) };
    }

    private async Task<ImageMatch?> SearchUntil(ExecutableStep step, CancellationToken token)
    {
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var interval = Number(step, "intervalMs");
        if (interval <= 0)
        {
            interval = 200;
        }

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var match = Search(step);
            if (match is not null)
            {
                return match;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                return null;
            }

            await Pause(interval, token);
        }
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

        if (_images.TryGetValue(text, out var captured))
        {
            return captured;
        }

        return _devices.Vision.Load(Read(text).AsText()) ?? throw new StepFailure("Run.MissingImage", text);
    }

    /// <summary>The area to search: the whole screen unless the step names a rectangle.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) SearchArea(ExecutableStep step)
    {
        var text = step.Text("region").Trim();
        if (text.Length == 0)
        {
            var size = _devices.Screen.PrimarySize;
            return (_devices.Screen.Capture(0, 0, size.Width, size.Height), new ScreenPoint(0, 0));
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            throw new StepFailure("Run.BadRegion", text);
        }

        return (_devices.Screen.Capture(x, y, width, height), new ScreenPoint(x, y));
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
        _devices.Input.Hotkey(Keys(step), 50);
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

        _devices.Input.Hotkey(Keys(step), 50);
        Log(LogLevel.Info, depth, step.Type, given ? "Run.PastedText" : "Run.Pasted");
    }

    /// <summary>
    /// Waits for the clipboard's change counter to move past <paramref name="before"/>, which is
    /// how a macro notices a copy without reading the same text over and over.
    /// </summary>
    private async Task AwaitClipboard(ExecutableStep step, int before, CancellationToken token)
    {
        var timeout = OptionalNumber(step, "timeoutMs", 1500);
        var started = Stopwatch.GetTimestamp();

        while (_devices.Clipboard.ChangeCount == before)
        {
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.ClipboardTimeout");
            }

            await Pause(30, token);
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
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            var found = _devices.Processes.Find(name);
            if (found.Count > 0)
            {
                var variable = VariableName(step, "resultVariable", string.Empty);
                if (variable.Length > 0)
                {
                    Variables.Set(variable, Value.FromNumber(found[0]));
                }

                Log(LogLevel.Info, depth, step.Type, "Run.ProgramFound", name, found[0]);
                return;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.ProgramNotFound", name);
            }

            await Pause(100, token);
        }
    }

    /// <summary>Waits for a program to finish and keeps the exit code it returned.</summary>
    private async Task WaitForExit(ExecutableStep step, int depth, CancellationToken token)
    {
        var id = ProcessId(step);
        var timeout = OptionalNumber(step, "timeoutMs", 60000);
        var started = Stopwatch.GetTimestamp();

        while (!_devices.Processes.HasExited(id))
        {
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.ProcessTimeout", id.ToString(CultureInfo.InvariantCulture));
            }

            await Pause(100, token);
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
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            var window = _devices.Windows.Find(title);
            if (window is not null)
            {
                var name = VariableName(step, "resultVariable", string.Empty);
                if (name.Length > 0)
                {
                    Variables.Set(name, Value.FromText(window.Title));
                }

                Log(LogLevel.Info, depth, step.Type, "Run.WindowAppeared", window.Title);
                return;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.WindowTimeout", title);
            }

            await Pause(100, token);
        }
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

    /// <summary>Reads the text in a step's region, with the positions in screen coordinates.</summary>
    private IReadOnlyList<TextSpan> ReadSpans(ExecutableStep step)
    {
        var (area, origin) = SearchArea(step);
        var spans = _devices.Ocr.Recognize(area, step.Text("language").Trim().Length == 0
            ? "auto"
            : step.Text("language").Trim());

        return origin == default
            ? spans
            : [.. spans.Select(span => span with { Location = new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y) })];
    }

    private void Recognize(ExecutableStep step, int depth)
    {
        var area = _devices.Screen.Capture(Number(step, "x"), Number(step, "y"),
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

        Variables.Set(name, span is null
            ? Value.FromText(string.Empty)
            : Value.FromText($"{span.Center.X},{span.Center.Y}"));

        Log(LogLevel.Info, depth, step.Type, span is null ? "Run.TextMissing" : "Run.TextFound",
            name, span is null ? string.Empty : $"{span.Center.X},{span.Center.Y}");
    }

    private async Task ClickText(ExecutableStep step, int depth, CancellationToken token)
    {
        var wanted = Read(step.Text("text")).AsText();
        var mode = step.Text("matchMode");
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            var span = ReadSpans(step).FirstOrDefault(candidate => Matches(candidate.Text, wanted, mode));
            if (span is not null)
            {
                var x = span.Center.X + Number(step, "offsetX");
                var y = span.Center.Y + Number(step, "offsetY");
                _devices.Input.Click(Button(step), x, y, 1, 0);
                Log(LogLevel.Info, depth, step.Type, "Run.ClickedText", wanted, $"{x},{y}");
                return;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.TextNotFound", wanted);
            }

            await Pause(200, token);
        }
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

        var started = Stopwatch.GetTimestamp();
        while (!_devices.Ui.Exists(query, 0))
        {
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.ElementNotFound", step.Text("selector"));
            }

            await Pause(poll, token);
        }

        Log(LogLevel.Info, depth, step.Type, "Run.ElementFound", step.Text("selector"));
    }

    private async Task ClickElement(ExecutableStep step, int depth, CancellationToken token)
    {
        var query = Query(step);
        var timeout = Math.Max(0, Number(step, "timeoutMs"));
        var started = Stopwatch.GetTimestamp();

        while (!_devices.Ui.Exists(query, 0))
        {
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                throw new StepFailure("Run.ElementNotFound", step.Text("selector"));
            }

            await Pause(200, token);
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
    /// Reads a selector such as <c>Button[name='Save']</c> or <c>Edit[automationId='input']</c>,
    /// together with the optional window title in front of it.
    /// </summary>
    private UiQuery Query(ExecutableStep step)
    {
        var text = step.Text("selector").Trim();
        string? name = null;
        string? id = null;
        string? type = null;
        string? cls = null;

        var open = text.IndexOf('[');
        var head = open < 0 ? text : text[..open].Trim();
        if (head.Length > 0)
        {
            type = head;
        }

        var close = text.LastIndexOf(']');
        if (open >= 0 && close > open)
        {
            foreach (var clause in text[(open + 1)..close]
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var split = clause.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                var key = clause[..split].Trim().ToLowerInvariant();
                var value = clause[(split + 1)..].Trim().Trim('\'', '"');
                switch (key)
                {
                    case "name":
                        name = value;
                        break;
                    case "automationid":
                        id = value;
                        break;
                    case "controltype":
                    case "type":
                        type = value;
                        break;
                    case "class":
                    case "classname":
                        cls = value;
                        break;
                }
            }
        }

        var title = Read(step.Text("window")).AsText().Trim();
        return new UiQuery(name, id, type, cls, title.Length == 0 ? null : title);
    }

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

        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
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
