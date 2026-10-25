using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
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
    public void Clicking_a_key_hands_that_key_back_and_leaves_the_window_up()
    {
        Ui.Run(() =>
        {
            var window = new VirtualKeyboardWindow();
            var taken = new List<string>();
            window.KeyChosen += taken.Add;

            Click(Controls(window).First(cap => (string?)cap.Tag == "F5"));
            Click(Controls(window).First(cap => (string?)cap.Tag == "左Ctrl"));

            // A combination is built a key at a time, so the window takes the next cap instead of
            // closing and being opened again for it.
            Assert.Equal(["F5", "左Ctrl"], taken);
            Assert.True(window.IsVisible);
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

    /// <summary>Where a cap sits in its window, and how big it is.</summary>
    private static Rect Place(List<Button> caps, string name, Visual window)
    {
        var cap = caps.First(candidate => (string?)candidate.Tag == name);
        var at = cap.TranslatePoint(default, window);
        Assert.NotNull(at);
        return new Rect(at!.Value, cap.Bounds.Size);
    }

    /// <summary>
    /// The keys a keyboard keeps to the side are found by where they sit, so a column of them is
    /// only useful if it is a column: the lock row, the navigation keys and the arrows all start at
    /// the same place, are the same width, and the gaps between them are the same. A key that
    /// drifts by a few pixels is a key the eye looks for where it is not.
    /// </summary>
    [Fact]
    public void The_keys_at_the_side_line_up_with_the_keys_under_them()
    {
        Ui.Run(() =>
        {
            var window = new VirtualKeyboardWindow();
            var caps = Controls(window);

            var columns = new[]
            {
                // The side block, whose lock row sits above the navigation keys in the same three
                // columns, with the arrow of each column under them.
                new[] { "PrintScreen", "Insert", "Delete", "Left" },
                new[] { "ScrollLock", "Home", "End", "Up", "Down" },
                new[] { "Pause", "PageUp", "PageDown", "Right" },

                // The number pad, which is four columns of five keys.
                new[] { "NumLock", "NumPad7", "NumPad4", "NumPad1" },
                new[] { "NumPadDivide", "NumPad8", "NumPad5", "NumPad2" },
                new[] { "NumPadMultiply", "NumPad9", "NumPad6", "NumPad3", "NumPadDecimal" },
                new[] { "NumPadSubtract", "NumPadAdd", "NumPadEnter" },
            };

            foreach (var column in columns)
            {
                var places = column.Select(name => Place(caps, name, window)).ToList();
                Assert.Single(places.Select(place => Math.Round(place.X, 1)).Distinct());
                Assert.Single(places.Select(place => Math.Round(place.Width, 1)).Distinct());
            }

            // And the gap between two keys of a row is the gap between the two under it, so no row
            // is spread out further than the one above it.
            foreach (var gap in new[]
                     {
                         GapAfter(caps, "PrintScreen", "ScrollLock", window),
                         GapAfter(caps, "Insert", "Home", window),
                         GapAfter(caps, "Delete", "End", window),
                         GapAfter(caps, "Home", "PageUp", window),
                         GapAfter(caps, "End", "PageDown", window),
                         GapAfter(caps, "NumPad7", "NumPad8", window),
                         GapAfter(caps, "NumPad5", "NumPad6", window),
                     })
            {
                Assert.InRange(gap, 3.9, 4.1);
            }
        });
    }

    /// <summary>
    /// A cap says the whole name of its key. The program draws its writing in Inter, which the
    /// headless session does not take as its own default, so the width is measured in Inter here:
    /// a key that reads "NumL" is a key that cannot be told from the ones beside it.
    /// </summary>
    [Fact]
    public void Every_cap_says_all_of_its_name()
    {
        Ui.Run(() =>
        {
            var inter = new FontFamily("avares://Avalonia.Fonts.Inter/Assets/Inter.ttf#Inter");

            foreach (var cap in Controls(new VirtualKeyboardWindow()))
            {
                var text = new TextLayout((string?)cap.Content ?? string.Empty,
                    new Typeface(inter), cap.FontSize, Brushes.Black);
                var room = cap.Bounds.Width - cap.BorderThickness.Left
                    - cap.BorderThickness.Right - cap.Padding.Left - cap.Padding.Right;

                Assert.True(text.Width <= room,
                    $"{cap.Content} needs {text.Width:F1} of the {room:F1} it has");
            }
        });
    }

    [Fact]
    public void Escape_closes_without_taking_the_key_it_is_pressed_on()
    {
        Ui.Run(() =>
        {
            var window = new VirtualKeyboardWindow();
            var taken = new List<string>();
            window.KeyChosen += taken.Add;
            Controls(window);

            // The key pressed to get out is not the key being picked: taking Esc is done by
            // clicking the cap that says so.
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(taken);
            Assert.False(window.IsVisible);
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
