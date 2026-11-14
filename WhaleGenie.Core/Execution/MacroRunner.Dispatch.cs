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

// Sending a step to the family that carries it out, and the control flow that walks blocks.
public sealed partial class MacroRunner
{
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

        if (ActionSemanticsCatalog.Find(step.Type)?.SignatureSource
            is not ActionSignatureSource.CalledMacro)
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
            case "control.delay":
                await Pause(Pace(Number(step, "ms")), token);
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
                throw new StepFailure(FailureKeyOf(step), ReadValue(step, "reason").AsText());
            case "control.note":
                // A note is for whoever reads the macro. There is nothing to do and nothing to say.
                return Signal.Normal;
            case "control.setVariable":
                SetVariable(step, depth);
                return Signal.Normal;
            case "control.clearVariable":
                ClearVariable(step, depth);
                return Signal.Normal;
            case "control.incrementGlobal":
                IncrementGlobal(step, depth);
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
        RequireCondition(step, "condition");
        var poll = Number(step, "intervalMs");
        if (poll <= 0)
        {
            poll = 200;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await WaitForFlagAsync(() => Check(step, "condition", token), poll, token);
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
        Log(LogLevel.Info, depth, step.Type, LogKeys.WaitedFor, waited, step.Text("condition"));
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
            scope.Set(VariableStore.LoopIndexName, Value.FromNumber(round));
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
        RequireCondition(step, "condition");
        var limit = Math.Max(1, Number(step, "maxIterations"));
        var interval = Pace(Number(step, "intervalMs"));
        using var scope = EnterLoop(VariableStore.LoopIndexName);

        for (var round = 0; round < limit; round++)
        {
            token.ThrowIfCancellationRequested();
            scope.Set(VariableStore.LoopIndexName, Value.FromNumber(round));
            if (!Check(step, "condition", token))
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

            await Pause(interval, token);
        }

        Give(step, "ended", Value.FromText("limit"));
        Log(LogLevel.Warn, depth, step.Type, LogKeys.LoopLimit, limit);
        return Signal.Normal;
    }
}
