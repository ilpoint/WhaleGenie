using System;
using Avalonia;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// The menu overlay as the shell uses it: one window built once, shown near the pointer and hidden
/// again when a macro runs or the page asks to close.
/// </summary>
internal sealed class MenuOverlayHost(BridgeServer bridge, IDeviceLayer devices) : IMenuOverlay
{
    private WebOverlayWindow? _window;
    private long _foregroundBeforeSearch;
    private bool _searchFocused;

    /// <summary>
    /// The menu closes when another program comes forward, the way the native one does. Watched
    /// from the device layer rather than from window activation: an overlay is shown without
    /// taking the focus, so activation is not what "the person moved on" looks like.
    /// </summary>
    private void OnForegroundChanged()
    {
        if (_window is not { IsVisible: true } window)
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
            window.Hide();
        }
    }

    public event Action? Hidden;

    public event Action<bool>? FocusChanged;

    public bool IsVisible => _window?.IsVisible == true;

    public bool Pinned
    {
        get => _window?.Pinned == true;
        set
        {
            if (_window is { } window)
            {
                window.Pinned = value;
            }
        }
    }

    /// <summary>Starts watching the foreground once the shell has built the host.</summary>
    internal void Attach()
        => devices.WindowEvents.ForegroundChanged += OnForegroundChanged;

    public void Warm()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Menu, "overlay/menu", 350, 667);
        _window.SetNoActivate(true);
        _window.Warm();
    }

    public void Show()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Menu, "overlay/menu", 350, 667);
        _window.EndWarm();
        _window.SetNoActivate(true);
        _window.PlaceNearCursor(350, 667);
        _window.Show();
        // The menu keeps the game's keyboard until its search button is pressed; only then does
        // the page get the focus, and closing the search gives it back.
    }

    public void BeginDrag() => _window?.BeginWindowDrag();

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

        window.Hide();
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