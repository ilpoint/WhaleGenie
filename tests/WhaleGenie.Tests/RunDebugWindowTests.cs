using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The run-and-debug window and the list that opens it are two windows, not one window and a
/// dialog hanging off it. The list goes into the notification area whenever it is minimized, and
/// anything owned by it is taken down with it — so a run being watched would lose the window
/// watching it, and the button that opens it would find a window that is not on the screen.
/// </summary>
public class RunDebugWindowTests
{
    /// <summary>
    /// The list is put away and the run window stays: a run that is still going is watched in a
    /// window of its own, not in one that disappears with the list.
    /// </summary>
    [Fact]
    public void The_run_window_stays_when_the_list_is_put_away()
    {
        Ui.Run(() =>
        {
            var main = new MainWindow { DataContext = new MainViewModel() };
            main.Show();
            Dispatcher.UIThread.RunJobs();

            var run = OpenRunDebug(main);
            Assert.NotNull(run);
            Assert.True(run!.IsVisible);

            // The list goes into the notification area, which is what minimizing it does.
            main.Hide();
            Dispatcher.UIThread.RunJobs();

            Assert.False(main.IsVisible);
            Assert.True(run.IsVisible);
            Assert.DoesNotContain(run, main.OwnedWindows);

            run.Close();
            main.Close();
        });
    }

    /// <summary>
    /// The run window is put away in its own right — minimized to the taskbar, or hidden by a
    /// scope that wanted the screen — and the button that opens it brings it back rather than
    /// finding a window that is not there.
    /// </summary>
    [Fact]
    public void The_button_brings_back_the_run_window_that_was_put_away()
    {
        Ui.Run(() =>
        {
            var main = new MainWindow { DataContext = new MainViewModel() };
            main.Show();
            Dispatcher.UIThread.RunJobs();

            var run = OpenRunDebug(main);
            Assert.NotNull(run);

            // Minimized to the taskbar: the window is still open, but it is not on the screen.
            run!.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Minimized, run.WindowState);

            // Hidden outright, which is what a scope that took the screen away does.
            run.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.False(run.IsVisible);

            var again = OpenRunDebug(main);
            Assert.Same(run, again);
            Assert.True(again!.IsVisible);
            Assert.Equal(WindowState.Normal, again.WindowState);

            again.Close();
            main.Close();
        });
    }

    /// <summary>
    /// The window that is opened is the one that is still open: the button does not pile a second
    /// window over the first, whichever way the first was put away.
    /// </summary>
    [Fact]
    public void The_button_opens_one_window_and_brings_that_one_back()
    {
        Ui.Run(() =>
        {
            var main = new MainWindow { DataContext = new MainViewModel() };
            main.Show();
            Dispatcher.UIThread.RunJobs();

            var run = OpenRunDebug(main);
            Assert.NotNull(run);

            run!.Hide();
            Dispatcher.UIThread.RunJobs();

            var again = OpenRunDebug(main);
            Assert.Same(run, again);
            Assert.True(again!.IsVisible);

            // Nothing of the run window is owned by the list, which is what lets it stand on the
            // screen while the list is in the notification area: a second window would show up
            // there instead of here.
            Assert.Empty(main.OwnedWindows);

            again!.Close();
            main.Close();
        });
    }

    /// <summary>
    /// Presses the button on the list that opens the run-and-debug window, and says which window
    /// that left on the screen.
    /// </summary>
    private static RunWindow? OpenRunDebug(MainWindow main)
    {
        main.FindControl<Button>("RunDebugButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        return main.RunDebug;
    }
}
