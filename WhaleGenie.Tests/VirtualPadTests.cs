using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The two pads that are drawn on screen so a key or a controller control can be pointed at
/// instead of spelled. What is checked here is the part a person cannot see: that every cap on
/// them is a name the engine reads back, because a cap that spells a key wrongly looks pickable
/// and only fails when the macro runs.
/// </summary>
public class VirtualPadTests
{
    /// <summary>The controls of a pad, once it is on screen and its controls are wired up.</summary>
    private static List<Button> Controls(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return
        [
            .. window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Tag is string),
        ];
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void Every_key_on_the_keyboard_is_one_the_engine_knows()
    {
        Ui.Run(() =>
        {
            var caps = Controls(new VirtualKeyboardWindow());
            Assert.NotEmpty(caps);

            // A cap carries the name the step stores, so a name that does not resolve would be a
            // key that is there to be clicked and then cannot be run.
            foreach (var cap in caps)
            {
                Assert.NotNull(KeyNames.Resolve((string?)cap.Tag));
            }

            var names = caps.Select(cap => (string)cap.Tag!).ToList();
            foreach (var wanted in new[]
                     {
                         "A", "5", "F5", "Enter", "Space", "Ctrl", "Shift", "Alt", "Win",
                         "Up", "Left", "PageDown", "NumPad5", "Esc", "Tab", "Backspace", "Delete",
                     })
            {
                Assert.Contains(wanted, names);
            }
        });
    }

    [Fact]
    public void Clicking_a_key_hands_that_key_back()
    {
        Ui.Run(() =>
        {
            var window = new VirtualKeyboardWindow();
            Click(Controls(window).First(cap => (string?)cap.Tag == "F5"));

            Assert.Equal("F5", window.Chosen);
        });
    }

    [Fact]
    public void Escape_backs_out_without_picking_the_key_it_is_pressed_on()
    {
        Ui.Run(() =>
        {
            var window = new VirtualKeyboardWindow();
            Controls(window);

            // The key pressed to get out is not the key being picked: taking Esc is done by
            // clicking the cap that says so.
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

            Assert.Null(window.Chosen);
        });
    }

    [Fact]
    public void Every_control_on_the_pad_is_one_the_engine_knows()
    {
        Ui.Run(() =>
        {
            var controls = Controls(new VirtualGamepadWindow(GamepadPick.Button));
            Assert.NotEmpty(controls);

            foreach (var control in controls)
            {
                // Asked for a button, so every control of the pad is one — a stick and a trigger
                // included, since a step may press either of those the way it presses a button.
                Assert.NotNull(GamepadNames.Button((string?)control.Tag));
                Assert.True(control.IsEnabled);
            }

            var names = controls.Select(control => (string)control.Tag!).ToList();
            foreach (var wanted in new[]
                     {
                         "a", "b", "x", "y", "lb", "rb", "lt", "rt", "ls", "rs",
                         "up", "down", "left", "right", "start", "back", "guide",
                     })
            {
                Assert.Contains(wanted, names);
            }
        });
    }

    [Fact]
    public void Clicking_a_control_hands_that_control_back()
    {
        Ui.Run(() =>
        {
            var window = new VirtualGamepadWindow(GamepadPick.Button);
            Click(Controls(window).First(control => (string?)control.Tag == "y"));

            Assert.Equal("y", window.Chosen);
        });
    }

    [Fact]
    public void A_pad_asked_about_a_stick_answers_with_the_side_it_was_clicked_on()
    {
        Ui.Run(() =>
        {
            var window = new VirtualGamepadWindow(GamepadPick.Stick);
            var controls = Controls(window);

            // Only the two sticks can be clicked: a button has no side, and a step that moves a
            // stick is not asking about one.
            foreach (var control in controls)
            {
                Assert.Equal((string?)control.Tag is "ls" or "rs", control.IsEnabled);
            }

            Click(controls.First(control => (string?)control.Tag == "rs"));

            Assert.Equal("right", window.Chosen);
        });
    }

    [Fact]
    public void A_pad_asked_about_a_trigger_answers_with_the_side_it_was_clicked_on()
    {
        Ui.Run(() =>
        {
            var window = new VirtualGamepadWindow(GamepadPick.Trigger);
            var controls = Controls(window);

            foreach (var control in controls)
            {
                Assert.Equal((string?)control.Tag is "lt" or "rt", control.IsEnabled);
            }

            Click(controls.First(control => (string?)control.Tag == "lt"));

            Assert.Equal("left", window.Chosen);
        });
    }
}
