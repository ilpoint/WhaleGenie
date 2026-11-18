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

// What each kind of step lists: input, screen reads, picked places, conditions and triggers.
public partial class WindowPreviewTests
{
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
            PutWindowRule(viewModel, "calc", "process");
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

            var text = Parameter(viewModel, "window");

            // Sending in front: the window is not read, so nothing hangs under its name.
            Assert.False(text.ShowsWindowPreview);

            Choose(viewModel, "inputMode", "background");
            PutWindowRule(viewModel, "notepad", "process");
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

            var text = Parameter(viewModel, "window");

            // Counting from the screen: the window is not read, so nothing hangs under its name.
            Assert.False(text.ShowsWindowPreview);

            Choose(viewModel, "anchorMode", "window");
            PutWindowRule(viewModel, "notepad", "process");
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
            PutWindowRule(viewModel, "notepad", "process");

            Assert.True(viewModel.ApplyCursorPosition(1010, 520));

            Assert.Equal("10", Parameter(viewModel, "x").CurrentText);
            Assert.Equal("20", Parameter(viewModel, "y").CurrentText);
        });
    }

    /// <summary>
    /// A condition asks about a window with the same two fields a window action acts on, so it gets
    /// the same list under them: "is this window open" is written the way every other window rule is
    /// written, and read the way a run would read it.
    /// </summary>
    [Fact]
    public void A_condition_that_asks_about_a_window_shows_the_windows_it_would_ask_about()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("window.exists");
            viewModel.Windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var text = Parameter(viewModel, "window");
            Assert.True(text.ShowsWindowPreview);

            Assert.True(viewModel.ShowsRun);

            PutWindowRule(viewModel, "calc", "process");
            Dispatcher.UIThread.RunJobs();

            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("Calculator", Assert.Single(preview.Rows).Title);
        });
    }

    /// <summary>
    /// The editor's window trigger lists the windows its rule picks out right now and frames them
    /// where they sit. A rule written a little too wide fires on a window nobody meant, and both
    /// halves of the answer — which windows, and where they are — are worth having before the macro
    /// runs rather than after.
    /// </summary>
    [Fact]
    public void A_window_trigger_lists_and_frames_the_windows_it_would_fire_for()
    {
        Ui.Run(() =>
        {
            var devices = new ChecksDevices
            {
                Windows = Desk()
                    .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                    .Add(2, "Calculator", "calc", "CalcFrame"),
            };

            var macro = new MacroItem { Name = "probe" };
            var entries = new List<TriggerEntry>
            {
                new() { MacroName = macro.Name, Spec = new WindowSpec(WindowRule.Empty) },
            };
            var window = new MacroEditorWindow(macro, [macro], null, devices, entries: entries);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.True(viewModel.IsWindowTrigger);

            viewModel.UseWindowTarget(new WindowRule(Process: "calc", How: WindowHow.Process));
            Dispatcher.UIThread.RunJobs();

            // A rule that changes while it is being written waits for the typing to pause, so the
            // button beside the list is what reads it again at once.
            viewModel.WindowPreview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), viewModel.WindowPreview.Summary);
            Assert.Equal("Calculator", Assert.Single(viewModel.WindowPreview.Rows).Title);
            Assert.Equal([2L], window.FramedWindows);

            // A trigger that is not a window trigger has no rule to read, and the frames go with the
            // list rather than standing over a rule nothing reads.
            viewModel.TriggerMode = TriggerKind.Timer;
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(viewModel.WindowPreview.Places);
            Assert.Empty(window.FramedWindows);
        });
    }

    /// <summary>A step that recognises a window, written the way the fields say and read once.</summary>
    private static (AddActionViewModel ViewModel, WindowPreviewViewModel Preview) Rule(
        WindowsForChecks windows, string text, string how = "titleContains")
    {
        var viewModel = Open("window.exists");
        viewModel.Windows = windows;
        PutWindowRule(viewModel, text, how);

        var preview = viewModel.WindowPreview!;
        preview.RefreshCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return (viewModel, preview);
    }

    private static void PutWindowRule(AddActionViewModel viewModel, string text, string how)
    {
        var parameter = viewModel.Parameters.First(item => item.Definition.Name == "window");
        var rule = how switch
        {
            "process" or "processEquals" => new WindowRule(Process: text, How: how),
            "class" or "classEquals" => new WindowRule(ClassName: text, How: how),
            _ => new WindowRule(Title: text, How: how),
        };
        parameter.ApplyWindowRule(rule);
    }

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
    private static ActionEditorWindow Shell(string key)
    {
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("wanted"), []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        ((AddActionViewModel)window.DataContext!).SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
