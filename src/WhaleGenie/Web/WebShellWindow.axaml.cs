using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using WhaleGenie.Bridge;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

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

    public WebShellWindow(BridgeServer bridge)
        : this()
    {
        _bridge = bridge;

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

    private bool IsAllowedSource(Uri uri)
    {
        if (uri.Scheme == "about")
        {
            return true;
        }

        if (DevUrl is { } dev && uri.AbsoluteUri.StartsWith(dev, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.IsFile)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "webui");
            return uri.LocalPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        return false;
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

    private object WindowStatePayload() => new
    {
        state = WindowState switch
        {
            WindowState.Maximized => "maximized",
            WindowState.Minimized => "minimized",
            _ => "normal",
        },
        topmost = Topmost,
    };

    private void SetTitle()
        => Title = Strings.Get("Main.Title") + (ProcessRights.IsElevated ? Strings.Get("Main.AdminBadge") : string.Empty);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
