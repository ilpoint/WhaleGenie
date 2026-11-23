using System;
using Avalonia;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// One interface group's panel. Position and size come from C#; the page draws and reports a move
/// after the system move loop ends, so the offset can be stored against the group's rule.
/// </summary>
internal sealed class PanelOverlayHost(BridgeServer bridge, PanelSurfaceBridge frames, string groupKey)
    : IPanelOverlay
{
    private WebOverlayWindow? _window;
    private bool _placing;

    public event Action<OverlayPoint>? Moved;

    public bool IsVisible => _window?.IsVisible == true;

    public void Warm() => Ensure().Warm();

    public void Show(SurfacePanelSnapshot snapshot, OverlayPoint position, int width, int height,
        long aboveHandle)
    {
        var window = Ensure();
        window.EndWarm();
        _placing = true;
        window.Width = width;
        window.Height = height;
        window.Position = new PixelPoint(position.X, position.Y);
        window.Show();
        _placing = false;
        window.PlaceAbove(aboveHandle);
        frames.Push(snapshot);
    }

    public void Hide() => _window?.Hide();

    public void BeginDrag() => _window?.BeginWindowDrag();

    private WebOverlayWindow Ensure()
    {
        if (_window is { } ready)
        {
            return ready;
        }

        _window = new WebOverlayWindow(bridge, BridgeSurface.Panel, "overlay/panel", 86, 24,
            "overlay=panel&group=" + Uri.EscapeDataString(groupKey), topmost: false);
        _window.PositionChanged += (_, _) =>
        {
            if (!_placing && IsVisible)
            {
                Moved?.Invoke(new OverlayPoint(_window.Position.X, _window.Position.Y));
            }
        };
        return _window;
    }
}