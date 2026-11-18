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

// The frame drawn around each window the list shows, and how it follows a move.
public partial class WindowPreviewTests
{
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

            var text = Parameter(viewModel, "window");
            Choose(viewModel, "inputMode", "background");
            PutWindowRule(viewModel, "notepad", "process");
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
}
