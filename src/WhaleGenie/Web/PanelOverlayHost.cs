using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// One interface group's panel. Position and size come from C#; the page draws and reports a move
/// after the system move loop ends, so the offset can be stored against the group's rule.
/// </summary>
/// <remarks>
/// The window is built the first time its group has a target, because there is no telling which
/// groups a project has until a window answers to one of them. It sits directly above its target in
/// that target's own band: a program dragged over the target covers the panel with it, which is what
/// "in the same layer" means here.
/// A drag is allowed to leave the target while the pointer is down — the system owns that move loop
/// — but the place that is kept and drawn from is clamped back inside the target's client area when
/// the button comes up, so the group never comes to rest outside the window it belongs to.
/// </remarks>
internal sealed class PanelOverlayHost(
    BridgeServer bridge, PanelSurfaceBridge frames, string groupKey, IWindowDevice windows)
    : IPanelOverlay
{
    private WebOverlayWindow? _window;
    private DispatcherTimer? _moveWatch;
    private bool _placing;
    private bool _dragging;
    private PixelPoint _placedAt;
    private PixelPoint _dragStartedAt;
    private long _targetHandle;
    private int _width;
    private int _height;

    public event Action<OverlayPoint>? Moved;

    public bool IsVisible => _window?.IsVisible == true;

    public bool IsMoving => _dragging;

    public void Show(SurfacePanelSnapshot snapshot, OverlayPoint position, int width, int height,
        long aboveHandle)
    {
        var window = Ensure();
        window.EndWarm();
        _targetHandle = aboveHandle;
        _width = width;
        _height = height;
        _placing = true;
        try
        {
            window.SetNoActivate(true);
            window.Width = width;
            window.Height = height;
            _placedAt = new PixelPoint(position.X, position.Y);
            window.Position = _placedAt;
            // Shown before the page is handed over: a hidden control refuses it, and a panel whose
            // page never loaded is a blank strip beside the target window.
            window.ShowOnScreen();
            // Windows may refuse a body as short as the folded handle; the region is what makes the
            // strip exact, so a taller window cannot show a white band under it.
            window.ClipToBody(width, height, PanelGeometry.PanelCornerRadius);
        }
        finally
        {
            _placing = false;
        }

        // Placed after the frame is up: the window manager has the panel at its own size by now,
        // and the band the target sits in decides which side of it the panel lands on.
        window.PlaceAbove(aboveHandle);
        frames.Push(snapshot);
    }

    public void Hide()
    {
        _moveWatch?.Stop();
        _dragging = false;
        _window?.Hide();
    }

    public void Close()
    {
        var window = _window;
        _window = null;
        _moveWatch?.Stop();
        _moveWatch = null;
        _dragging = false;
        window?.Close();
    }

    public void BeginDrag()
    {
        if (_window is not { IsVisible: true } window)
        {
            return;
        }

        _dragging = true;
        _placedAt = window.Position;
        _dragStartedAt = _placedAt;
        _moveWatch ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background, (_, _) => CompleteMove());
        _moveWatch.Stop();
        _moveWatch.Start();
        window.BeginWindowDrag();
    }

    private WebOverlayWindow Ensure()
    {
        if (_window is { } ready)
        {
            return ready;
        }

        var window = new WebOverlayWindow(bridge, BridgeSurface.Panel, "overlay/panel", 86, 24,
            "overlay=panel&group=" + Uri.EscapeDataString(groupKey), topmost: false);
        window.SetNoActivate(true);
        window.PositionChanged += (_, _) => OnPositionChanged(window);
        _window = window;
        return window;
    }

    private void OnPositionChanged(WebOverlayWindow window)
    {
        if (_placing || !IsVisible)
        {
            return;
        }

        var at = window.Position;
        if (at == _placedAt)
        {
            // The placement we just asked for coming back; it is not the person moving the panel.
            return;
        }

        var from = _placedAt;
        _placedAt = at;
        if (_dragging)
        {
            // The system move loop is still going: let it own the place until the button is up.
            _moveWatch?.Stop();
            _moveWatch?.Start();
            return;
        }

        // A move we did not start (a DPI change, for instance) is still a place to keep.
        FinishMove(window, from);
    }

    /// <summary>
    /// Ends one move once the left button is up. The timer is what sees the end: the move loop does
    /// not report it, and a panel that reported every intermediate position would store the place
    /// dozens of times and fight the window manager for the last word.
    /// </summary>
    private void CompleteMove()
    {
        if (!_dragging)
        {
            _moveWatch?.Stop();
            return;
        }

        if (OperatingSystem.IsWindows() && (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0)
        {
            return;
        }

        _moveWatch?.Stop();
        _dragging = false;
        if (_window is { } window)
        {
            FinishMove(window, _dragStartedAt);
        }
    }

    /// <summary>Clamps the window back into its target and reports the place that was kept.</summary>
    private void FinishMove(WebOverlayWindow window, PixelPoint from)
    {
        var at = window.Position;
        var kept = Clamp(at);
        if (kept != at)
        {
            _placing = true;
            try
            {
                _placedAt = kept;
                window.Position = kept;
            }
            finally
            {
                _placing = false;
            }
        }
        else
        {
            _placedAt = kept;
        }

        // A panel dragged by hand is still in its target's layer: put it back above the target
        // before the move is remembered.
        window.PlaceAbove(_targetHandle);
        if (kept != from)
        {
            Moved?.Invoke(new OverlayPoint(kept.X, kept.Y));
        }
    }

    /// <summary>
    /// The moved window's place inside the target's client area. A target that went away has no
    /// client area left to clamp against, and the place it had is the only one there is.
    /// </summary>
    private PixelPoint Clamp(PixelPoint at)
    {
        if (_targetHandle == 0 || _width <= 0 || _height <= 0)
        {
            return at;
        }

        try
        {
            var origin = windows.ClientOrigin(_targetHandle);
            var client = windows.ClientSize(_targetHandle);
            var (x, y) = PanelGeometry.Place((origin.X, origin.Y),
                (client.Width, client.Height), at.X - origin.X, at.Y - origin.Y,
                (_width, _height));
            return new PixelPoint(x, y);
        }
        catch
        {
            return at;
        }
    }

    private const int VkLeftButton = 0x01;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
