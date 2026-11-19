using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The controller actions: plugging one in, pressing a button, pushing a stick, pulling a trigger
/// and letting everything go. Every action tells the run a controller was used, so a button left
/// down is released when the run ends rather than keeping the character walking.
/// </summary>
internal static class GamepadActions
{
    /// <summary>
    /// A press that comes and goes in the same instant is one a game may never see, so even a step
    /// that asked for no hold keeps the button down for this long.
    /// </summary>
    private const int ShortestPressMs = 50;

    /// <summary>
    /// The controller actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["gamepad.connect"] = (run, step, depth, _) =>
            {
                run.Devices.Gamepad.Connect();
                run.MarkGamepadUsed();
                return Task.FromResult(Signal.Normal);
            },

            ["gamepad.button"] = async (run, step, depth, token) =>
            {
                await PressButton(run, step, token);
                return Signal.Normal;
            },

            ["gamepad.stick"] = (run, step, depth, _) =>
            {
                run.Devices.Gamepad.Stick(run.Args(step).Text("stick"),
                    run.Args(step).Number("x"), run.Args(step).Number("y"));
                run.MarkGamepadUsed();
                return Task.FromResult(Signal.Normal);
            },

            ["gamepad.trigger"] = (run, step, depth, _) =>
            {
                run.Devices.Gamepad.Trigger(run.Args(step).Text("trigger"),
                    run.Args(step).Number("amount"));
                run.MarkGamepadUsed();
                return Task.FromResult(Signal.Normal);
            },

            ["gamepad.release"] = (run, step, depth, _) =>
            {
                run.Devices.Gamepad.ReleaseAll();
                run.MarkGamepadUsed();
                return Task.FromResult(Signal.Normal);
            },
        };

    /// <summary>
    /// Holds a controller button or lets it up, or presses and lets go of it. A tap is what a
    /// button is normally for, so it is the one that happens when the step does not say; the hold
    /// is the pause between the press and the release, and a step that asked for none still gets a
    /// short one, because a report that comes and goes in the same instant is one a game may never
    /// see. Tapping several times is the same question the key actions answer with repeat and
    /// interval, so it is answered the same way here: one pause between taps, none before the first.
    /// </summary>
    private static async Task PressButton(IRunContext run, ExecutableStep step,
        CancellationToken token)
    {
        var device = run.Devices.Gamepad;
        var button = run.Args(step).Text("button");
        run.MarkGamepadUsed();

        switch (run.Args(step).Text("buttonMode").Trim().ToLowerInvariant())
        {
            case "down":
                device.Button(button, true);
                return;
            case "up":
                device.Button(button, false);
                return;
        }

        var hold = Math.Max(run.Pace(run.Args(step).Number("holdMs")), ShortestPressMs);
        var repeats = Math.Max(1, run.Args(step).Number("repeat"));
        var interval = run.Pace(run.Args(step).Number("intervalMs"));

        for (var count = 0; count < repeats; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count > 0)
            {
                await Pacing.Pause(interval, token);
            }

            device.Button(button, true);
            await Pacing.Pause(hold, token);
            device.Button(button, false);
        }
    }
}
