using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The support section starts folded, loads its two pictures only when asked for, and keeps the
/// longer documents reachable from the same window.
/// </summary>
public class AboutWindowTests
{
    [Fact]
    public void Support_codes_start_collapsed_and_load_only_when_expanded()
    {
        Ui.Run(() =>
        {
            var window = new AboutWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var codes = window.FindControl<Grid>("SupportCodes")!;
            var button = window.FindControl<Button>("SupportButton")!;
            var supportText = window.FindControl<TextBlock>("SupportText")!;
            var weChat = window.FindControl<Image>("WeChatQr")!;
            var alipay = window.FindControl<Image>("AlipayQr")!;

            Assert.False(codes.IsVisible);
            Assert.Equal(430, window.Height);
            Assert.Equal(Strings.Get("About.SupportButton"), button.Content);
            Assert.Null(weChat.Source);
            Assert.Null(alipay.Source);
            var textBottom = supportText.TranslatePoint(
                new Avalonia.Point(0, supportText.Bounds.Height), window)!.Value.Y;
            var buttonTop = button.TranslatePoint(default, window)!.Value.Y;
            Assert.True(buttonTop >= textBottom);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(codes.IsVisible);
            Assert.Equal(720, window.Height);
            Assert.Equal(Strings.Get("About.SupportCollapse"), button.Content);
            Assert.NotNull(weChat.Source);
            Assert.NotNull(alipay.Source);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(codes.IsVisible);
            Assert.Equal(430, window.Height);
            Assert.Equal(Strings.Get("About.SupportButton"), button.Content);
            window.Close();
        });
    }

    [Fact]
    public void The_about_window_has_both_valid_support_assets()
    {
        var code = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AboutWindow.axaml.cs"));

        Assert.Contains("DonateWeChat.png", code, StringComparison.Ordinal);
        Assert.Contains("DonateAlipay.png", code, StringComparison.Ordinal);

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
    public void The_support_text_is_in_both_languages()
    {
        Assert.Equal("支持开发者", Strings.Chinese["About.SupportTitle"]);
        Assert.Contains("支持开发者",
            Strings.Chinese["About.SupportText"], StringComparison.Ordinal);

        Assert.Equal("Support the developer", Strings.English["About.SupportTitle"]);
        Assert.Contains("support the developer",
            Strings.English["About.SupportText"], StringComparison.Ordinal);
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
