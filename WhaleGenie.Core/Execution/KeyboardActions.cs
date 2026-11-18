using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The keyboard actions: one key, one combination, a run of combinations, waiting for a key, and
/// typing text. A key that is pressed and not let go belongs to the run rather than here — it has to
/// be released when the run ends — so what is here is every press that finishes on its own.
/// </summary>
internal static class KeyboardActions
{
    /// <summary>
    /// The keyboard actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["input.keyPress"] = async (run, step, depth, token) =>
            {
                await PressKey(run, step, token);
                return Signal.Normal;
            },

            ["input.waitKey"] = async (run, step, depth, token) =>
            {
                await WaitForKey(run, step, token);
                return Signal.Normal;
            },

            ["input.hotkey"] = async (run, step, depth, token) =>
            {
                await SendHotkey(run, step, token);
                return Signal.Normal;
            },

            ["input.typeText"] = (run, step, depth, _) =>
            {
                TypeText(run, step);
                return Task.FromResult(Signal.Normal);
            },

            ["input.keySequence"] = async (run, step, depth, token) =>
            {
                await SendKeySequence(run, step, depth, token);
                return Signal.Normal;
            },
        };

    /// <summary>
    /// Presses a key, over again when the step asks for more than one. The pause goes between the
    /// presses and never after the last, so a step that presses once waits for nothing.
    /// </summary>
    private static async Task PressKey(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var key = run.Args(step).Text("key");
        var hold = run.Pace(run.Args(step).Number("holdMs"));
        var repeats = Math.Max(1, run.Args(step).Number("repeat"));
        var interval = run.Pace(run.Args(step).Number("intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pacing.Pause(interval, token);
            }

            run.Input(step).KeyPress(key, hold);
        }
    }

    /// <summary>
    /// Waits for one key to go down or come up. The hook reports keys on its own thread, so the
    /// wait is a task the hook fills in; the step's own timeout cancels it, and the handler is
    /// taken off the hook either way. A key that ends the wait may be kept from the program in
    /// front, which is what "press F8 to carry on" without F8 reaching the page needs.
    /// </summary>
    private static async Task WaitForKey(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var written = run.Args(step).Text("key").Trim();
        var wanted = written.Length == 0
            ? (HookKey?)null
            : KeyNames.Resolve(written) ?? throw new StepFailure(RunKeys.UnknownKey, written);
        var up = string.Equals(run.Args(step).Text("event").Trim(), "up",
            StringComparison.OrdinalIgnoreCase);
        var swallow = run.Flag(step, "swallow", false);

        var heard = new TaskCompletionSource<KeyHookEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Saw(KeyHookEvent seen)
        {
            if (wanted is { } key && seen.Key != key)
            {
                return;
            }

            if (swallow)
            {
                seen.Suppress = true;
            }

            heard.TrySetResult(seen);
        }

        if (up)
        {
            run.Devices.Hook.KeyUp += Saw;
        }
        else
        {
            run.Devices.Hook.KeyDown += Saw;
        }

        try
        {
            var seen = await heard.Task.WaitAsync(token);
            run.Give(step, "key", Value.FromText(KeyNames.Name(seen.Key)));
            run.Give(step, "injected", Value.FromBool(seen.Injected));
        }
        finally
        {
            if (up)
            {
                run.Devices.Hook.KeyUp -= Saw;
            }
            else
            {
                run.Devices.Hook.KeyDown -= Saw;
            }
        }
    }

    /// <summary>The same for a combination: it can be sent more than once with a pause between.</summary>
    private static async Task SendHotkey(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var keys = run.Keys(step);
        var hold = run.Pace(run.Args(step).Number("holdMs"));
        var repeats = Math.Max(1, run.Args(step).Number("repeat"));
        var interval = run.Pace(run.Args(step).Number("intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pacing.Pause(interval, token);
            }

            run.Input(step).Hotkey(keys, hold);
        }
    }

    private static void TypeText(IRunContext run, ExecutableStep step)
        => run.Input(step).TypeText(run.Args(step).ValueOf("typedText").AsText(),
            run.Pace(run.Args(step).Number("intervalMs")));

    /// <summary>
    /// Sends a run of combinations in order, one row after another, the way a macro plays out a
    /// combo. Each row is held together for its own moment and then the run waits out the beat
    /// before the next one — the gaps are what make a run read as separate presses rather than as
    /// one long hold, and they are the part of a combo a game notices. A row may carry its own hold
    /// and its own gap, because a combination with one beat of its own is the rule rather than the
    /// exception; the whole run may be played again with a pause between.
    /// </summary>
    private static async Task SendKeySequence(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var rows = step.Rows("keySequence");
        var hold = run.Args(step).Number("holdMs");
        var gap = run.Args(step).Number("gapMs");
        var after = run.Pace(run.Args(step).Number("afterMs"));
        var runs = Math.Max(1, run.Args(step).Number("repeat"));
        var interval = run.Pace(run.Args(step).Number("sequenceIntervalMs"));
        var presses = 0;

        for (var runNumber = 0; runNumber < runs; runNumber++)
        {
            token.ThrowIfCancellationRequested();
            if (runNumber > 0)
            {
                await Pacing.Pause(interval, token);
            }

            for (var index = 0; index < rows.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var keys = StepArguments.Chords(run.Word(rows[index], "keys"));
                if (keys.Count == 0)
                {
                    // A row that was added and never filled in is left out rather than sent as a
                    // press of nothing.
                    continue;
                }

                run.Input(step).Hotkey(keys, run.Pace(run.Number(rows[index], "holdMs", hold)));
                presses++;

                // The beat is between the presses; there is nothing to wait for after the last one.
                if (index < rows.Count - 1)
                {
                    await Pacing.Pause(run.Pace(run.Number(rows[index], "gapMs", gap)), token);
                }
            }

            if (after > 0)
            {
                await Pacing.Pause(after, token);
            }
        }

        if (presses == 0)
        {
            throw new StepFailure(RunKeys.NoKeys, string.Empty);
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.KeysSent, presses);
    }
}
