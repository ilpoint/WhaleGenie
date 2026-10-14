using System.IO;
using WhaleGenie.Execution;

namespace WhaleGenie.Tests;

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
            Assert.StartsWith("https://github.com/ilpoint/WhaleGenie", link, StringComparison.Ordinal));

        Assert.Equal(ProjectLinks.Home + "/issues", ProjectLinks.Feedback);
        Assert.Equal(ProjectLinks.Home + "/releases", ProjectLinks.Releases);
        Assert.Equal(ProjectLinks.Home + "/releases/latest", ProjectLinks.LatestRelease);
    }

    [Fact]
    public void The_get_started_item_opens_the_manual_this_repository_keeps()
    {
        // The page only helps if it is the one that is really in the repository under the name the
        // link spells out, so both halves are checked: where the link points, and that the file is
        // there.
        Assert.Equal(ProjectLinks.Home + "/blob/master/doc/manual.md", ProjectLinks.Manual);
        Assert.True(File.Exists(Path.Combine(Repository(), "doc", "manual.md")),
            "the manual the Get started item opens is not in the repository");
    }

    /// <summary>
    /// 装好程序的人手上没有仓库，也不一定有网：包里的那本手册才是「快速上手」该打开的东西，找不到
    /// 才回落到仓库那一页。哪一份都没有时，宁可给一个打不开的地址，也不能什么都不给。
    /// </summary>
    [Fact]
    public void The_get_started_item_prefers_the_manual_that_came_with_the_program()
    {
        var folder = Directory.CreateTempSubdirectory("whalegenie-manual").FullName;

        try
        {
            // 还没有那份手册时，打开的是仓库里那一页。
            Assert.Equal(ProjectLinks.Manual, Manual.Where(folder));

            Directory.CreateDirectory(Path.Combine(folder, "doc"));
            File.WriteAllText(Manual.Packaged(folder), "# 手册");

            Assert.Equal(Manual.Packaged(folder), Manual.Where(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        // 打包脚本抄的是同一个位置，否则程序包里有手册也找不到。
        var script = File.ReadAllText(Path.Combine(Repository(), "build", "package.ps1"));
        Assert.Contains("doc\\manual.md", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path $app 'doc'", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OnWhatsNewClicked")]
    [InlineData("OnUpdatesClicked")]
    [InlineData("OnFeedbackClicked")]
    [InlineData("OnGetStartedClicked")]
    [InlineData("OnInsertSampleClicked")]
    [InlineData("OnAboutClicked")]
    public void The_window_answers_a_click_on_each_help_item(string handler)
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));
        Assert.Contains($"Click=\"{handler}\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// 空列表上也要有这一条：手上什么都没有的人最先看到的就是那一页，示例宏只在帮助菜单里的人
    /// 找不到它。按钮与菜单项走的是同一个方法，所以两边不会各说各的。
    /// </summary>
    [Fact]
    public void An_empty_macro_list_offers_the_sample_as_well()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));

        var empty = xaml.IndexOf("IsVisible=\"{Binding !HasMacros}\"", StringComparison.Ordinal);
        Assert.True(empty >= 0, "the empty state of the macro list is gone");

        var state = xaml[empty..xaml.IndexOf("</StackPanel>", empty, StringComparison.Ordinal)];
        Assert.Contains("Name=\"InsertSampleButton\"", state, StringComparison.Ordinal);
        Assert.Contains("Main.InsertSample", state, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "MainWindow.axaml.cs"));
        Assert.Contains("InsertSampleButton", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_mark_on_the_title_bar_opens_the_same_thing_as_About()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));

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
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
