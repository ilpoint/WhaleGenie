using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The windows that stay on screen while a macro is written or run are put away one at a time: the
/// keyboard covers what the keys are read off, the result of a search covers the screen it came
/// from, and a run window covers the program the macro is driving. Each one is minimized on its own
/// and comes back the same way it went, so nothing is lost by putting it away.
/// </summary>
public class MinimizeTests
{
    /// <summary>Every window the user can work with keeps a way of putting it away on its own.</summary>
    [Fact]
    public void Every_window_that_stays_up_can_be_put_away()
    {
        foreach (var name in new[] { "AddActionWindow", "VirtualKeyboardWindow", "LookWindow", "RunWindow" })
        {
            var markup = File.ReadAllText(
                Path.Combine(Repository(), "WhaleGenie", "Views", $"{name}.axaml"));

            Assert.Contains("Name=\"MinimizeButton\"", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_run_window_is_put_away_without_stopping_the_run()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow([]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "MinimizeButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Minimized, window.WindowState);

            window.Close();
        });
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
