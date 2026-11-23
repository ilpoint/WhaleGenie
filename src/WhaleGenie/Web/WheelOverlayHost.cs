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
/// <remarks>
/// The window is a square twice the wheel's diameter, as the native one was: the guide line has to
/// follow the pointer outside the circles. It never takes focus and it is click-through; the global
/// hook is what reads the pointer.
/// </remarks>
internal sealed class WheelOverlayHost(BridgeServer bridge, WheelSurfaceBridge wheel) : IWheelOverlay
{
    private WebOverlayWindow? _window;
    private DispatcherTimer? _timeout;
    private WheelRuntimeLayout? _layout;
    private WheelOptions _options = new();
    private OverlayPoint _origin;
    private SurfaceWheelAim? _aim;
    private OverlayPoint? _pointer;

    public event Action? TimedOut;

    public bool IsVisible => _window?.IsVisible == true;

    public WheelMetrics Metrics { get; private set; } =
        WheelGeometry.For(WheelSurface.DefaultDiameter, false);

    public void Warm()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Wheel, "overlay/wheel", 840, 840);
        _window.SetClickThrough(true);
        _window.SetNoActivate(true);
        _window.Warm();
    }

    public void Open(WheelRuntimeLayout layout, WheelOptions options, OverlayPoint origin,
        int timeoutMs)
    {
        _layout = layout;
        _options = options;
        _origin = origin;
        _aim = null;
        _pointer = null;
        Metrics = WheelSurface.MetricsOf(layout, options);

        var window = Ensure();
        window.EndWarm();
        window.SetClickThrough(true);
        window.SetNoActivate(true);
        var side = options.Diameter * 2;
        window.Width = side;
        window.Height = side;
        window.PlaceCenteredAt(new PixelPoint(origin.X, origin.Y), options.Diameter);
        window.Show();
        Push(null);

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
        Metrics = WheelSurface.MetricsOf(layout, _options);
        Push(_aim);
    }

    public void SetAim(SurfaceWheelAim? aim, OverlayPoint pointer)
    {
        _aim = aim;
        _pointer = pointer;
        Push(aim);
    }

    public void Close()
    {
        _timeout?.Stop();
        _timeout = null;
        _layout = null;
        _aim = null;
        _pointer = null;
        _window?.Hide();
    }

    private void Push(SurfaceWheelAim? aim)
    {
        if (_layout is not { } layout)
        {
            return;
        }

        var (dx, dy) = Offset();
        wheel.PushLayout(layout, _options, aim, dx, dy);
    }

    /// <summary>The pointer from the wheel's centre in DIPs; the page adds it to the canvas centre.</summary>
    private (double X, double Y) Offset()
    {
        if (_pointer is not { } pointer)
        {
            return (0, 0);
        }

        var scale = _window?.RenderScaling ?? 1;
        return ((pointer.X - _origin.X) / scale, (pointer.Y - _origin.Y) / scale);
    }

    private WebOverlayWindow Ensure()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Wheel, "overlay/wheel", 840, 840);
        return _window;
    }
}
