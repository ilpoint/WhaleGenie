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

// What is listed but never framed: the dialog, the indicator, and this program itself.
public partial class WindowPreviewTests
{
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

            Parameter(viewModel, "window").WriteValue(Fields.Shape("Notepad"));
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

    /// <summary>
    /// An indicator frame is not a window anything can recognise: the window device lists the
    /// top-level windows that carry a title, and this one deliberately carries none. A frame that
    /// could be found would be a window a rule answers with — it stands above every window that is
    /// not pinned — so an empty match text, which means the window in front, would come back as the
    /// ring just drawn, and both the preview and a run would be chasing or reading a drawing.
    /// </summary>
    [Fact]
    public void An_indicator_frame_is_not_a_window_a_rule_can_find()
    {
        Ui.Run(() =>
        {
            var frame = ScreenFrameWindow.Show(new ScreenPoint(10, 20), new ScreenSize(100, 50));

            Assert.NotNull(frame);
            Assert.True(string.IsNullOrEmpty(frame.Title),
                $"the frame carries the title '{frame.Title}', so a window rule can recognise it");

            frame.Close();
        });
    }

    /// <summary>
    /// The window asking the question is not framed: it stands in front of the very thing being
    /// looked for, and a ring drawn at its edge is read as that window's own border rather than as
    /// an answer. The list keeps it either way — an empty match text really does mean the window in
    /// front, and what the list says is what a run would do.
    /// </summary>
    [Fact]
    public void The_window_asking_is_listed_but_not_framed()
    {
        Ui.Run(() =>
        {
            var handles = AppWindows.Handles;
            AppWindows.Handles = () => [2L];
            try
            {
                var window = Shell("window.exists");
                var viewModel = (AddActionViewModel)window.DataContext!;
                viewModel.Windows = Desk()
                    .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                    .Add(2, "WhaleGenie", "whalegenie", "Win32");

                Parameter(viewModel, "window").WriteValue(Fields.Shape("e"));
                var preview = viewModel.WindowPreview!;
                preview.RefreshCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(2, preview.Rows.Count);
                Assert.Equal([1L], window.FramedWindows);
            }
            finally
            {
                AppWindows.Handles = handles;
            }
        });
    }

    /// <summary>
    /// The editor writing a window trigger is not framed either, and for a sharper reason: a rule
    /// written wide enough to fire on the window it is written in really would fire on it, so the
    /// list has to say so. The frame is what stays off — the editor is already in front of the
    /// person reading the list.
    /// </summary>
    [Fact]
    public void The_editor_asking_is_listed_but_not_framed()
    {
        Ui.Run(() =>
        {
            var handles = AppWindows.Handles;
            AppWindows.Handles = () => [1L];
            try
            {
                var devices = new ChecksDevices
                {
                    Windows = Desk()
                        .Add(1, "宏编辑器 - probe", "whalegenie", "Win32")
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
                viewModel.UseWindowTarget(new WindowRule(Title: "o"));
                Dispatcher.UIThread.RunJobs();
                viewModel.WindowPreview.RefreshCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(2, viewModel.WindowPreview.Rows.Count);
                Assert.Equal([2L], window.FramedWindows);
            }
            finally
            {
                AppWindows.Handles = handles;
            }
        });
    }
}
