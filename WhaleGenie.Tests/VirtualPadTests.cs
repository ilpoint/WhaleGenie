using Avalonia;
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

                // And it is one of the names the key field offers, so the list and the keyboard
                // never disagree about what a key is called.
                Assert.Contains((string)cap.Tag!, KeyNames.Names);
            }

            var names = caps.Select(cap => (string)cap.Tag!).ToList();
            foreach (var wanted in new[]
                     {
                         "A", "5", "F5", "F13", "Enter", "Space", "Up", "Left", "PageDown",
                         "NumPad5", "Esc", "Tab", "Backspace", "Delete",
                         "MediaPlay", "VolumeMute", "BrowserBack", "AppCalculator",

                         // A side is part of the name: a trigger is bound to one of the two shift
                         // keys, so the keyboard writes them apart as well.
                         "左Ctrl", "右Ctrl", "左Shift", "右Shift", "左Alt", "右Alt", "左Win", "右Win",
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
    public void The_keyboard_needs_no_scrolling_and_keeps_the_side_keys_by_the_letters()
    {
        Ui.Run(() =>
        {
            var window = new VirtualKeyboardWindow();
            var caps = Controls(window);
            Dispatcher.UIThread.RunJobs();

            // Everything has to be on screen at once: a page the user has to scroll to find a key
            // on is a page that hides keys, which is what the widths are chosen for.
            var view = window.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(view.Extent.Width <= view.Viewport.Width,
                $"content is {view.Extent.Width} wide in a viewport of {view.Viewport.Width}");
            Assert.True(view.Extent.Height <= view.Viewport.Height,
                $"content is {view.Extent.Height} tall in a viewport of {view.Viewport.Height}");

            // Backspace and the navigation block are a hand's width apart, not a window's: the two
            // blocks are laid out with a fixed gap rather than the second one pushed to the edge.
            var gap = GapAfter(caps, "Backspace", "Insert", window);
            Assert.InRange(gap, 1, 60);
        });
    }

    [Fact]
    public void Every_cap_is_big_enough_to_read()
    {
        Ui.Run(() =>
        {
            foreach (var cap in Controls(new VirtualKeyboardWindow()))
            {
                Assert.True(cap.FontSize >= 11, $"{cap.Tag} is written at {cap.FontSize}");
                Assert.True(cap.Height >= 28, $"{cap.Tag} is {cap.Height} tall");
            }
        });
    }

    /// <summary>The empty space between the right edge of one cap and the left edge of another.</summary>
    private static double GapAfter(List<Button> caps, string left, string right, Visual window)
    {
        var first = caps.First(cap => (string?)cap.Tag == left);
        var start = first.TranslatePoint(default, window);
        var end = caps.First(cap => (string?)cap.Tag == right).TranslatePoint(default, window);

        Assert.NotNull(start);
        Assert.NotNull(end);
        return end!.Value.X - (start!.Value.X + first.Bounds.Width);
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
