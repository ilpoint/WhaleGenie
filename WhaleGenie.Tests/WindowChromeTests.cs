using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The strip at the top of a window: the whale, the window's name and the room the caption buttons
/// need. It is what makes a window draggable, because dragging only begins once the title bar is
/// handed over to the content — a window that leaves the bar to the library draws one with no whale
/// on it, and nothing above the content can be grabbed.
/// </summary>
public class WindowChromeTests
{
    [Fact]
    public void The_strip_carries_the_whale_and_the_windows_name()
    {
        var (extends, icon, title, rows) = Ui.Run(() =>
        {
            var window = new AppWindow { Title = "Probe", Content = new Border() };
            WindowChrome.Attach(window);

            var chrome = Assert.IsType<Grid>(window.Content);
            var bar = Assert.IsType<Grid>(chrome.Children[0]);
            var text = bar.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => block.Text == "Probe");

            var result = (
                window.TitleBar.ExtendsContentIntoTitleBar,
                window.Icon is not null,
                text.Text,
                chrome.RowDefinitions.Count);

            window.Close();
            return result;
        });

        Assert.True(extends, "the title bar was left to the library, so there is nothing to drag");
        Assert.True(icon, "the window has no icon");
        Assert.Equal("Probe", title);
        Assert.Equal(2, rows);
    }

    [Fact]
    public void The_name_follows_the_window_as_it_changes()
    {
        var shown = Ui.Run(() =>
        {
            var window = new AppWindow { Title = "First", Content = new Border() };
            WindowChrome.Attach(window);

            window.Title = "Second";
            Dispatcher.UIThread.RunJobs();

            var bar = Assert.IsType<Grid>(Assert.IsType<Grid>(window.Content).Children[0]);
            var text = bar.GetVisualDescendants().OfType<TextBlock>()
                .First(block => !string.IsNullOrEmpty(block.Text)).Text;

            window.Close();
            return text;
        });

        Assert.Equal("Second", shown);
    }

    [Fact]
    public void Attaching_twice_leaves_one_strip()
    {
        var depth = Ui.Run(() =>
        {
            var window = new AppWindow { Title = "Probe", Content = new Border() };
            WindowChrome.Attach(window);
            WindowChrome.Attach(window);

            var chrome = Assert.IsType<Grid>(window.Content);
            var depth = chrome.GetVisualDescendants().OfType<TextBlock>()
                .Count(block => block.Text == "Probe");

            window.Close();
            return depth;
        });

        Assert.Equal(1, depth);
    }

    /// <summary>
    /// The windows that carry their own content: the four that were asked for, plus settings. A
    /// window added here without the call is a window that looks like the library rather than like
    /// the rest of the program.
    /// </summary>
    [Fact]
    public void The_content_windows_wear_it()
    {
        foreach (var name in new[]
                 {
                     "GlobalVariablesWindow.axaml.cs",
                     "LookWindow.axaml.cs",
                     "ExpressionBuilderWindow.axaml.cs",
                     "BreakpointWindow.axaml.cs",
                     "SettingsWindow.axaml.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", name));
            Assert.Contains("WindowChrome.Attach(this);", source, StringComparison.Ordinal);
        }
    }

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
