using System.Text;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// The introduction page is built from the site sources, the screenshots the README already uses,
/// and the files the program carries. These checks keep a renamed picture or a workflow that deploys
/// the wrong folder from turning the published page into a set of broken frames.
/// </summary>
public class SiteTests
{
    [Fact]
    public void The_introduction_page_only_names_assets_the_site_has()
    {
        var root = Repository();
        var html = File.ReadAllText(Path.Combine(root, "site", "index.html"));
        var vibe = File.ReadAllText(Path.Combine(root, "site", "vibe", "index.html"));
        var wanted = Regex.Matches(html + vibe, "(?:src|href)=\"(?:\\.\\./)?assets/([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();

        Assert.NotEmpty(wanted);

        // Pictures the program and README already carry are copied by build/site.ps1 rather than
        // committed a second time; only the files the site itself creates live under site/assets.
        var borrowed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["logo.png"] = Path.Combine("WhaleGenie", "Assets", "WhaleGenie.png"),
            ["donate-wechat.png"] = Path.Combine("WhaleGenie", "Assets", "DonateWeChat.png"),
            ["donate-alipay.png"] = Path.Combine("WhaleGenie", "Assets", "DonateAlipay.png"),
            ["main-window.png"] = Path.Combine("doc", "images", "main-window.png"),
            ["macro-editor.png"] = Path.Combine("doc", "images", "macro-editor.png"),
        };

        foreach (var name in wanted)
        {
            if (File.Exists(Path.Combine(root, "site", "assets", name)))
            {
                continue;
            }

            Assert.True(borrowed.TryGetValue(name, out var source), $"{name} has no source");
            Assert.True(File.Exists(Path.Combine(root, source!)), $"{source} is missing");
        }
    }

    [Fact]
    public void The_short_video_and_its_cover_are_real_files()
    {
        var root = Repository();
        var video = Path.Combine(root, "site", "assets", "whalegenie-vibe.mp4");
        var bytes = File.ReadAllBytes(video);

        Assert.True(bytes.Length > 500_000, "the introduction video is missing or empty");
        Assert.Equal("ftyp", Encoding.ASCII.GetString(bytes, 4, 4));

        Assert.True(new FileInfo(Path.Combine(root, "site", "assets",
            "whalegenie-vibe-poster.png")).Length > 20_000);
        Assert.True(new FileInfo(Path.Combine(root, "site", "assets",
            "whalegenie-og.png")).Length > 20_000);
    }

    [Fact]
    public void Pages_publishes_the_folder_the_build_script_assembles()
    {
        var root = Repository();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "pages.yml"));
        var script = File.ReadAllText(Path.Combine(root, "build", "site.ps1"));

        Assert.Contains("build/site.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("path: _site", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-pages-artifact", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/deploy-pages", workflow, StringComparison.Ordinal);

        // The screenshots and the logo stay in the places the rest of the repository already reads.
        Assert.Contains("doc/images/main-window.png", script, StringComparison.Ordinal);
        Assert.Contains("doc/images/macro-editor.png", script, StringComparison.Ordinal);
        Assert.Contains("WhaleGenie/Assets/WhaleGenie.png", script, StringComparison.Ordinal);
        Assert.Contains("WhaleGenie/Assets/DonateWeChat.png", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_points_at_the_published_introduction()
    {
        var readme = File.ReadAllText(Path.Combine(Repository(), "README.md"));
        Assert.Contains("https://ilpoint.github.io/WhaleGenie/", readme, StringComparison.Ordinal);
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
