using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WhaleGenie.Bridge;

namespace WhaleGenie.Web;

/// <summary>
/// 一块透明、置顶、无边框的浮层，内容是同一份网页产物里的一个路由。壳与浮层是两个 WebView，
/// 各自附着在自己的桥面上：菜单只收菜单的推送，壳只收项目的变化。
/// </summary>
/// <remarks>
/// 窗口必须在程序启动时就建好并隐藏：S0 实测冷启首帧约 0.6 秒，而一个已建窗口的 <c>Show()</c>
/// 只要 19–25 毫秒。页面里的键盘由页面自己回传（同样来自 S0），宿主窗口不抢焦点。
/// </remarks>
internal sealed class WebOverlayWindow : Window
{
    private readonly NativeWebView _web = new();
    private readonly BridgeServer _bridge;
    private readonly BridgeSurface _surface;
    private readonly string _route;
    private WebBridge? _channel;

    /// <summary>Pinned overlays stay up when the user clicks another program.</summary>
    internal bool Pinned { get; set; }


    internal WebOverlayWindow(BridgeServer bridge, BridgeSurface surface, string route,
        double width, double height)
    {
        _bridge = bridge;
        _surface = surface;
        _route = route.TrimStart('/');
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Width = width;
        Height = height;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // The page paints nothing behind its own cards; a transparent control background is what
        // reaches the WebView2 controller as its default background.
        _web.Background = Brushes.Transparent;
        _web.AdapterCreated += OnAdapterCreated;

        Content = _web;
        _channel = new WebBridge(_web);
        _channel.Listen();
    }

    /// <summary>Loads the page once the adapter exists, attached to this surface.</summary>
    private void OnAdapterCreated(object? sender, EventArgs args)
    {
        if (_channel is not { } channel)
        {
            return;
        }

        _bridge.Attach(_surface, channel);
        var url = StartUrl();
        WhaleGenie.Runtime.AppDiagnostics.Note("overlay.url", url.AbsoluteUri);
        _web.Navigate(url);
    }

    private Uri StartUrl()
    {
#if DEBUG
        if (App.WebUiDev is { Length: > 0 } dev)
        {
            return new Uri(dev.TrimEnd('/') + "/#" + _route);
        }
#endif
        // Uri treats a relative "#..." against a file as a rooted path and escapes the hash, so the
        // fragment is set through the builder instead: that keeps the file path and yields the
        // hash the page's router reads.
        var page = new UriBuilder(new Uri(Path.Combine(AppContext.BaseDirectory, "webui", "index.html")))
        {
            // The stamp keeps WebView2 from answering a file navigation out of its cache: a page
            // built after the last launch would otherwise be served as the one before it.
            Query = "v=" + DateTime.Now.Ticks,
            Fragment = _route,
        };
        return page.Uri;
    }

    /// <summary>
    /// Puts the window next to the pointer, like the native menu: a menu that opens in the middle
    /// of the screen makes the person look for it. A position that would leave the display is
    /// pulled back inside the work area.
    /// </summary>
    internal void PlaceNearCursor()
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is not { } area)
        {
            return;
        }

        var work = area.WorkingArea;
        var cursor = TryCursor() ?? new PixelPoint(work.X + work.Width / 2, work.Y + work.Height / 2);
        var width = (int)Width;
        var height = (int)Height;
        var x = Math.Min(cursor.X, work.X + work.Width - width);
        var y = Math.Min(cursor.Y, work.Y + work.Height - height);
        Position = new PixelPoint(Math.Max(work.X, x), Math.Max(work.Y, y));
    }

    private static PixelPoint? TryCursor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }


    protected override void OnClosed(EventArgs e)
    {
        _web.AdapterCreated -= OnAdapterCreated;
        base.OnClosed(e);
    }
}
