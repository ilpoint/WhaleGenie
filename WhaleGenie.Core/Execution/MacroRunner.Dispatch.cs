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
    /// One action's work: the step, how deep the run is, and the run's token. It is a delegate
    /// rather than an interface because a handler is a piece of work, not an object with a life of
    /// its own — the run it belongs to is the argument.
    /// </summary>
    private delegate Task<Signal> ActionHandler(ExecutableStep step, int depth,
        CancellationToken token);

    private Dictionary<string, ActionHandler>? _actions;

    /// <summary>The actions the engine answers, by the key the catalogue gives them.</summary>
    internal IReadOnlyCollection<string> HandledActions => Handlers.Keys;

    private Dictionary<string, ActionHandler> Handlers => _actions ??= Collect();

    /// <summary>
    /// Every family's table in one place. A key written in two families is a mistake that would
    /// otherwise be decided by which table was collected last, so collecting it throws.
    /// </summary>
    private Dictionary<string, ActionHandler> Collect()
    {
        var all = new Dictionary<string, ActionHandler>(StringComparer.Ordinal);
        foreach (var family in Families)
        {
            foreach (var (key, handler) in family)
            {
                all.Add(key, handler);
            }
        }

        // A family that has left this class brings its own table; it is handed the run it is
        // answering rather than capturing it, so the table is built once for the process.
        foreach (var family in new[]
                 {
                     ClipboardActions.Map, ProcessActions.Map, SystemActions.Map, UiaActions.Map,
                     FileActions.Map, FileJsonActions.Map, FileCsvActions.Map, DataActions.Map,
                     WindowActions.Map, KeyboardActions.Map, GamepadActions.Map,
                     PointerActions.Map, BrowserActions.Map,
                 })
        {
            foreach (var (key, handler) in family)
            {
                all.Add(key, (step, depth, token) => handler(this, step, depth, token));
            }
        }

        return all;
    }

    private IReadOnlyList<Dictionary<string, ActionHandler>> Families
        =>
        [
            ControlActions,
            HeldActions, VisionActions, OcrActions,
            ScriptActions,
        ];

    /// <summary>
    /// Answers one step by handing it to the family it belongs to. Which family an action is in is
    /// the table it was written in rather than a switch here, so adding an action is a matter of
    /// writing its method and naming it in its family's table, not of touching this.
    /// </summary>
    private Task<Signal> Dispatch(ExecutableStep step, int depth, CancellationToken token)
        => Handlers.TryGetValue(step.Type, out var handler)
            ? handler(step, depth, token)
            : Task.FromException<Signal>(new StepFailure(RunKeys.Unsupported, step.Type));

    /// <summary>
    /// The control actions, each answered by the method written for it. A step of
    /// another family is not in this table.
    /// </summary>
    private Dictionary<string, ActionHandler> ControlActions
        => _controlActions ??= new(StringComparer.Ordinal)
        {
            ["control.sequence"] = async (step, depth, token) =>
            {
                return await RunSteps(step.Children("steps"), depth + 1, token);
            },

            ["control.holdInput"] = async (step, depth, token) =>
            {
                return await RunHold(step, depth, token, input: true);
            },

            ["control.holdBrowser"] = async (step, depth, token) =>
            {
                return await RunHold(step, depth, token, input: false);
            },

            ["control.runMacro"] = async (step, depth, token) =>
            {
                return await RunOtherMacro(step, depth, token);
            },

            ["control.delay"] = async (step, depth, token) =>
            {
                await Pause(Pace(Args(step).Number("ms")), token);
                return Signal.Normal;
            },

            ["control.waitUntil"] = async (step, depth, token) =>
            {
                return await RunWaitUntil(step, depth, token);
            },

            ["control.repeat"] = async (step, depth, token) =>
            {
                return await RunRepeat(step, depth, token);
            },

            ["control.while"] = async (step, depth, token) =>
            {
                return await RunWhile(step, depth, token);
            },

            ["control.forEach"] = async (step, depth, token) =>
            {
                return await RunForEach(step, depth, token);
            },

            ["control.for"] = async (step, depth, token) =>
            {
                return await RunFor(step, depth, token);
            },

            ["control.switch"] = async (step, depth, token) =>
            {
                return await RunSwitch(step, depth, token);
            },

            ["control.if"] = async (step, depth, token) =>
            {
                return await RunIf(step, depth, token);
            },

            ["control.try"] = async (step, depth, token) =>
            {
                return await RunTry(step, depth, token);
            },

            ["control.break"] = (step, depth, token) =>
            {
                return Task.FromResult(LeaveLoop(Signal.Break, step));
            },

            ["control.continue"] = (step, depth, token) =>
            {
                return Task.FromResult(LeaveLoop(Signal.Continue, step));
            },

            ["control.return"] = (step, depth, token) =>
            {
                return Task.FromResult(Signal.Return);
            },

            ["control.rethrow"] = (step, depth, token) =>
            {
                throw CaughtFailure();
            },

            ["control.stop"] = (step, depth, token) =>
            {
                Log(LogLevel.Warn, depth, step.Type, LogKeys.StopRequested,
                    Args(step).ValueOf("reason").AsText());
                return Task.FromResult(Signal.Stop);
            },

            ["control.fail"] = (step, depth, token) =>
            {
                // The macro says this step did not work. It travels the same way a device failure
                // does, so whatever the step's failure rule says — retry, carry on, ask — holds for
                // a step the macro itself called failed.
                throw new StepFailure(FailureKeyOf(step), Args(step).ValueOf("reason").AsText());
            },

            ["control.note"] = (step, depth, token) =>
            {
                // A note is for whoever reads the macro. There is nothing to do and nothing to say.
                return Task.FromResult(Signal.Normal);
            },

            ["control.setVariable"] = (step, depth, token) =>
            {
                SetVariable(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.clearVariable"] = (step, depth, token) =>
            {
                ClearVariable(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.incrementGlobal"] = (step, depth, token) =>
            {
                IncrementGlobal(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.listAdd"] = (step, depth, token) =>
            {
                ChangeList(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.listInsert"] = (step, depth, token) =>
            {
                ChangeList(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.listSet"] = (step, depth, token) =>
            {
                ChangeList(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.listRemoveAt"] = (step, depth, token) =>
            {
                ChangeList(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.listClear"] = (step, depth, token) =>
            {
                ChangeList(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["control.log"] = (step, depth, token) =>
            {
                Log(Level(Args(step).Text("level")), depth, step.Type, LogKeys.Message, Args(step).ValueOf("message").AsText());
                return Task.FromResult(Signal.Normal);
            },

            ["control.ask"] = async (step, depth, token) =>
            {
                return await AskPerson(step, depth, token);
            },

            ["control.input"] = async (step, depth, token) =>
            {
                return await AskForText(step, depth, token);
            },
        };

    private Dictionary<string, ActionHandler>? _controlActions;


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
        var poll = Args(step).Number("intervalMs");
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
        var times = Math.Max(0, Args(step).Number("times"));
        var interval = Pace(Args(step).Number("intervalMs"));
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
        var limit = Math.Max(1, Args(step).Number("maxIterations"));
        var interval = Pace(Args(step).Number("intervalMs"));
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
