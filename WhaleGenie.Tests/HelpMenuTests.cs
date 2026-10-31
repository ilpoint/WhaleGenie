using System.IO;
using WhaleGenie.Runtime;

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
    [InlineData("OnAboutClicked")]
    public void The_window_answers_a_click_on_each_help_item(string handler)
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));
        Assert.Contains($"Click=\"{handler}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_lives_in_the_menu_rather_than_on_the_title_bar()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));

        // 标题栏只留置顶：一个问号与一个换主题的钮都不在这里了，帮助走菜单里的「关于」。
        Assert.DoesNotContain("Name=\"HelpButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"ThemeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AlwaysOnTopButton\"", xaml, StringComparison.Ordinal);
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
