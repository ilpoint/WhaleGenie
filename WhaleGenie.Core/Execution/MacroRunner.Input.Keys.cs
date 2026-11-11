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

// Keys: reading a chord, sending a run of them, waiting for one, and driving the gamepad.
public sealed partial class MacroRunner
{
    private IReadOnlyList<string> Keys(ExecutableStep step)
        => Chord(ReadText(step, "keys"));

    /// <summary>The shortcut a clipboard step presses, written as one combination.</summary>
    private IReadOnlyList<string> Shortcut(ExecutableStep step)
        => Chord(ReadText(step, "shortcut"));

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
        var rows = step.Rows("keySequence");
        var hold = Number(step, "holdMs");
        var gap = Number(step, "gapMs");
        var after = Pace(Number(step, "afterMs"));
        var runs = Math.Max(1, Number(step, "repeat"));
        var interval = Pace(Number(step, "sequenceIntervalMs"));
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
                var keys = Chord(Word(rows[index], "keys"));
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
            throw new StepFailure(RunKeys.NoKeys, string.Empty);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.KeysSent, presses);
    }

    /// <summary>
    /// Waits for one key to go down or come up. The hook reports keys on its own thread, so the
    /// wait is a task the hook fills in; the step's own timeout cancels it, and the handler is
    /// taken off the hook either way. A key that ends the wait may be kept from the program in
    /// front, which is what "press F8 to carry on" without F8 reaching the page needs.
    /// </summary>
    private async Task WaitForKey(ExecutableStep step, CancellationToken token)
    {
        var written = ReadText(step, "key").Trim();
        var wanted = written.Length == 0
            ? (HookKey?)null
            : KeyNames.Resolve(written) ?? throw new StepFailure(RunKeys.UnknownKey, written);
        var up = string.Equals(ReadText(step, "event").Trim(), "up", StringComparison.OrdinalIgnoreCase);
        var swallow = Flag(step, "swallow", false);

        var heard = new TaskCompletionSource<KeyHookEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            _devices.Hook.KeyUp += Saw;
        }
        else
        {
            _devices.Hook.KeyDown += Saw;
        }

        try
        {
            var seen = await heard.Task.WaitAsync(token);
            Give(step, "key", Value.FromText(KeyNames.Name(seen.Key)));
            Give(step, "injected", Value.FromBool(seen.Injected));
        }
        finally
        {
            if (up)
            {
                _devices.Hook.KeyUp -= Saw;
            }
            else
            {
                _devices.Hook.KeyDown -= Saw;
            }
        }
    }

    /// <summary>
    /// Presses a key, over again when the step asks for more than one. The pause goes between the
    /// presses and never after the last, so a step that presses once waits for nothing.
    /// </summary>
    private async Task PressKey(ExecutableStep step, CancellationToken token)
    {
        var key = ReadText(step, "key");
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
        var button = ReadText(step, "button");
        _gamepadUsed = true;

        switch (ReadText(step, "buttonMode").Trim().ToLowerInvariant())
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
}
