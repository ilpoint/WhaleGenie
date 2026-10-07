using System.Text.RegularExpressions;
using WhaleGenie.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Tests;

/// <summary>
/// The list of other people's work is a document that has to travel with the release and be
/// reachable from inside the program. These read the three things that keep it honest: the icon's
/// author is named the way CC BY 4.0 asks, every package the program references has a line, and
/// what the About box shows is the same text the repository keeps.
/// </summary>
public class ThirdPartyNoticesTests
{
    [Fact]
    public void The_icon_names_its_author_and_its_licence()
    {
        // CC BY 4.0 is only satisfied by naming the author, the licence and the changes made, so
        // each of those has to be there — and the box a user actually sees says it too.
        var notices = Notices();

        Assert.Contains("Emojiall", notices, StringComparison.Ordinal);
        Assert.Contains("CC BY 4.0", notices, StringComparison.Ordinal);
        Assert.Contains("https://www.emojiall.com/zh-hans/platform-pixel", notices, StringComparison.Ordinal);
        Assert.Contains("https://creativecommons.org/licenses/by/4.0/", notices, StringComparison.Ordinal);
        Assert.Contains("缩放修改", notices, StringComparison.Ordinal);

        foreach (var text in new[] { Strings.English["Main.AboutText"], Strings.Chinese["Main.AboutText"] })
        {
            Assert.Contains("Emojiall", text, StringComparison.Ordinal);
            Assert.Contains("CC BY 4.0", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_package_the_program_references_is_named()
    {
        var notices = Notices();
        var missing = new List<string>();

        foreach (var project in new[] { "WhaleGenie", "WhaleGenie.Core" })
        {
            var project_file = Path.Combine(Repository(), project, project + ".csproj");
            foreach (Match match in Regex.Matches(File.ReadAllText(project_file),
                         "PackageReference Include=\"([^\"]+)\""))
            {
                var package = match.Groups[1].Value;
                if (!notices.Contains(package, StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add(package);
                }
            }
        }

        Assert.Empty(missing.Order());
    }

    [Fact]
    public void The_release_carries_the_licence_and_the_notices()
    {
        // The two documents only do their job when they are unpacked beside the executable, which
        // is what the packaging script decides.
        var script = File.ReadAllText(Path.Combine(Repository(), "build", "package.ps1"));

        Assert.Contains("LICENSE.txt", script, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_about_box_opens_the_notices_it_promises()
    {
        // The button on About says there is a list; this is what makes sure the list it opens is
        // this document rather than a promise with nothing behind it.
        var window = Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml.cs");
        var code = File.ReadAllText(window);

        Assert.Contains("ThirdPartyNotices.Read()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_about_box_shows_the_document_the_repository_keeps()
    {
        // The list is linked into the assembly rather than copied into it, so this is what would
        // catch the link breaking and the box opening an empty document.
        var embedded = Ui.Run(ThirdPartyNotices.Read);
        var on_disk = File.ReadAllText(Path.Combine(Repository(), "THIRD-PARTY-NOTICES.md"));

        Assert.Equal(Normalize(on_disk), Normalize(embedded));
    }

    private static string Notices()
        => File.ReadAllText(Path.Combine(Repository(), "THIRD-PARTY-NOTICES.md"));

    /// <summary>Line endings are the checkout's business, not the text's.</summary>
    private static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

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
