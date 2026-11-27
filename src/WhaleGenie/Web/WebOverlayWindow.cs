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
internal sealed partial class WebOverlayWindow : Window
{
    private readonly NativeWebView _web = new();
    private readonly BridgeServer _bridge;
    private readonly BridgeSurface _surface;
    private readonly string _route;
    private readonly string? _extraQuery;
    private WebBridge? _channel;

    /// <summary>The native control exists; a page handed over before that has nowhere to go.</summary>
    private bool _adapterReady;

    /// <summary>
    /// False until the host shows the window for a person. The control refuses a page while the
    /// window is not on screen (<c>E_ABORT</c>), so navigating is only ever asked for from
    /// <see cref="ShowOnScreen"/>.
    /// </summary>
    private bool _navigateAllowed;

    /// <summary>True once the page was handed to the control.</summary>
    private bool _navigated;

    /// <summary>True while a navigation is being handed over, so a retry cannot overlap it.</summary>
    private bool _navigating;

    /// <summary>True once the page finished a navigation.</summary>
    private bool _pageLoaded;

    /// <summary>Times the control refused the page, so a retry can stop rather than spin.</summary>
    private int _navigateAttempts;

    /// <summary>True while a refused navigation waits for its retry.</summary>
    private bool _retryPending;

    /// <summary>True once the window and its control were built off screen.</summary>
    private bool _warmed;

    /// <summary>True while the window is shown only to get it built.</summary>
    private bool _warming;

    /// <summary>The warm-up window no longer owns the page; a real open decides when it loads.</summary>
    internal void EndWarm() => _warming = false;

    /// <summary>
    /// Builds the adapter and window while nobody is looking, so opening the overlay does not pay
    /// for creating them. The page itself is loaded only once the window is on screen for a person,
    /// which is <see cref="ShowOnScreen"/>'s job.
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
        _warmed = true;
    }

    /// <summary>True once the page has finished a navigation, so a later open only has to show it.</summary>
    internal bool PageLoaded => _pageLoaded;

    /// <summary>
    /// Shows the window where its host has just placed it and hands it the page. The order is the
    /// whole point: the control refuses a page while the window is not on screen (<c>E_ABORT</c>), so
    /// showing first and navigating afterwards is what makes the overlay appear at all.
    /// </summary>
    internal void ShowOnScreen()
    {
        _navigateAllowed = true;
        _navigateAttempts = 0;
        Show();
        TryNavigate();
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
        // The same browser environment as the shell: without it this control asks for a default
        // folder, the engine falls back to legacy WebView1 when that folder cannot be made, and
        // every navigation on that engine ends in E_ABORT — a blank overlay that never recovers.
        _web.EnvironmentRequested += (_, args) => WebViewEnvironment.Configure(args);
        _web.AdapterCreated += OnAdapterCreated;
        _web.NavigationCompleted += OnNavigationCompleted;
        Opened += (_, _) =>
        {
            ApplyStyles();
            TryNavigate();
        };

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
        _adapterReady = true;
        _web.Background = null;
        _web.Background = Brushes.Transparent;
        ApplyStyles();
        _bridge.Attach(_surface, channel);
        TryNavigate();
    }

    /// <summary>Hands the page to the control once it has an adapter and a window that is shown.</summary>
    private void TryNavigate()
    {
        if (!_adapterReady || !_navigateAllowed || _navigated || _navigating)
        {
            return;
        }

        _navigating = true;
        try
        {
            var url = StartUrl();
            WhaleGenie.Runtime.AppDiagnostics.Note("overlay.url", url.AbsoluteUri);
            _web.Navigate(url);
            _navigated = true;
        }
        catch (COMException error) when (error.HResult == unchecked((int)0x80004004))
        {
            // The control was not ready for a page yet. A window that is still coming up settles
            // within a frame or two, so the attempt is repeated a couple of times and then dropped
            // rather than left spinning; the next real open asks again.
            WhaleGenie.Runtime.AppDiagnostics.Record("overlay.navigate", error);
            if (_warming)
            {
                _warming = false;
                Hide();
            }

            ScheduleRetry();
        }
        finally
        {
            _navigating = false;
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        _pageLoaded = args.IsSuccess;
        WhaleGenie.Runtime.AppDiagnostics.Note("overlay.completed",
            args.IsSuccess ? "success" : "failed");
    }

    private void ScheduleRetry()
    {
        if (_retryPending || _navigateAttempts >= MaximumNavigateAttempts)
        {
            return;
        }

        _navigateAttempts++;
        _retryPending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            _retryPending = false;
            TryNavigate();
        }, TimeSpan.FromMilliseconds(NavigateRetryDelayMs));
    }

    private const int MaximumNavigateAttempts = 3;
    private const int NavigateRetryDelayMs = 150;

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
        // The screen the pointer is on decides where "near the pointer" may end: a window that is
        // still at its warm-up spot off every monitor would otherwise always land on the primary.
        var cursor = TryCursor();
        var screen = (cursor is { } at ? Screens.ScreenFromPoint(at) : null)
            ?? Screens.ScreenFromPoint(Position)
            ?? Screens.Primary;
        if (screen is not { } area)
        {
            return;
        }

        var work = area.WorkingArea;
        var pointer = cursor ?? new PixelPoint(work.X + work.Width / 2, work.Y + work.Height / 2);
        var scale = RenderScaling;
        var x = Math.Min(pointer.X, work.X + work.Width - (int)Math.Round(width * scale));
        var y = Math.Min(pointer.Y, work.Y + work.Height - (int)Math.Round(height * scale));
        Position = new PixelPoint(Math.Max(work.X, x), Math.Max(work.Y, y));
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

    protected override void OnClosed(EventArgs e)
    {
        _web.AdapterCreated -= OnAdapterCreated;
        _web.NavigationCompleted -= OnNavigationCompleted;
        base.OnClosed(e);
    }
}
