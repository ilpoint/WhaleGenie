using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Views;

/// <summary>
/// Says what the program is, names the work it stands on, and shows where somebody who finds it
/// useful can leave a tip. The confirmation dialog could not carry the two codes, so this window
/// owns both the text and the two buttons that open longer documents.
/// </summary>
public partial class AboutWindow : AppWindow
{
    private const double CollapsedHeight = 430;
    private const double ExpandedHeight = 720;
    private readonly Grid _supportCodes;
    private readonly Button _supportButton;
    private readonly Image _weChatQr;
    private readonly Image _alipayQr;

    public AboutWindow()
    {
        InitializeComponent();

        Title = Strings.Get("Main.About");
        this.FindControl<TextBlock>("AboutText")!.Text =
            Strings.Format("Main.AboutText", AppVersion.Text);
        _supportCodes = this.FindControl<Grid>("SupportCodes")!;
        _supportButton = this.FindControl<Button>("SupportButton")!;
        _weChatQr = this.FindControl<Image>("WeChatQr")!;
        _alipayQr = this.FindControl<Image>("AlipayQr")!;

    }

    private void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Home);

    private void OnSupportClicked(object? sender, RoutedEventArgs e)
    {
        var expand = !_supportCodes.IsVisible;
        _supportCodes.IsVisible = expand;
        Height = expand ? ExpandedHeight : CollapsedHeight;
        _supportButton.Content = Strings.Get(expand
            ? "About.SupportCollapse"
            : "About.SupportButton");
        if (!expand)
        {
            return;
        }

        _weChatQr.Source ??= new Bitmap(AssetLoader.Open(
            new Uri("avares://WhaleGenie/Assets/DonateWeChat.png")));
        _alipayQr.Source ??= new Bitmap(AssetLoader.Open(
            new Uri("avares://WhaleGenie/Assets/DonateAlipay.png")));
    }

    private async void OnNoticesClicked(object? sender, RoutedEventArgs e)
        => await ConfirmDialog.ShowAsync(this, Strings.Get("Main.AboutNotices"),
            ThirdPartyNotices.Read(), Strings.Get("Common.Ok"), showCancel: false, height: 460);

    private void OnDoneClicked(object? sender, RoutedEventArgs e) => Close();


}
