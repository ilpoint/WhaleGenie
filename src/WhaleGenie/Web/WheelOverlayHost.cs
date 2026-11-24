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
/// The window is a full-screen transparent frame, so the guide line can follow the pointer past the
/// wheel square without being clipped. It stays hidden until the page has painted the opening frame.
/// It never takes focus and it is click-through; the global hook reads the pointer.
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
    private double _centerX = -1;
    private double _centerY = -1;
    private DispatcherTimer? _revealTimer;
    private bool _readyHooked;
    private bool _revealPending;
    private int _revealAttempts;

    public event Action? TimedOut;

    public bool IsVisible => _window?.IsVisible == true;

    public WheelMetrics Metrics { get; private set; } =
        WheelGeometry.For(WheelSurface.DefaultDiameter, false);

    public OverlayPoint Center => _origin;

    public void Warm()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Wheel, "overlay/wheel", 840, 840);
        _window.SetClickThrough(true);
        _window.SetNoActivate(true);
        if (!_readyHooked)
        {
            wheel.Ready += OnWheelReady;
            _readyHooked = true;
        }

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
        window.Hide();
        window.SetClickThrough(true);
        window.SetNoActivate(true);
        var centre = PlaceOnScreen(window, origin, options);
        _origin = new OverlayPoint(centre.X, centre.Y);
        if (options.MovePointerToCenter)
        {
            MovePointerTo(centre);
            _pointer = new OverlayPoint(centre.X, centre.Y);
        }

        // Prepare the opening frame while the window is hidden, and reveal only after the page
        // says the frame is painted. The WebView is a native child and does not honour top-level
        // opacity, so showing first would flash the previous wheel. Closing already cleared the
        // previous frame; this frame overwrites it before anything can become visible.
        BeginReveal();
        Push(null, opening: true);

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

    /// <summary>
    /// Keeps only the visible wheel inside the working area. The transparent square is twice the
    /// diameter and may hang over an edge; clamping the square would drag the wheel away from the
    /// press that opened it, which is why the native window clamped the centre instead.
    /// </summary>
    private PixelPoint PlaceOnScreen(WebOverlayWindow window, OverlayPoint origin,
        WheelOptions options)
    {
        var side = options.Diameter * 2;
        var scale = window.RenderScaling;
        var full = new PixelRect(0, 0, (int)Math.Ceiling(side * scale),
            (int)Math.Ceiling(side * scale));
        var work = full;
        try
        {
            var screen = window.Screens.ScreenFromPoint(new PixelPoint(origin.X, origin.Y))
                ?? window.Screens.Primary;
            if (screen is not null)
            {
                full = screen.Bounds;
                work = screen.WorkingArea;
                scale = screen.Scaling > 0 ? screen.Scaling : scale;
            }
        }
        catch
        {
            // A headless session has no screen list; the wheel still gets a centre to aim at.
        }

        var wheel = (int)Math.Ceiling(options.Diameter * scale);
        var centre = WheelPlacement.Center(origin.X, origin.Y, wheel,
            work.X, work.Y, work.Right, work.Bottom, options.ClampToScreen);
        var x = (int)Math.Round(centre.X);
        var y = (int)Math.Round(centre.Y);

        // A full-screen transparent frame: the guide line can leave the wheel's square and still
        // reach the pointer, instead of being clipped by a 2×diameter window.
        window.Position = new PixelPoint(full.X, full.Y);
        window.Width = Math.Max(1, full.Width / scale);
        window.Height = Math.Max(1, full.Height / scale);
        _centerX = (x - full.X) / scale;
        _centerY = (y - full.Y) / scale;
        return new PixelPoint(x, y);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out ScreenPoint point);

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
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
        if (!_revealPending)
        {
            PushAim();
        }
    }

    public void Close()
    {
        CancelReveal();
        _timeout?.Stop();
        _timeout = null;
        _layout = null;
        _aim = null;
        _pointer = null;
        _ = bridge.PushAsync("surface.wheel.closed", null,
            BridgePushMode.Latest, BridgeSurface.Wheel);
        _window?.Hide();
    }

    private void OnWheelReady()
    {
        if (_revealPending)
        {
            Reveal("ready");
        }
    }

    private void BeginReveal()
    {
        CancelReveal();
        _revealPending = true;
        _revealAttempts = 1;
        ArmRevealTimer();
    }

    private void ArmRevealTimer()
    {
        _revealTimer?.Stop();
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _revealTimer.Tick += (_, _) => RetryReveal();
        _revealTimer.Start();
    }

    /// <summary>
    /// Never reveal an unacknowledged frame. A page that is still warming up may lose the first
    /// opening push, so resend it while the window stays hidden; after a short budget give up rather
    /// than flash the previous wheel.
    /// </summary>
    private void RetryReveal()
    {
        if (!_revealPending)
        {
            return;
        }

        _revealTimer?.Stop();
        _revealTimer = null;
        if (_revealAttempts >= 20)
        {
            CancelReveal();
            _window?.Hide();
            TimedOut?.Invoke();
            return;
        }

        _revealAttempts++;
        Push(null, opening: true);
        ArmRevealTimer();
    }

    private void Reveal(string reason)
    {
        if (!_revealPending)
        {
            return;
        }

        _revealPending = false;
        _revealAttempts = 0;
        _revealTimer?.Stop();
        _revealTimer = null;
        AppDiagnostics.Note("wheel.reveal", reason + " " + _centerX + "," + _centerY);
        if (_window is { } window)
        {
            // The window was built at startup and has been hidden since. Showing it again puts it
            // back where it sat in the topmost band, which is below anything that became topmost
            // in the meantime — the game the wheel is opened over, a pinned shell, a panel. Ask for
            // the top of the band before and after the show; both are idempotent.
            window.EnsureTopmost();
            window.Show();
            window.EnsureTopmost();
        }
        _ = bridge.PushAsync("surface.wheel.opened",
            new { pop = _options.PopAnimation, ms = _options.PopAnimationMs },
            BridgePushMode.Latest, BridgeSurface.Wheel);
        if (_pointer is not null)
        {
            PushAim();
        }
    }

    private void CancelReveal()
    {
        _revealPending = false;
        _revealAttempts = 0;
        _revealTimer?.Stop();
        _revealTimer = null;
    }

    private void Push(SurfaceWheelAim? aim, bool opening = false)
    {
        if (_layout is not { } layout)
        {
            return;
        }

        var (dx, dy) = Offset();
        wheel.PushLayout(layout, _options, aim, dx, dy, _centerX, _centerY, opening);
    }

    private void PushAim()
    {
        var (dx, dy) = Offset();
        wheel.PushAim(_aim, dx, dy);
    }

    private void MovePointerTo(PixelPoint centre)
    {
        SetCursorPos(centre.X, centre.Y);
        if (GetCursorPos(out var now) && now.X == centre.X && now.Y == centre.Y)
        {
            AppDiagnostics.Note("wheel.recenter", "ok");
            return;
        }

        SetCursorPos(centre.X, centre.Y);
        AppDiagnostics.Note("wheel.recenter",
            GetCursorPos(out now) ? $"retry at {now.X},{now.Y}" : "cursor unreadable");
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
