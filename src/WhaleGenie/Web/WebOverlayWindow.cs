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
    private readonly string? _extraQuery;
    private WebBridge? _channel;
    private bool _noActivate;
    private bool _clickThrough;

    /// <summary>True once the page has been loaded in the background.</summary>
    private bool _warmed;

    /// <summary>True while the window is shown only to get the page loaded.</summary>
    private bool _warming;

    /// <summary>
    /// Starts a system caption drag. A page cannot move a window itself: pointer events stop at the
    /// window edge, so dragging a small panel across the screen needs the OS move loop.
    /// </summary>
    /// <summary>
    /// Puts the window directly above the target in the ordinary z-order: ask for the target's
    /// predecessor and go behind it, which lands just above the target.
    /// </summary>
    internal void PlaceAbove(long targetHandle)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || targetHandle == 0)
        {
            return;
        }

        var target = new IntPtr(targetHandle);
        var above = GetWindow(target, GwHwndPrev);
        SetWindowPos(handle, above, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    internal void BeginWindowDrag()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
    }

    /// <summary>The warm-up load no longer owns the window; a real open must not be hidden by it.</summary>
    internal void EndWarm() => _warming = false;

    /// <summary>
    /// Loads the page while nobody is looking: the window is placed off screen, shown, and hidden
    /// again as soon as the navigation finishes. A first open then costs a <c>Show()</c> (S0: 9 ms)
    /// instead of a WebView cold start (S0: about 0.6 s).
    /// </summary>
    internal void Warm()
    {
        if (_warmed || _channel is null)
        {
            return;
        }

        _warming = true;
        Position = new PixelPoint(-20000, -20000);
        Show();
    }

    /// <summary>Pinned overlays stay up when the user clicks another program.</summary>
    internal bool Pinned { get; set; }


    internal WebOverlayWindow(BridgeServer bridge, BridgeSurface surface, string route,
        double width, double height, string? extraQuery = null, bool topmost = true)
    {
        _bridge = bridge;
        _surface = surface;
        _route = route.TrimStart('/');
        _extraQuery = extraQuery;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = topmost;
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
        Opened += (_, _) => ApplyStyles();

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

        // The control applies Background to the controller; a value set before the adapter exists
        // can miss it, so it is written again here. Clearing first is what makes the property change.
        _web.Background = null;
        _web.Background = Brushes.Transparent;
        ApplyStyles();
        _bridge.Attach(_surface, channel);
        if (_warming)
        {
            _web.NavigationCompleted += OnWarmNavigationCompleted;
        }

        var url = StartUrl();
        WhaleGenie.Runtime.AppDiagnostics.Note("overlay.url", url.AbsoluteUri);
        _web.Navigate(url);
    }

    private Uri StartUrl()
    {
#if DEBUG
        if (App.WebUiDev is { Length: > 0 } dev)
        {
            var query = _extraQuery is { Length: > 0 } ? "?" + _extraQuery : "";
            return new Uri(dev.TrimEnd('/') + "/" + query + "#" + _route);
        }
#endif
        // Uri treats a relative "#..." against a file as a rooted path and escapes the hash, so the
        // fragment is set through the builder instead: that keeps the file path and yields the
        // hash the page's router reads.
        var page = new UriBuilder(new Uri(Path.Combine(AppContext.BaseDirectory, "webui", "index.html")))
        {
            // The stamp keeps WebView2 from answering a file navigation out of its cache: a page
            // built after the last launch would otherwise be served as the one before it.
            Query = "v=" + DateTime.Now.Ticks
                + (_extraQuery is { Length: > 0 } ? "&" + _extraQuery : ""),
            Fragment = _route,
        };
        return page.Uri;
    }

    /// <summary>
    /// Puts the window next to the pointer, like the native menu: a menu that opens in the middle
    /// of the screen makes the person look for it. A position that would leave the display is
    /// pulled back inside the work area.
    /// </summary>
    internal void PlaceNearCursor(double width, double height)
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is not { } area)
        {
            return;
        }

        var work = area.WorkingArea;
        var cursor = TryCursor() ?? new PixelPoint(work.X + work.Width / 2, work.Y + work.Height / 2);
        var scale = RenderScaling;
        var x = Math.Min(cursor.X, work.X + work.Width - (int)Math.Round(width * scale));
        var y = Math.Min(cursor.Y, work.Y + work.Height - (int)Math.Round(height * scale));
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

    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int GwHwndPrev = 3;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoActivate = 0x0010;
    private const int SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx,
        int cy, int flags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }


    private void OnWarmNavigationCompleted(object? sender, EventArgs args)
    {
        _web.NavigationCompleted -= OnWarmNavigationCompleted;
        _warmed = true;
        _warming = false;
        Hide();
    }

    /// <summary>Centres a square of <paramref name="contentSize"/> on a screen point.</summary>
    internal void PlaceCenteredAt(PixelPoint centre, double contentSize)
    {
        var scale = RenderScaling;
        var offset = (int)Math.Round((Width - contentSize) / 2 * scale);
        Position = new PixelPoint(centre.X - (int)Math.Round(contentSize / 2 * scale) - offset,
            centre.Y - (int)Math.Round(contentSize / 2 * scale) - offset);
    }

    /// <summary>Puts the keyboard on the page: the native WebView is its own window and swallows keys.</summary>
    internal void FocusPage() => _web.Focus();

    /// <summary>A menu or panel must not take the keyboard until its search is opened.</summary>
    internal void SetNoActivate(bool value)
    {
        _noActivate = value;
        ApplyStyles();
    }

    /// <summary>The wheel is driven by the global hook; the pointer belongs to whatever is behind it.</summary>
    internal void SetClickThrough(bool value)
    {
        _clickThrough = value;
        ApplyStyles();
    }

    /// <summary>Whether a screen point (physical pixels) lies inside this window.</summary>
    internal bool ContainsScreen(int x, int y)
    {
        var scale = RenderScaling;
        var position = Position;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        return x >= position.X && y >= position.Y
            && x < position.X + width && y < position.Y + height;
    }

    /// <summary>Clips the window after Windows refused a very short height, as the native panel did.</summary>
    internal void ClipToBody(int width, int height)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        var scale = RenderScaling;
        var region = CreateRectRgn(0, 0, (int)Math.Round(width * scale), (int)Math.Round(height * scale));
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void ApplyStyles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style = _noActivate
            ? style | WsExNoActivate | WsExToolWindow
            : style & ~(long)WsExNoActivate;
        style = _clickThrough ? style | WsExTransparent : style & ~(long)WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("user32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    protected override void OnClosed(EventArgs e)
    {
        _web.AdapterCreated -= OnAdapterCreated;
        base.OnClosed(e);
    }
}
