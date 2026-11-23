using System;
using Avalonia;
using Avalonia.Threading;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// The menu overlay as the shell uses it: one window built once, shown near the pointer and hidden
/// again when a macro runs or the page asks to close.
/// </summary>
internal sealed class MenuOverlayHost(BridgeServer bridge, IDeviceLayer devices, AppSettings settings) : IMenuOverlay
{
    private WebOverlayWindow? _window;
    private long _foregroundBeforeSearch;
    private bool _searchFocused;
    private bool _pinned;
    private DispatcherTimer? _outsideWatch;
    private bool _wasDown;

    /// <summary>
    /// The menu closes when another program comes forward, the way the native one does. Watched
    /// from the device layer rather than from window activation: an overlay is shown without
    /// taking the focus, so activation is not what "the person moved on" looks like.
    /// </summary>
    private void OnForegroundChanged()
    {
        if (AutoCloseSuspended || _window is not { IsVisible: true } window)
        {
            return;
        }

        long own = 0;
        try
        {
            own = window.TryGetPlatformHandle()?.Handle.ToInt64() ?? 0;
        }
        catch
        {
            // A window without a platform handle is judged by the foreground alone.
        }

        long foreground = 0;
        try
        {
            foreground = devices.Windows.Foreground()?.Handle ?? 0;
        }
        catch
        {
            return;
        }

        if (MenuOverlayPolicy.ShouldClose(true, window.Pinned, own, foreground))
        {
            Hide();
        }
    }

    private bool _autoCloseSuspended;

    public bool AutoCloseSuspended
    {
        get => _autoCloseSuspended;
        set
        {
            _autoCloseSuspended = value;
            if (_window is { } window)
            {
                // A native file dialog is not topmost; stepping the menu down lets it appear above.
                window.Topmost = !value;
            }
        }
    }

    public event Action? Hidden;

    public event Action<bool>? FocusChanged;

    public bool IsVisible => _window?.IsVisible == true;

    public bool Pinned
    {
        get => _window?.Pinned ?? _pinned;
        set
        {
            _pinned = value;
            if (_window is { } window)
            {
                window.Pinned = value;
            }

            settings.StoreMenuPinned(settings.LoadRememberMenuPin() && value);
            PushPinned();
        }
    }

    /// <summary>Starts watching the foreground once the shell has built the host.</summary>
    internal void Attach()
    {
        devices.WindowEvents.ForegroundChanged += OnForegroundChanged;
        // The menu host owns its outside-click watch: it must work for every way the menu opens,
        // not only while the trigger host happens to be listening.
        try
        {
            devices.Hook.MouseDown += OnMouseDown;
        }
        catch
        {
            // A machine whose hooks refuse to mount still gets the foreground-change fallback.
        }
    }

    public void Warm()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Menu, "overlay/menu", 350, 667);
        _window.SetNoActivate(true);
        _window.Topmost = !AutoCloseSuspended;
        _window.Warm();
    }

    public void Show()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Menu, "overlay/menu", 350, 667);
        _pinned = settings.LoadRememberMenuPin() && settings.LoadMenuPinned();
        _window.Pinned = _pinned;
        _window.EndWarm();
        _window.SetNoActivate(true);
        _window.Topmost = !AutoCloseSuspended;
        _window.PlaceNearCursor(350, 667);
        _window.Show();
        PushPinned();
        StartOutsideWatch();
        // The menu keeps the game's keyboard until its search button is pressed; only then does
        // the page get the focus, and closing the search gives it back.
    }

    public void BeginDrag() => _window?.BeginWindowDrag();

    public void MouseDown(OverlayPoint point)
    {
        if (AutoCloseSuspended || _window is not { IsVisible: true } window || window.Pinned)
        {
            return;
        }

        if (!window.ContainsScreen(point.X, point.Y))
        {
            Hide();
        }
    }

    private void OnMouseDown(MouseHookEvent e)
    {
        // The hook runs on its own thread; the window's state and Hide() belong to the UI thread.
        var point = new OverlayPoint((int)Math.Round((double)e.X), (int)Math.Round((double)e.Y));
        Dispatcher.UIThread.Post(() => MouseDown(point));
    }

    /// <summary>
    /// A second, event-free way to notice a press outside: hooks can be refused by the machine or
    /// not mounted before the menu opens, so while the window is visible a light poll reads the
    /// button and cursor directly. It only acts on a fresh press, never on the one that opened it.
    /// </summary>
    private void StartOutsideWatch()
    {
        _wasDown = AnyButtonDown();
        _outsideWatch ??= new DispatcherTimer(TimeSpan.FromMilliseconds(30),
            DispatcherPriority.Input, (_, _) => WatchOutside());
        _outsideWatch.Start();
    }

    private void StopOutsideWatch() => _outsideWatch?.Stop();

    private void WatchOutside()
    {
        if (_window is not { IsVisible: true } window || AutoCloseSuspended || window.Pinned)
        {
            _wasDown = AnyButtonDown();
            return;
        }

        var down = AnyButtonDown();
        var fresh = down && !_wasDown;
        _wasDown = down;
        if (fresh && GetCursorPos(out var point))
        {
            MouseDown(new OverlayPoint(point.X, point.Y));
        }
    }

    private static bool AnyButtonDown()
        => (GetAsyncKeyState(0x01) & 0x8000) != 0
            || (GetAsyncKeyState(0x02) & 0x8000) != 0
            || (GetAsyncKeyState(0x04) & 0x8000) != 0
            || (GetAsyncKeyState(0x05) & 0x8000) != 0
            || (GetAsyncKeyState(0x06) & 0x8000) != 0;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out ScreenPoint point);

    private void PushPinned()
        => _ = bridge.PushAsync("surface.menu.pinned", new { pinned = Pinned },
            BridgePushMode.Latest, BridgeSurface.Menu);

    public void Hide()
    {
        if (_window is not { IsVisible: true } window)
        {
            return;
        }

        if (_searchFocused)
        {
            EndSearch();
        }

        settings.StoreMenuPinned(settings.LoadRememberMenuPin() && _pinned);
        StopOutsideWatch();
        window.Hide();
        // A pin belongs to the opening unless the setting remembers it; the next Show restores the
        // remembered value.
        _pinned = false;
        window.Pinned = false;
        PushPinned();
        // The page is reused for the next opening: tell it to drop the search and the menus it
        // may still be holding, so a reopened menu starts the way the native one did.
        _ = bridge.PushAsync("surface.menu.closed", null, BridgePushMode.Latest, BridgeSurface.Menu);
        Hidden?.Invoke();
    }

    public void SetSearchFocused(bool focused)
    {
        if (focused == _searchFocused)
        {
            return;
        }

        if (focused)
        {
            _foregroundBeforeSearch = ForegroundHandle();
            _searchFocused = true;
            if (_window is { } window)
            {
                window.SetNoActivate(false);
                window.Activate();
                window.FocusPage();
            }
        }
        else
        {
            EndSearch();
        }

        FocusChanged?.Invoke(focused);
    }

    private void EndSearch()
    {
        _searchFocused = false;
        _window?.SetNoActivate(true);
        if (_foregroundBeforeSearch != 0)
        {
            SetForegroundWindow(new IntPtr(_foregroundBeforeSearch));
            _foregroundBeforeSearch = 0;
        }
    }

    private long ForegroundHandle()
    {
        try
        {
            return devices.Windows.Foreground()?.Handle ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}