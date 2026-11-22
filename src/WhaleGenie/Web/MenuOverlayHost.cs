using System;
using Avalonia;
using WhaleGenie.Bridge;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// The menu overlay as the shell uses it: one window built once, shown near the pointer and hidden
/// again when a macro runs or the page asks to close.
/// </summary>
internal sealed class MenuOverlayHost(BridgeServer bridge) : IMenuOverlay
{
    private WebOverlayWindow? _window;

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

    public void Show()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Menu, "overlay/menu", 356, 312);
        _window.PlaceNearCursor();
        _window.Show();
        _window.Activate();
    }

    public void Hide() => _window?.Hide();
}