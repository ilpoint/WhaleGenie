using System.IO;
using Viktor.Execution;

namespace Viktor.Tests;

/// <summary>
/// The help menu spent a long time full of items that did nothing when they were clicked, and the
/// "?" on the title bar was one of them. These read the two things that keep them honest: what the
/// links point at, and whether the window really answers a click on each of them.
/// </summary>
public class HelpMenuTests
{
    [Fact]
    public void Every_help_link_opens_a_page_of_this_project()
    {
        string[] links =
        [
            ProjectLinks.Home,
            ProjectLinks.Feedback,
            ProjectLinks.Releases,
            ProjectLinks.LatestRelease,
        ];

        // Nothing here may point somewhere else: a wrong address is worse than no item at all,
        // because it looks like it worked.
        Assert.All(links, link =>
            Assert.StartsWith("https://github.com/ilpoint/Viktor", link, StringComparison.Ordinal));

        Assert.Equal(ProjectLinks.Home + "/issues", ProjectLinks.Feedback);
        Assert.Equal(ProjectLinks.Home + "/releases", ProjectLinks.Releases);
        Assert.Equal(ProjectLinks.Home + "/releases/latest", ProjectLinks.LatestRelease);
    }

    [Theory]
    [InlineData("OnWhatsNewClicked")]
    [InlineData("OnUpdatesClicked")]
    [InlineData("OnFeedbackClicked")]
    [InlineData("OnAboutClicked")]
    public void The_window_answers_a_click_on_each_help_item(string handler)
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "Viktor", "Views", "MainWindow.axaml"));
        Assert.Contains($"Click=\"{handler}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_mark_on_the_title_bar_opens_the_same_thing_as_About()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "Viktor", "Views", "MainWindow.axaml"));

        // The button only counts as wired when the click lands on a handler, which is what it
        // was missing while it sat there doing nothing.
        var start = xaml.IndexOf("Name=\"HelpButton\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the title bar no longer has a help button");
        Assert.Contains("Click=\"OnAboutClicked\"", xaml[start..], StringComparison.Ordinal);
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "Viktor.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("Viktor.slnx was not found above the test binaries.");
    }
}
