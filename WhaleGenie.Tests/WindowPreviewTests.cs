using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The list of windows a step recognises with the rule it is written with right now. It is there to
/// be trusted instead of running the macro, so what it says has to be what a run would do: the same
/// three parts, the same three ways of comparing, and the same answer for a text left empty.
/// </summary>
public class WindowPreviewTests
{
    [Fact]
    public void The_windows_a_rule_recognises_are_listed()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame")
                .Add(3, "Paint", "mspaint", "MSPaintClass");

            var (_, preview) = Rule(windows, "notes");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            var row = Assert.Single(preview.Rows);
            Assert.Equal("Notepad - notes.txt", row.Title);

            // What is compared is the title, so the line does not say the title twice.
            Assert.False(row.HasPart);
            Assert.Empty(preview.Note);
            Assert.Empty(preview.More);
        });
    }

    [Fact]
    public void A_rule_that_finds_nothing_says_so()
    {
        Ui.Run(() =>
        {
            var (_, preview) = Rule(Desk().Add(1, "Notepad", "notepad", "Notepad"), "nothing like it");

            Assert.Equal(Strings.Get("Add.WindowPreview.None"), preview.Summary);
            Assert.Empty(preview.Rows);
        });
    }

    [Fact]
    public void A_window_is_recognised_through_a_pattern_the_way_a_run_would()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Notepad - other.txt", "notepad", "Notepad");

            var (_, preview) = Rule(windows, @"^Notepad - notes", how: "titleRegex");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);
        });
    }

    [Fact]
    public void A_pattern_that_will_not_read_is_said_in_the_words_a_run_uses()
    {
        Ui.Run(() =>
        {
            var (_, preview) = Rule(Desk().Add(1, "Notepad", "notepad", "Notepad"), "[",
                how: "titleRegex");

            // The same words the run reports it with, because it is the same mistake.
            Assert.Equal(Strings.Format("Run.BadPattern", "["), preview.Summary);
            Assert.Empty(preview.Rows);
        });
    }

    [Fact]
    public void Comparing_a_part_other_than_the_title_shows_that_part()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var (_, preview) = Rule(windows, "calc", how: "process");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            var row = Assert.Single(preview.Rows);
            Assert.Equal("Calculator", row.Title);
            Assert.Equal(Strings.Get("WindowLookup.Process"), row.PartLabel);
            Assert.Equal("calc", row.Part);
        });
    }

    [Fact]
    public void A_text_left_empty_is_the_window_in_front()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var (_, preview) = Rule(windows, "");

            // Nothing said means the frontmost window, the same answer the run gives.
            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);
            Assert.Equal(Strings.Get("Add.WindowPreview.Front"), preview.Note);
        });
    }

    [Fact]
    public void A_text_written_as_a_variable_is_said_to_be_read_as_it_stands()
    {
        Ui.Run(() =>
        {
            var (_, preview) = Rule(Desk().Add(1, "Notepad", "notepad", "Notepad"), "$wanted");

            Assert.Equal(Strings.Get("Add.WindowPreview.Variable"), preview.Note);
        });
    }

    [Fact]
    public void Only_the_first_few_are_listed_and_the_rest_counted()
    {
        Ui.Run(() =>
        {
            var windows = Desk();
            for (var handle = 1; handle <= 7; handle++)
            {
                windows.Add(handle, $"Notepad - document {handle}.txt", "notepad", "Notepad");
            }

            var (_, preview) = Rule(windows, "Notepad");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 7), preview.Summary);
            Assert.Equal(5, preview.Rows.Count);
            Assert.Equal(Strings.Format("Add.WindowPreview.More", 2), preview.More);
        });
    }

    [Fact]
    public void Refresh_reads_the_windows_again()
    {
        Ui.Run(() =>
        {
            var windows = Desk().Add(1, "Notepad - notes.txt", "notepad", "Notepad");
            var (_, preview) = Rule(windows, "Notepad");
            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);

            // A window opened since the last look is what the button beside the list is for.
            windows.Add(2, "Notepad - second.txt", "notepad", "Notepad");
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 2), preview.Summary);
            Assert.Equal(2, preview.Rows.Count);
        });
    }

    /// <summary>
    /// Every window the list shows is framed where it sits. The list says which windows the rule
    /// recognises by their titles, and the frames say where they are, which is the half a list
    /// cannot say: two windows of the same name read the same in a list and are told apart on the
    /// screen.
    /// </summary>
    [Fact]
    public void Every_window_the_list_shows_is_framed_where_it_sits()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Notepad - other.txt", "notepad", "Notepad");
            windows.Place(2, 300, 200, 400, 300);

            var (_, preview) = Rule(windows, "Notepad");

            Assert.Equal(2, preview.Rows.Count);
            var places = preview.Places.OrderBy(place => place.Handle).ToList();
            Assert.Equal(new FramePlace(1, new ScreenPoint(0, 0), new ScreenSize(100, 100)), places[0]);
            Assert.Equal(new FramePlace(2, new ScreenPoint(300, 200), new ScreenSize(400, 300)),
                places[1]);
        });
    }

    /// <summary>
    /// The frames follow the windows while the list is up. A window gets dragged somewhere else while
    /// a rule is being written more often than not, and a frame left where the window used to be
    /// points at a place that says nothing about this step any more.
    /// </summary>
    [Fact]
    public void The_frames_follow_a_window_that_is_moved_while_the_list_is_up()
    {
        var followed = Ui.RunAsync(async () =>
        {
            var interval = WindowPreviewViewModel.FollowInterval;
            WindowPreviewViewModel.FollowInterval = TimeSpan.FromMilliseconds(30);
            try
            {
                var windows = Desk().Add(1, "Notepad - notes.txt", "notepad", "Notepad");
                var (_, preview) = Rule(windows, "Notepad");
                Assert.Equal(new FramePlace(1, new ScreenPoint(0, 0), new ScreenSize(100, 100)),
                    Assert.Single(preview.Places));

                windows.Place(1, 300, 200, 400, 300);
                await Task.Delay(200);

                return preview.Places;
            }
            finally
            {
                WindowPreviewViewModel.FollowInterval = interval;
            }
        });

        Assert.Equal(new FramePlace(1, new ScreenPoint(300, 200), new ScreenSize(400, 300)),
            Assert.Single(followed));
    }

    /// <summary>
    /// A shrunk window is not lying anywhere on the screen, so there is nowhere to draw its frame:
    /// a frame over nothing would say the window is somewhere it is not.
    /// </summary>
    [Fact]
    public void A_window_that_is_not_on_the_screen_is_listed_but_not_framed()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Notepad - other.txt", "notepad", "Notepad");
            windows.Shrink(1);

            var (_, preview) = Rule(windows, "Notepad");

            Assert.Equal(2, preview.Rows.Count);
            Assert.Equal(2, Assert.Single(preview.Places).Handle);
        });
    }

    /// <summary>
    /// A step that does not read the window it names has nothing to frame, and the frames come back
    /// with the list when the step does read it again: the same rule the list itself follows.
    /// </summary>
    [Fact]
    public void A_rule_the_step_does_not_read_is_not_framed()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.Windows = Desk().Add(1, "笔记 - 第 3 关", "notepad", "Notepad");

            var text = Parameter(viewModel, "targetWindow");
            Choose(viewModel, "inputMode", "background");
            Choose(viewModel, "targetHow", "process");
            text.Text = "notepad";
            Dispatcher.UIThread.RunJobs();

            var preview = viewModel.WindowPreview!;
            Assert.Single(preview.Places);

            Choose(viewModel, "inputMode", "foreground");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(preview.Places);

            Choose(viewModel, "inputMode", "background");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(preview.Places);
        });
    }

    /// <summary>
    /// The frames the dialog itself puts up, which is the whole of what the list is for: the windows
    /// it lists are shown on the screen, and they come down with the dialog that put them there.
    /// </summary>
    [Fact]
    public void The_dialog_frames_the_windows_the_list_shows()
    {
        Ui.Run(() =>
        {
            var window = Shell("window.exists");
            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.Windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Notepad - other.txt", "notepad", "Notepad");

            Parameter(viewModel, "window").Text = "Notepad";
            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, window.FramedWindows.Count);
            Assert.Contains(1L, window.FramedWindows);
            Assert.Contains(2L, window.FramedWindows);

            // A dialog that is gone has nothing left to explain the frames.
            window.Close();
            Assert.Empty(window.FramedWindows);
        });
    }

    [Fact]
    public void A_rule_changed_while_typing_is_read_once_the_typing_pauses()
    {
        Ui.RunAsync(async () =>
        {
            var interval = WindowPreviewViewModel.Interval;
            WindowPreviewViewModel.Interval = TimeSpan.FromMilliseconds(30);
            try
            {
                var windows = Desk()
                    .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                    .Add(2, "Calculator", "calc", "CalcFrame");

                var (viewModel, preview) = Rule(windows, "notes");
                Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);

                Typing(viewModel, "calc");

                // Reading the desktop once per keystroke is what waiting for the pause avoids.
                Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);

                await Task.Delay(200);
                Assert.Equal("Calculator", Assert.Single(preview.Rows).Title);
            }
            finally
            {
                WindowPreviewViewModel.Interval = interval;
            }

            return true;
        });
    }

    [Fact]
    public void The_list_stands_under_the_field_the_rule_is_about()
    {
        Ui.Run(() =>
        {
            var window = Shell("window.exists");
            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.Windows = Desk().Add(1, "Notepad - notes.txt", "notepad", "Notepad");
            Typing(viewModel, "Notepad");

            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            double Top(Control control) => control.TranslatePoint(default, window)!.Value.Y;

            // The window's text is written in the box the variables are offered in, like any other
            // text a macro works out earlier: what matters is that the list stands under that box.
            var box = window.GetVisualDescendants().OfType<Control>()
                .First(one => one.IsEffectivelyVisible
                    && one is TextBox or AutoCompleteBox
                    && ReferenceEquals(one.DataContext, Parameter(viewModel, "window")));
            var list = window.GetVisualDescendants().OfType<TextBlock>()
                .First(one => one.IsEffectivelyVisible && one.Text == preview.Summary);
            var comparing = window.GetVisualDescendants().OfType<ComboBox>()
                .First(one => ReferenceEquals(one.DataContext, Parameter(viewModel, "how")));

            // Under the box it is about, rather than between a field and the editor of that field.
            Assert.True(Top(list) > Top(box), "the list is not under the match text");
            Assert.True(Top(list) < Top(comparing), "the list is not above the comparing field");
        });
    }

    [Fact]
    public void An_action_that_recognises_no_window_of_its_own_has_no_list()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "control.delay", "file.readText" })
            {
                var viewModel = Open(key);

                Assert.Null(viewModel.WindowPreview);
                Assert.All(viewModel.Parameters,
                    parameter => Assert.False(parameter.ShowsWindowPreview));
            }
        });
    }

    /// <summary>
    /// A listing recognises windows with the same three fields as every other window action, so it
    /// gets the same list under the same field — and the list has to mean what the run means by an
    /// empty match text, which for a listing is every window rather than the one in front.
    /// </summary>
    [Fact]
    public void A_listing_shows_the_windows_it_would_hand_over()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("window.list");
            viewModel.Windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var text = Parameter(viewModel, "window");
            Assert.True(text.ShowsWindowPreview);

            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Nothing written: every window is listed, and the line under the box says so.
            Assert.Equal(2, preview.Rows.Count);
            Assert.Equal(Strings.Get("Add.WindowPreview.Every"), preview.Note);

            // Narrowed down by program name: the same three fields the other window actions use.
            Choose(viewModel, "how", "process");
            text.Text = "calc";
            Dispatcher.UIThread.RunJobs();
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["Calculator"], preview.Rows.Select(row => row.Title));
            Assert.Equal("calc", preview.Rows[0].Part);
        });
    }

    /// <summary>
    /// A step that sends its input to a window names that window with the same three parts the
    /// window actions use, so it gets the same list — and only while that is where its input goes:
    /// the other input modes send to whatever has the focus, so the window named below is not read
    /// and a list of windows standing under it says the step does something it does not.
    /// </summary>
    [Fact]
    public void A_step_that_posts_its_input_at_a_window_shows_the_windows_it_would_post_to()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.Windows = Desk()
                .Add(1, "笔记 - 第 3 关", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var text = Parameter(viewModel, "targetWindow");

            // Sending in front: the window is not read, so nothing hangs under its name.
            Assert.False(text.ShowsWindowPreview);

            Choose(viewModel, "inputMode", "background");
            Choose(viewModel, "targetHow", "process");
            text.Text = "notepad";
            Dispatcher.UIThread.RunJobs();

            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(text.ShowsWindowPreview);
            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("笔记 - 第 3 关", Assert.Single(preview.Rows).Title);
            Assert.Equal(WindowMatch.Process, viewModel.WindowMatchFor(text));

            // Back to sending in front, and the list goes with the rule.
            Choose(viewModel, "inputMode", "foreground");
            Dispatcher.UIThread.RunJobs();

            Assert.False(text.ShowsWindowPreview);
        });
    }

    /// <summary>
    /// A step that reads the screen recognises its window with the same three parts the window
    /// actions use, so it gets the same list — and only while its numbers are counted from that
    /// window: a step counting from the screen does not read the window it names at all, and a list
    /// of windows standing under a rule nothing reads says the step does something it does not.
    /// </summary>
    [Fact]
    public void A_step_that_reads_the_screen_shows_the_windows_it_would_read_from()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.findText");
            viewModel.Windows = Desk()
                .Add(1, "笔记 - 第 3 关", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var text = Parameter(viewModel, "anchorWindow");

            // Counting from the screen: the window is not read, so nothing hangs under its name.
            Assert.False(text.ShowsWindowPreview);

            Choose(viewModel, "anchorMode", "window");
            Choose(viewModel, "anchorHow", "process");
            text.Text = "notepad";
            Dispatcher.UIThread.RunJobs();

            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(text.ShowsWindowPreview);
            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("笔记 - 第 3 关", Assert.Single(preview.Rows).Title);
            Assert.Equal(WindowMatch.Process, viewModel.WindowMatchFor(text));

            // Back to counting from the screen, and the list goes with the rule.
            Choose(viewModel, "anchorMode", "screen");
            Dispatcher.UIThread.RunJobs();

            Assert.False(text.ShowsWindowPreview);
        });
    }

    /// <summary>
    /// A position taken off the screen is stored the way the engine will read it back: counted from
    /// the corner of the window the rule recognises, whether that window was named by its title, its
    /// program or its class.
    /// </summary>
    [Fact]
    public void A_picked_position_follows_the_window_named_by_the_program()
    {
        Ui.Run(() =>
        {
            var windows = Desk().Add(1, "笔记 - 第 3 关", "notepad", "Notepad");
            windows.Open[0] = windows.Open[0] with { Location = new ScreenPoint(1000, 500) };

            var viewModel = Open("vision.getPixel");
            viewModel.Windows = windows;
            Choose(viewModel, "anchorMode", "window");
            Choose(viewModel, "anchorHow", "process");
            Parameter(viewModel, "anchorWindow").Text = "notepad";

            Assert.True(viewModel.ApplyCursorPosition(1010, 520));

            Assert.Equal("10", Parameter(viewModel, "x").CurrentText);
            Assert.Equal("20", Parameter(viewModel, "y").CurrentText);
        });
    }

    /// <summary>A step that recognises a window, written the way the fields say and read once.</summary>
    private static (AddActionViewModel ViewModel, WindowPreviewViewModel Preview) Rule(
        WindowsForChecks windows, string text, string how = "titleContains")
    {
        var viewModel = Open("window.exists");
        viewModel.Windows = windows;
        Choose(viewModel, "how", how);
        Typing(viewModel, text);

        var preview = viewModel.WindowPreview!;
        preview.RefreshCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return (viewModel, preview);
    }

    private static void Typing(AddActionViewModel viewModel, string text)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == "window").Text = text;

    private static void Choose(AddActionViewModel viewModel, string name, string value)
    {
        var choice = viewModel.Parameters.First(parameter => parameter.Definition.Name == name);
        choice.Option = choice.Choices.First(option => option.Value == value);
    }

    private static WindowsForChecks Desk() => new();

    private static AddActionViewModel Open(string key)
        => (AddActionViewModel)Shell(key).DataContext!;

    private static StepParameterViewModel Parameter(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name);

    /// <summary>The dialog, opened on an action the way the editor opens it.</summary>
    private static AddActionWindow Shell(string key)
    {
        var window = new AddActionWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("wanted"), []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        ((AddActionViewModel)window.DataContext!).SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// A desktop with whatever windows a check asks for, compared the way the real device compares
    /// them: the list has to answer the same question, or the check would only be checking itself.
    /// </summary>
    private sealed class WindowsForChecks : IWindowDevice
    {
        private readonly Dictionary<long, (string Process, string Class)> _facts = [];

        /// <summary>The windows on this desktop, frontmost first.</summary>
        public List<WindowInfo> Open { get; } = [];

        public WindowsForChecks Add(long handle, string title, string process, string className)
        {
            Open.Add(new WindowInfo(handle, title, new ScreenPoint(0, 0), new ScreenSize(100, 100),
                false, false));
            _facts[handle] = (process, className);
            return this;
        }

        /// <summary>Moves one of the windows somewhere, which is what a frame is drawn around.</summary>
        public WindowsForChecks Place(long handle, int x, int y, int width, int height)
        {
            var at = Open.FindIndex(window => window.Handle == handle);
            Open[at] = Open[at] with
            {
                Location = new ScreenPoint(x, y),
                Size = new ScreenSize(width, height),
            };
            return this;
        }

        /// <summary>Shrinks one of the windows, which takes it off the screen.</summary>
        public WindowsForChecks Shrink(long handle)
        {
            var at = Open.FindIndex(window => window.Handle == handle);
            Open[at] = Open[at] with { Minimized = true };
            return this;
        }

        public IReadOnlyList<WindowInfo> List() => [.. Open];

        public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match, WindowCompare compare,
            bool everyWhenEmpty = false)
        {
            var wanted = (value ?? string.Empty).Trim();
            if (wanted.Length == 0)
            {
                return everyWhenEmpty || Open.Count == 0 ? [.. Open] : [Open[0]];
            }

            return [.. Open.Where(window => Answers(window, wanted, match, compare))];
        }

        public WindowInfo? Find(string value, WindowMatch match, WindowCompare compare)
            => Filter(value, match, compare).FirstOrDefault();

        public string ProcessOf(long handle)
            => _facts.TryGetValue(handle, out var facts) ? facts.Process : string.Empty;

        public string ClassOf(long handle)
            => _facts.TryGetValue(handle, out var facts) ? facts.Class : string.Empty;

        private bool Answers(WindowInfo window, string wanted, WindowMatch match, WindowCompare compare)
        {
            var text = match switch
            {
                WindowMatch.Process => ProcessOf(window.Handle),
                WindowMatch.ClassName => ClassOf(window.Handle),
                _ => window.Title,
            };

            return compare switch
            {
                WindowCompare.StartsWith => text.StartsWith(wanted, StringComparison.OrdinalIgnoreCase),
                WindowCompare.Regex => Regex.IsMatch(text, wanted, RegexOptions.IgnoreCase),
                _ => text.Contains(wanted, StringComparison.OrdinalIgnoreCase),
            };
        }

        public bool Activate(long handle) => true;

        public bool Minimize(long handle) => true;

        public bool Maximize(long handle) => true;

        public bool Restore(long handle) => true;

        public bool Close(long handle) => true;

        public bool Move(long handle, int x, int y, int width, int height) => true;

        public ScreenPoint ClientOrigin(long handle) => new(0, 0);

        public ScreenSize ClientSize(long handle) => new(0, 0);
    }
}
