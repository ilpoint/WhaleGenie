using System.Text.RegularExpressions;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

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
    public void The_library_that_carries_driver_level_input_is_named_with_its_licence()
    {
        // It is not a package and not fetched by NuGet: it travels in the repository and in the
        // release, and it is the one component the program links against that is licensed under the
        // GPL rather than permissively, so it has to be named with where it came from.
        var notices = Notices();

        Assert.Contains("libVIIPER", notices, StringComparison.Ordinal);
        Assert.Contains("GPL-3.0", notices, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Alia5/VIIPER", notices, StringComparison.Ordinal);

        // And the driver, which is not ours and is still the user's to install, stays on the list.
        Assert.Contains("usbip-win2", notices, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_carries_the_library_and_its_own_licence_file()
    {
        // The notices say the library is distributed with the program; the packaging script is what
        // makes that true. The DLL is moved into lib by the rule that moves every native library, so
        // what is pinned down here is the licence file that has to travel beside it.
        var script = File.ReadAllText(Path.Combine(Repository(), "build", "package.ps1"));
        var project = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie.Core", "WhaleGenie.Core.csproj"));

        Assert.Contains("third_party\\libVIIPER\\libVIIPER.dll", project, StringComparison.Ordinal);
        Assert.Contains("libVIIPER-LICENSES.txt", script, StringComparison.Ordinal);
        Assert.Contains("third_party\\libVIIPER\\licenses.txt", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_carries_the_licence_and_the_notices()
    {
        // The three documents only do their job when they are unpacked beside the executable, which
        // is what the packaging script decides. The quotes around NOTICE matter: without them the
        // assertion would be satisfied by THIRD-PARTY-NOTICES.md carrying the substring.
        var script = File.ReadAllText(Path.Combine(Repository(), "build", "package.ps1"));

        Assert.Contains("LICENSE.txt", script, StringComparison.Ordinal);
        Assert.Contains("'NOTICE'", script, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_licence_file_holds_the_licence_text_and_the_credits_stay_apart()
    {
        // What a repository is licensed under is decided from these bytes. Anything in front of the
        // licence's own text — a project name, a copyright line — is enough to make that come back
        // as "Other", so the credits live in NOTICE and this file starts where the licence starts.
        var licence = Normalize(File.ReadAllText(Path.Combine(Repository(), "LICENSE.txt")));
        var notice = Normalize(File.ReadAllText(Path.Combine(Repository(), "NOTICE")));

        Assert.StartsWith("GNU AFFERO GENERAL PUBLIC LICENSE", licence.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("WhaleGenie", licence, StringComparison.Ordinal);

        Assert.Contains("WhaleGenie", notice, StringComparison.Ordinal);
        Assert.Contains("Copyright (C) 2026 ilpoint", notice, StringComparison.Ordinal);
        Assert.Contains("AGPL-3.0", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void The_about_box_opens_the_notices_it_promises()
    {
        // The button on About says there is a list; this is what makes sure the list it opens is
        // this document rather than a promise with nothing behind it.
        var window = Path.Combine(Repository(), "WhaleGenie", "Views", "AboutWindow.axaml.cs");
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
