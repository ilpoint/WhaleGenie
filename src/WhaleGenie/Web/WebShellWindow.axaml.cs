using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using WhaleGenie.Bridge;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WindowStateSnapshot = WhaleGenie.Contracts.WindowState;

namespace WhaleGenie.Web;

/// <summary>
/// The web interface's window: one page holding the title bar, the navigation and every page the
/// shell has. The window itself keeps only the frame — resize borders and the window state —
/// while everything inside is the page's.
/// </summary>
/// <remarks>
/// The tray, the single-instance call-back and the exit walk all treat this like any other
/// window: closing it hides it, because the notification icon is how the program really stops.
/// </remarks>
public partial class WebShellWindow : Window, ITriggerFocusProbe
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private readonly BridgeServer? _bridge;
    private readonly ShellHostFactory? _hosts;
    private readonly ShellWindowRegistry? _registry;
    private readonly IWorkbenchHost? _workbench;
    private readonly BridgeSurface _surface;
    private readonly bool _workbenchWindow;

    private ShellHost? _host;
    private AppTray? _pendingTray;

    /// <summary>Whether a text field in the page last held the keyboard.</summary>
    private int _textInputFocused;

    private WebBridge? _channel;

    /// <summary>The one bridge server this window's page and its overlays speak through.</summary>
    internal BridgeServer? Bridge => _bridge;

    /// <summary>Which shell page this window is: Main or the separate Workbench.</summary>
    internal BridgeSurface Surface => _surface;

    /// <summary>Whether a text field in this window's page last held the keyboard.</summary>
    internal bool TextInputFocused => Volatile.Read(ref _textInputFocused) == 1;

    /// <summary>The Vite dev server to load instead of the packed page. Debug only.</summary>
    internal string? DevUrl { get; set; }

    /// <summary>
    /// This exists because a XAML file reached without a public parameterless constructor is
    /// reported as unreachable through the runtime loader (AVLN3001). The shell is only ever
    /// built by the container, which hands in the one bridge server; this constructor is never
    /// called by the program.
    /// </summary>
    public WebShellWindow()
    {
        InitializeComponent();
    }

    internal WebShellWindow(BridgeServer bridge, ShellHostFactory? hosts = null,
        ShellWindowRegistry? registry = null, IWorkbenchHost? workbench = null,
        BridgeSurface surface = BridgeSurface.Main, bool workbenchWindow = false)
        : this()
    {
        _bridge = bridge;
        _hosts = hosts;
        _registry = registry;
        _workbench = workbench;
        _surface = surface;
        _workbenchWindow = workbenchWindow;

        WebView.EnvironmentRequested += OnEnvironmentRequested;
        WebView.NavigationStarted += OnNavigationStarted;
        WebView.NavigationCompleted += OnNavigationCompleted;
        WebView.NewWindowRequested += OnNewWindowRequested;
        WebView.AdapterCreated += OnAdapterCreated;

        _channel = new WebBridge(WebView);
        _channel.Listen();
        _bridge!.Attach(_surface, _channel);

        // The page learns the window's first state before it draws its buttons; later changes
        // arrive through the same event, on the surface this window owns.
        _ = _bridge.PushAsync("window.changed", WindowStatePayload(), BridgePushMode.Latest, _surface);

        PropertyChanged += OnWindowPropertyChanged;
        Strings.Current.LanguageChanged += SetTitle;
        SetTitle();
    }

    /// <summary>
    /// Whether the page currently has a text field focused. The window adds its own active state:
    /// a field that kept the flag while the window lost the keyboard is not what the hotkeys mean.
    /// </summary>
    bool ITriggerFocusProbe.IsTextInputFocused => IsActive && Volatile.Read(ref _textInputFocused) == 1;

    /// <summary>
    /// Opens a macro in the editor. On the main window this hands the request to the workbench;
    /// on the workbench it asks its own page to load the document.
    /// </summary>
    internal void OpenEditor(string name)
    {
        if (_workbenchWindow)
        {
            _ = _bridge?.PushAsync("editor.openRequested", new { name },
                BridgePushMode.Latest, _surface);
            return;
        }

        _workbench?.OpenEditor(name);
    }

    /// <summary>Opens the named macro in the run/debug page, through the workbench.</summary>
    internal void OpenRun(string name)
    {
        if (_workbenchWindow)
        {
            _ = _bridge?.PushAsync("run.openRequested", new { name },
                BridgePushMode.Latest, _surface);
            return;
        }

        _workbench?.OpenRun(name);
    }

    /// <summary>Asks the page to show its settings page, from a native menu.</summary>
    internal void OpenSettings()
    {
        // 菜单是置顶且不激活的；打开设置必须先把自己带到前台，否则页面在后台换了路由，人还留在游戏里。
        Show();
        Activate();
        _ = _bridge?.PushAsync("settings.openRequested", new { });
    }

    /// <summary>The page reports focus changes through window.focus.</summary>
    internal void SetTextInputFocused(bool focused)
        => Volatile.Write(ref _textInputFocused, focused ? 1 : 0);

    /// <summary>
    /// Starts the native title-bar drag. The page sees only that a drag began: the capture and
    /// the move message hand the rest to Windows, which keeps dragging outside the window and
    /// snaps it the way any caption does.
    /// </summary>
    public void BeginNativeDrag()
    {
        var handle = TryGetPlatformHandle();
        if (handle?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        _ = SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    /// <summary>
    /// The shell creates the process's browser environment, so the folder and options it asks for are
    /// the ones every overlay reuses: <see cref="WebViewEnvironment.Configure"/> is the one answer.
    /// </summary>
    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
        => WebViewEnvironment.Configure(args);

    /// <summary>
    /// Only the packed page and the debug dev server may load here. A page that could navigate
    /// anywhere would turn the interface into a browser; a link a page wants to offer is handed
    /// to the system through <c>app.openExternal</c> instead.
    /// </summary>
    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        var allowed = args.Request is { } requested && IsAllowedSource(requested);
        AppDiagnostics.Note("webshell.navigate", $"{(allowed ? "allow" : "cancel")} {args.Request}");
        if (!allowed)
        {
            args.Cancel = true;
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
        => AppDiagnostics.Note("webshell.completed", args.IsSuccess ? "success" : "failed");

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args)
        => args.Handled = true;

    /// <summary>The notification icon; a plain close only puts the shell away while this is set.</summary>
    internal AppTray? Tray
    {
        get => _host?.Tray ?? _pendingTray;
        set
        {
            _pendingTray = value;
            if (_host is not null)
            {
                _host.Tray = value;
            }
        }
    }

    /// <summary>Offers work a stopped run left behind; once per run, after the host is up.</summary>
    internal Task OfferRecoveryAsync() => _host?.OfferRecoveryAsync() ?? Task.CompletedTask;

    /// <summary>The native windows the page can still ask for; null until the host is up.</summary>
    internal ShellNativeWindows? Actions => _host?.Actions;

    /// <summary>The project session the shell owns; null until the window has opened.</summary>
    internal ProjectSession? Session => _host?.Session;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowPlacement.Restore(this, AppSettings.Shared,
            _workbenchWindow ? WindowProfile.Workbench : WindowProfile.Main);
        _registry?.Register(this);

        _host ??= _hosts?.Create(this);

        if (_host is not null)
        {
            _host.Tray = _pendingTray;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        WindowPlacement.Remember(this, AppSettings.Shared,
            _workbenchWindow ? WindowProfile.Workbench : WindowProfile.Main);
        _host?.OnClosing(e);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _registry?.Unregister(this);
        _host?.Dispose();
        _host = null;
        base.OnClosed(e);
    }

    private bool IsAllowedSource(Uri uri)
    {
        Uri? dev = null;
        if (DevUrl is { Length: > 0 } text && Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            dev = parsed;
        }

        return WebShellNavigation.IsAllowed(uri, dev, Path.Combine(AppContext.BaseDirectory, "webui"));
    }

    private void OnAdapterCreated(object? sender, EventArgs args)
    {
        AppDiagnostics.Note("webshell.adapter", "created");
        Volatile.Write(ref _textInputFocused, 0);
        if (_bridge is not { } bridge || _channel is not { } channel)
        {
            return;
        }

        // A fresh page has not registered its receiver yet, so attaching again puts the server
        // back into buffering mode until the page is ready once more.
        bridge.Attach(_surface, channel);
        _ = bridge.PushAsync("window.changed", WindowStatePayload(), BridgePushMode.Latest, _surface);
        WebView.Navigate(StartUrl());
    }

    private Uri StartUrl()
    {
        var local = new Uri(Path.Combine(AppContext.BaseDirectory, "webui", "index.html"));
        var page = DevUrl is { } dev ? new Uri(dev) : local;
        if (!_workbenchWindow)
        {
            return page;
        }

        var builder = new UriBuilder(page);
        var query = builder.Query.TrimStart('?');
        builder.Query = query.Length == 0 ? "window=workbench" : query + "&window=workbench";
        return builder.Uri;
    }

    /// <summary>
    /// The page mirrors the window buttons it drew, so a maximize or minimize that did not come
    /// from the page — a snap, a taskbar action — is told back to it the same way.
    /// </summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_bridge is not { } bridge)
        {
            return;
        }

        if (args.Property == Window.WindowStateProperty || args.Property == Window.TopmostProperty)
        {
            _ = bridge.PushAsync("window.changed", WindowStatePayload(),
                BridgePushMode.Latest, _surface);
        }

        if (args.Property == Window.IsActiveProperty && !IsActive)
        {
            Volatile.Write(ref _textInputFocused, 0);
        }
    }

    private WindowStateSnapshot WindowStatePayload() => new(
        WindowState switch
        {
            WindowState.Maximized => "maximized",
            WindowState.Minimized => "minimized",
            _ => "normal",
        },
        Topmost);

    private void SetTitle()
    {
        var name = Strings.Get(_workbenchWindow ? "Shell.WorkbenchTitle" : "Main.Title");
        Title = name + (ProcessRights.IsElevated ? Strings.Get("Main.AdminBadge") : string.Empty);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
