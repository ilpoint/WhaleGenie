using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WhaleGenie.Localization;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The donation section is made of two pictures and the words that explain them. A missing asset
/// would leave an empty frame in the About window, and text written in only one place would let the
/// file people read on the project page drift away from the one the program shows.
/// </summary>
public class AboutWindowTests
{
    [Fact]
    public void The_about_window_shows_both_donation_codes()
    {
        var xaml = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AboutWindow.axaml"));

        Assert.Contains("Name=\"SupportPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"/Assets/DonateWeChat.png\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"/Assets/DonateAlipay.png\"", xaml, StringComparison.Ordinal);

        Ui.Run(() =>
        {
            using var wechat = AssetLoader.Open(Asset("DonateWeChat.png"));
            using var alipay = AssetLoader.Open(Asset("DonateAlipay.png"));
            using var wechatImage = new Bitmap(wechat);
            using var alipayImage = new Bitmap(alipay);

            Assert.True(wechatImage.PixelSize.Width > 0);
            Assert.True(wechatImage.PixelSize.Height > 0);
            Assert.True(alipayImage.PixelSize.Width > 0);
            Assert.True(alipayImage.PixelSize.Height > 0);
        });
    }

    [Fact]
    public void The_donation_text_is_in_both_languages()
    {
        Assert.Equal("打赏支持", Strings.Chinese["About.SupportTitle"]);
        Assert.Contains("WhaleGenie（鲸灵）是完全免费的开源项目。",
            Strings.Chinese["About.SupportText"], StringComparison.Ordinal);
        Assert.Contains("请🐋吃白饭🍚", Strings.Chinese["About.SupportText"], StringComparison.Ordinal);

        Assert.Equal("Support WhaleGenie", Strings.English["About.SupportTitle"]);
        Assert.Contains("completely free open-source project",
            Strings.English["About.SupportText"], StringComparison.Ordinal);
        Assert.Contains("🐋", Strings.English["About.SupportText"], StringComparison.Ordinal);
        Assert.Contains("🍚", Strings.English["About.SupportText"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_project_page_and_the_notices_stay_reachable_from_about()
    {
        var code = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AboutWindow.axaml.cs"));

        Assert.Contains("WebPage.Open(ProjectLinks.Home)", code, StringComparison.Ordinal);
        Assert.Contains("ThirdPartyNotices.Read()", code, StringComparison.Ordinal);

        // The About window itself has to follow the rule that keeps dialogs above a pinned main
        // window, which means it is opened through the helper rather than by calling ShowDialog.
        var main = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml.cs"));
        Assert.Contains("new AboutWindow().ShowDialogOver(this)", main, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_carries_the_same_donation_message_and_codes()
    {
        var root = Repository();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("## 打赏支持", readme, StringComparison.Ordinal);
        Assert.Contains("WhaleGenie（鲸灵）是完全免费的开源项目。", readme, StringComparison.Ordinal);
        Assert.Contains("请🐋吃白饭🍚", readme, StringComparison.Ordinal);
        Assert.Contains("WhaleGenie/Assets/DonateWeChat.png", readme, StringComparison.Ordinal);
        Assert.Contains("WhaleGenie/Assets/DonateAlipay.png", readme, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "WhaleGenie", "Assets", "DonateWeChat.png")));
        Assert.True(File.Exists(Path.Combine(root, "WhaleGenie", "Assets", "DonateAlipay.png")));
    }

    private static Uri Asset(string name)
        => new($"avares://{typeof(AboutWindow).Assembly.GetName().Name}/Assets/{name}");

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
