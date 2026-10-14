using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Reading the controller in the person's hands, which is the whole of what a trigger bound to a
/// controller button and a recording of one stand on.
/// </summary>
public class GamepadReadingTests
{
    /// <summary>
    /// A reading is a snapshot, so holding a button is one press and not one per reading: a trigger
    /// bound to it must fire once for the press, however often the controller is looked at while the
    /// button stays down.
    /// </summary>
    [Fact]
    public void A_held_button_is_reported_once_and_then_quiet()
    {
        var watch = new GamepadButtonWatch();

        var pressed = Assert.Single(Changes(watch, GamepadButtons.A));
        Assert.Equal(GamepadButtons.A, pressed.Button);
        Assert.True(pressed.Down, "the button went down");

        // Looked at again while it is still down: nothing new happened.
        Assert.Empty(Changes(watch, GamepadButtons.A));
        Assert.Empty(Changes(watch, GamepadButtons.A));

        // Let go, and the release is reported once as well.
        var released = Assert.Single(Changes(watch, GamepadButtons.None));
        Assert.Equal(GamepadButtons.A, released.Button);
        Assert.False(released.Down, "the button came back up");
        Assert.Empty(Changes(watch, GamepadButtons.None));
    }

    /// <summary>Several buttons at once are each reported, in the order the flags are written.</summary>
    [Fact]
    public void Each_button_of_a_reading_is_reported()
    {
        var watch = new GamepadButtonWatch();

        var changes = Changes(watch, GamepadButtons.A | GamepadButtons.Start | GamepadButtons.LeftTrigger);

        Assert.Equal(3, changes.Count);
        Assert.Equal(GamepadButtons.A, changes[0].Button);
        Assert.Equal(GamepadButtons.LeftTrigger, changes[1].Button);
        Assert.Equal(GamepadButtons.Start, changes[2].Button);
        Assert.All(changes, change => Assert.True(change.Down));
    }

    /// <summary>
    /// Starting to watch in the middle of a press is not a press: what is held is taken up, and only
    /// what changes after that is reported. Without it, switching the triggers on with a button
    /// already down would set off its macro, and so would arming a binding with a finger on the pad.
    /// </summary>
    [Fact]
    public void What_is_held_when_a_watch_starts_is_not_a_press()
    {
        var watch = new GamepadButtonWatch();
        watch.Take(GamepadButtons.A);

        Assert.Empty(Changes(watch, GamepadButtons.A));

        var released = Assert.Single(Changes(watch, GamepadButtons.None));
        Assert.Equal(GamepadButtons.A, released.Button);
        Assert.False(released.Down);

        Assert.Single(Changes(watch, GamepadButtons.A));
    }

    /// <summary>
    /// Nobody touching the controller says nothing, which is what a machine with nothing plugged in
    /// looks like: the trigger never fires and a held macro is let go of.
    /// </summary>
    [Fact]
    public void An_untouched_controller_says_nothing()
    {
        var watch = new GamepadButtonWatch();

        Assert.Empty(Changes(watch, GamepadButtons.None));

        // A controller that was picked up and put down between two readings releases what it held.
        Changes(watch, GamepadButtons.B);

        var released = Assert.Single(Changes(watch, GamepadButtons.None));
        Assert.Equal(GamepadButtons.B, released.Button);
        Assert.False(released.Down);
    }

    /// <summary>
    /// Every button a binding can name, and nothing else: the Guide button has no name because
    /// Windows never reports it, so a binding of it could never fire.
    /// </summary>
    [Fact]
    public void Every_bindable_button_has_one_name_and_the_guide_button_has_none()
    {
        var names = new List<string>();

        for (var bit = 1; bit <= (int)GamepadButtons.Guide; bit <<= 1)
        {
            var button = (GamepadButtons)bit;
            var name = GamepadNames.Binding(button);

            if (button == GamepadButtons.Guide)
            {
                Assert.Equal(string.Empty, name);
                continue;
            }

            Assert.StartsWith("Pad ", name, StringComparison.Ordinal);
            names.Add(name);
        }

        Assert.Equal(16, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("guide", names, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, GamepadNames.Binding(GamepadButtons.None));
    }

    /// <summary>
    /// The reading itself, on whatever machine this runs on: asking must answer rather than throw,
    /// and a machine with no controller plugged in must report none — that is what makes a trigger
    /// bound to a controller button on such a machine fire never rather than fall over.
    /// </summary>
    [Fact]
    public void Asking_theMachine_answers_and_a_machine_with_none_reports_none()
    {
        var reading = new XInputGamepadReading();

        var connected = reading.Connected;
        var held = reading.Held();

        Assert.True(
            (held & ~Known) == GamepadButtons.None,
            $"the reading held {held}, which is not a button");

        if (!connected)
        {
            Assert.Equal(GamepadButtons.None, held);
        }
    }

    /// <summary>Every button a reading may carry.</summary>
    private static GamepadButtons Known { get; } = GamepadButtons.A | GamepadButtons.B
        | GamepadButtons.X | GamepadButtons.Y | GamepadButtons.LeftBumper | GamepadButtons.RightBumper
        | GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger | GamepadButtons.LeftStick
        | GamepadButtons.RightStick | GamepadButtons.Up | GamepadButtons.Down | GamepadButtons.Left
        | GamepadButtons.Right | GamepadButtons.Start | GamepadButtons.Back;

    private static List<(GamepadButtons Button, bool Down)> Changes(GamepadButtonWatch watch,
        GamepadButtons now) => [.. watch.Changed(now)];
}
