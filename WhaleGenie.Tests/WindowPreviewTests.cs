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

            var (_, preview) = Rule(windows, @"^Notepad - notes", compareBy: "regex");

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
                compareBy: "regex");

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

            var (_, preview) = Rule(windows, "calc", matchBy: "process");

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

            var box = window.GetVisualDescendants().OfType<TextBox>()
                .First(one => one.IsEffectivelyVisible && ReferenceEquals(one.DataContext,
                    Parameter(viewModel, "title")));
            var list = window.GetVisualDescendants().OfType<TextBlock>()
                .First(one => one.IsEffectivelyVisible && one.Text == preview.Summary);
            var comparing = window.GetVisualDescendants().OfType<ComboBox>()
                .First(one => ReferenceEquals(one.DataContext, Parameter(viewModel, "compareBy")));

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
            foreach (var key in new[]
                     {
                         "input.keyPress", "control.delay", "window.list",

                         // A step that works at the pointer names a window for its numbers to be
                         // counted from and recognises it by its title alone, which is no three-part
                         // rule to show.
                         "input.mouseClick",
                     })
            {
                var viewModel = Open(key);

                Assert.Null(viewModel.WindowPreview);
                Assert.All(viewModel.Parameters,
                    parameter => Assert.False(parameter.ShowsWindowPreview));
            }
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
            Choose(viewModel, "anchorMatchBy", "process");
            text.Text = "notepad";
            Dispatcher.UIThread.RunJobs();

            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(text.ShowsWindowPreview);
            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("笔记 - 第 3 关", Assert.Single(preview.Rows).Title);
            Assert.Equal(WindowMatch.Process, viewModel.WindowMatchFor());

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
            Choose(viewModel, "anchorMatchBy", "process");
            Parameter(viewModel, "anchorWindow").Text = "notepad";

            Assert.True(viewModel.ApplyCursorPosition(1010, 520));

            Assert.Equal("10", Parameter(viewModel, "x").CurrentText);
            Assert.Equal("20", Parameter(viewModel, "y").CurrentText);
        });
    }

    /// <summary>A step that recognises a window, written the way the fields say and read once.</summary>
    private static (AddActionViewModel ViewModel, WindowPreviewViewModel Preview) Rule(
        WindowsForChecks windows, string text, string matchBy = "title", string compareBy = "contains")
    {
        var viewModel = Open("window.exists");
        viewModel.Windows = windows;
        Choose(viewModel, "matchBy", matchBy);
        Choose(viewModel, "compareBy", compareBy);
        Typing(viewModel, text);

        var preview = viewModel.WindowPreview!;
        preview.RefreshCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return (viewModel, preview);
    }

    private static void Typing(AddActionViewModel viewModel, string text)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == "title").Text = text;

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

        public IReadOnlyList<WindowInfo> List() => [.. Open];

        public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match, WindowCompare compare)
        {
            var wanted = (value ?? string.Empty).Trim();
            if (wanted.Length == 0)
            {
                return Open.Count > 0 ? [Open[0]] : [];
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
    }
}
