using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// What a machine without the WebView2 runtime sees instead of a white frame. The native window
/// explains what is missing and opens the installer page; closing it ends the program, because
/// there is no interface to keep.
/// </summary>
internal static class WebViewMissingWindow
{
    public static void Show(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var install = new Button
        {
            Content = Strings.Get("WebView.Install"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        install.Click += (_, _) => WebPage.Open(WebViewRuntime.InstallUrl);

        var close = new Button
        {
            Content = Strings.Get("Shell.Window.Close"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var body = new TextBlock
        {
            Text = Strings.Get("WebView.MissingBody"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 460,
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = Strings.Get("WebView.MissingTitle"),
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                body,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { install, close },
                },
            },
        };

        var window = new Window
        {
            Title = Strings.Get("WebView.MissingTitle"),
            Width = 540,
            Height = 280,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel,
        };
        close.Click += (_, _) => window.Close();
        window.Closed += (_, _) => desktop.Shutdown();
        desktop.MainWindow = window;
        window.Show();
    }
}
