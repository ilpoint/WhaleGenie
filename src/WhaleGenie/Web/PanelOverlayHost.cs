using WhaleGenie.Bridge;

namespace WhaleGenie.Web;

/// <summary>One interface panel window, built once and shown beside its group.</summary>
internal sealed class PanelOverlayHost(BridgeServer bridge)
{
    private WebOverlayWindow? _window;

    internal void Warm()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Panel, "overlay/panel", 240, 320);
        _window.Warm();
    }

    internal void Show()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Panel, "overlay/panel", 240, 320);
        _window.EndWarm();
        _window.PlaceNearCursor();
        _window.Show();
        _window.FocusPage();
    }

    internal void Close() => _window?.Hide();
}
