using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// The window itself: full screen, the pointer over the picture, and the zoom it asks for.
public partial class LookWindow : AppWindow
{

    // -------------------------------------------------------------------- commands

    /// <summary>
    /// Fills the screen, or leaves it: a reference picture and a screenful of hits are read at their
    /// own size. No button of its own — the title bar's maximize is the button, and F11 does it from
    /// the keyboard.
    /// </summary>
    private void ToggleFullScreen()
        => WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;

    /// <summary>Whether the event came from inside one part of the window.</summary>
    private static bool Inside(object? source, Visual part)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (ReferenceEquals(node, part))
            {
                return true;
            }
        }

        return false;
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, true), true);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, false), true);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Zoom(Fit(), false);

    /// <summary>
    /// A press on the picture starts a drag, or — on the second click — goes between its real size
    /// and the size that fits. The drag is taken here rather than left to the scroll viewer so that
    /// it works the same at every zoom, including the zoom where the picture fits and there is
    /// nothing to scroll yet.
    /// </summary>
    private void OnPicturePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            ToggleRealSize();
            return;
        }

        if (_board is null || _scroll is null
            || !e.GetCurrentPoint(_board).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _panning = true;
        _panFrom = e.GetPosition(_scroll);
        _panOrigin = _scroll.Offset;
        e.Pointer.Capture(_board);
        e.Handled = true;
    }

    private void OnPictureMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning || _scroll is null)
        {
            return;
        }

        // What the pointer moved by is what the picture moves by, the other way round: dragging to
        // the left brings the part that was off the right edge into view.
        _scroll.Offset = _panOrigin - (e.GetPosition(_scroll) - _panFrom);
        e.Handled = true;
    }

    private void OnPictureReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// The wheel zooms, and zooms about the pointer: whatever was under it stays under it, which is
    /// what makes a mark worth aiming at. Scrolling a picture that fills the window would otherwise
    /// move the one thing the person is pointing at.
    /// </summary>
    private void OnPictureWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_scroll is null || e.Delta.Y == 0)
        {
            return;
        }

        // Only over the picture: the mark list beside it scrolls with the same wheel.
        if (!Inside(e.Source, _scroll))
        {
            return;
        }

        e.Handled = true;
        ZoomAt(LookGeometry.Step(_zoom, e.Delta.Y > 0), e.GetPosition(_scroll));
    }

    /// <summary>
    /// Zooms while keeping one place on the picture where it is. The place is read before the
    /// picture is redrawn: after it, the same screen point is a different part of the picture.
    /// </summary>
    private void ZoomAt(double zoom, Point at)
    {
        var was = _zoom;
        if (_scroll is null || was <= 0)
        {
            Zoom(zoom, false);
            return;
        }

        var before = _scroll.Offset + new Vector(at.X, at.Y);
        Zoom(zoom, false);

        // The picture has just been given a new size, and the viewer answers a scroll position
        // against the size it knew before the layout that comes next: without this pass the offset
        // below is quietly cut down to the old range, and the place under the pointer drifts.
        _scroll.UpdateLayout();

        var ratio = _zoom / was;
        _scroll.Offset = new Vector(before.X * ratio, before.Y * ratio) - new Vector(at.X, at.Y);
    }

    /// <summary>
    /// Switches the picture between the size its pixels really are and the size that fits the room
    /// there is for it, which is what double-clicking the picture does.
    /// </summary>
    public void ToggleRealSize() => Zoom(Math.Abs(_zoom - 1) < 0.001 ? Fit() : 1, false);
}
