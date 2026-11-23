using System;
using Avalonia;
using Avalonia.Threading;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// One wheel window, built once and shown at the pointer. The idle timeout lives here because it
/// is a window concern; what a timeout means is the gesture's.
/// </summary>
internal sealed class WheelOverlayHost(BridgeServer bridge, WheelSurfaceBridge wheel) : IWheelOverlay
{
    private WebOverlayWindow? _window;
    private DispatcherTimer? _timeout;
    private WheelRuntimeLayout? _layout;

    public event Action? TimedOut;

    public bool IsVisible => _window?.IsVisible == true;

    public WheelMetrics Metrics { get; private set; } = WheelGeometry.For(420, false);

    public void Open(WheelRuntimeLayout layout, OverlayPoint origin, int timeoutMs)
    {
        _layout = layout;
        Metrics = WheelSurface.MetricsOf(layout);
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Wheel, "overlay/wheel", 440, 440);
        _window.PlaceCenteredAt(new PixelPoint(origin.X, origin.Y), 420);
        _window.Show();
        _window.FocusPage();
        wheel.PushLayout(layout, null);

        _timeout?.Stop();
        if (timeoutMs > 0)
        {
            _timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timeoutMs) };
            _timeout.Tick += (_, _) =>
            {
                _timeout?.Stop();
                TimedOut?.Invoke();
            };
            _timeout.Start();
        }
    }

    public void SetLayout(WheelRuntimeLayout layout)
    {
        _layout = layout;
        Metrics = WheelSurface.MetricsOf(layout);
        wheel.PushLayout(layout, null);
    }

    public void SetAim(SurfaceWheelAim? aim)
    {
        if (_layout is { } layout)
        {
            wheel.PushLayout(layout, aim);
        }
    }

    public void Close()
    {
        _timeout?.Stop();
        _timeout = null;
        _layout = null;
        _window?.Hide();
    }
}