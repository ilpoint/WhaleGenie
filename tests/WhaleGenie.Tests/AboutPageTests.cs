using System;
using System.IO;
using WhaleGenie.Localization;

namespace WhaleGenie.Tests;

/// <summary>
/// The support section, the project page and the notices are all on the web About page now. The
/// checks keep the two codes packed with the page, both languages present, and the notices routed
/// through the same document the release carries.
/// </summary>
public class AboutPageTests
{
    [Fact]
    public void The_web_about_page_carries_both_valid_support_assets()
    {
        var page = File.ReadAllText(AboutPage());
        Assert.Contains("donate-wechat.png", page, StringComparison.Ordinal);
        Assert.Contains("donate-alipay.png", page, StringComparison.Ordinal);

        foreach (var name in new[] { "donate-wechat.png", "donate-alipay.png" })
        {
            var path = RepoPaths.Path("webui", "src", "assets", name);
            Assert.True(File.Exists(path), name);
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 8, name);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        }
    }

    [Fact]
    public void The_support_text_is_in_both_languages()
    {
        Assert.Equal("支持开发者", Strings.Chinese["About.SupportTitle"]);
        Assert.Contains("支持开发者", Strings.Chinese["About.SupportText"], StringComparison.Ordinal);

        Assert.Equal("Support the developer", Strings.English["About.SupportTitle"]);
        Assert.Contains("support the developer",
            Strings.English["About.SupportText"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_project_page_and_the_notices_stay_reachable_from_about()
    {
        var page = File.ReadAllText(AboutPage());
        Assert.Contains("app.openExternal", page, StringComparison.Ordinal);
        Assert.Contains("app.about", page, StringComparison.Ordinal);
        Assert.Contains("notices", page, StringComparison.Ordinal);

        var bridge = File.ReadAllText(RepoPaths.Source("WhaleGenie.App", "Bridge", "AppBridge.cs"));
        Assert.Contains("ProjectLinks.Home", bridge, StringComparison.Ordinal);
        Assert.Contains("ThirdPartyNotices.Read()", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_carries_the_same_donation_message_and_codes()
    {
        var readme = File.ReadAllText(RepoPaths.Path("README.md"));

        Assert.Contains("## 打赏支持", readme, StringComparison.Ordinal);
        Assert.Contains("WhaleGenie（鲸灵）是完全免费的开源项目。", readme, StringComparison.Ordinal);
        Assert.Contains("请🐋吃白饭🍚", readme, StringComparison.Ordinal);
        Assert.Contains("src/WhaleGenie/Assets/DonateWeChat.png", readme, StringComparison.Ordinal);
        Assert.Contains("src/WhaleGenie/Assets/DonateAlipay.png", readme, StringComparison.Ordinal);

        Assert.True(File.Exists(RepoPaths.Source("WhaleGenie", "Assets", "DonateWeChat.png")));
        Assert.True(File.Exists(RepoPaths.Source("WhaleGenie", "Assets", "DonateAlipay.png")));
    }

    private static string AboutPage()
        => RepoPaths.Path("webui", "src", "features", "about", "about-page.ts");
}
