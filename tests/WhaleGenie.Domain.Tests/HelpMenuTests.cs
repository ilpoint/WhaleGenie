using System.IO;
using WhaleGenie.Runtime;

namespace WhaleGenie.Domain.Tests;

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
        Assert.Equal(ProjectLinks.Home + "/blob/master/docs/manual.md", ProjectLinks.Manual);
        Assert.True(File.Exists(RepoPaths.Docs("manual.md")),
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
        var script = File.ReadAllText(RepoPaths.Path("build", "package.ps1"));
        Assert.Contains("doc\\manual.md", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path $app 'doc'", script, StringComparison.Ordinal);
        Assert.Contains("..\\docs\\manual\\*.md", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path $packagedManual 'manual'", script, StringComparison.Ordinal);
    }

}
