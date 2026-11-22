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

    private ShellHost? _host;
    private AppTray? _pendingTray;

    /// <summary>Whether a text field in the page last held the keyboard.</summary>
    private int _textInputFocused;

    private WebBridge? _channel;

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

    internal WebShellWindow(BridgeServer bridge, ShellHostFactory? hosts = null)
        : this()
    {
        _bridge = bridge;
        _hosts = hosts;

        WebView.EnvironmentRequested += OnEnvironmentRequested;
        WebView.NavigationStarted += OnNavigationStarted;
        WebView.NavigationCompleted += OnNavigationCompleted;
        WebView.NewWindowRequested += OnNewWindowRequested;
        WebView.AdapterCreated += OnAdapterCreated;

        _channel = new WebBridge(WebView);
        _channel.Listen();
        _bridge!.Attach(_channel);

        // The page learns the window's first state before it draws its buttons; later changes
        // arrive through the same event.
        _ = _bridge.PushAsync("window.changed", WindowStatePayload());

        PropertyChanged += OnWindowPropertyChanged;
        Strings.Current.LanguageChanged += SetTitle;
        SetTitle();
    }

    /// <summary>
    /// Whether the page currently has a text field focused. The window adds its own active state:
    /// a field that kept the flag while the window lost the keyboard is not what the hotkeys mean.
    /// </summary>
    bool ITriggerFocusProbe.IsTextInputFocused => IsActive && Volatile.Read(ref _textInputFocused) == 1;

    /// <summary>Asks the page to open a macro in the web editor, from a native menu.</summary>
    internal void OpenEditor(string name)
        => _ = _bridge?.PushAsync("editor.openRequested", new { name });

    /// <summary>Asks the page to load a macro into the web run panel, from a native menu.</summary>
    internal void OpenRun(string name)
        => _ = _bridge?.PushAsync("run.openRequested", new { name });

    /// <summary>Asks the page to show its settings page, from a native menu.</summary>
    internal void OpenSettings()
        => _ = _bridge?.PushAsync("settings.openRequested", new { });

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

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
#if DEBUG
        args.EnableDevTools = true;
#endif
        if (args is WindowsWebView2EnvironmentRequestedEventArgs windows)
        {
            // The browser keeps its profiles and caches in its own folder under the user's
            // application data; the program folder is often not writable, and the browser must
            // not learn to try.
            windows.UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WhaleGenie", "webview");
            windows.Language = Strings.Current.Language == Language.Chinese ? "zh-CN" : "en-US";
#if DEBUG
            // The CDP port is what makes the real page automatable: Playwright connects over it
            // the same way it drives a normal browser, which is how the shell checks itself
            // without a person watching. Debug only — a release build exposes no such door.
            windows.AdditionalBrowserArguments = "--remote-debugging-port=9223";
#endif
        }
    }

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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowPlacement.Restore(this, AppSettings.Shared);

        _host ??= _hosts?.Create(this);
        if (_host is not null)
        {
            _host.Tray = _pendingTray;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        WindowPlacement.Remember(this, AppSettings.Shared);
        _host?.OnClosing(e);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
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
        bridge.Attach(channel);
        _ = bridge.PushAsync("window.changed", WindowStatePayload());
        WebView.Navigate(StartUrl());
    }

    private Uri StartUrl()
    {
        if (DevUrl is { } dev)
        {
            return new Uri(dev);
        }

        return new Uri(Path.Combine(AppContext.BaseDirectory, "webui", "index.html"));
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
            _ = bridge.PushAsync("window.changed", WindowStatePayload());
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
        => Title = Strings.Get("Main.Title") + (ProcessRights.IsElevated ? Strings.Get("Main.AdminBadge") : string.Empty);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
