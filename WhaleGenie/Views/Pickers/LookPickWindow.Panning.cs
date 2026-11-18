using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Moving the picture under the window with the pointer and the wheel.
public partial class LookPickWindow : AppWindow
{
    private void OnPanMoved(object? sender, PointerEventArgs e)
    {
        Aim(e);

        if (_dragging >= 0 && _board is not null)
        {
            DragTo(e.GetPosition(_board));
            e.Handled = true;
            return;
        }

        if (!_panning || this.FindControl<ScrollViewer>("Scroll") is not { } room)
        {
            return;
        }

        room.Offset = _panOrigin - (e.GetPosition(room) - _panFrom);
        e.Handled = true;
    }

    /// <summary>
    /// The button coming back up ends a drag, and a drag that did not go anywhere was a click: that
    /// is what picks the spot, so one gesture on a zoomed-in picture can both move it and pick on it.
    /// </summary>
    private void OnPanReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging >= 0)
        {
            _dragging = -1;
            e.Pointer.Capture(null);
            e.Handled = true;
            RebuildRows();
            return;
        }

        if (!_panning || _board is not { } board)
        {
            return;
        }

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        // A drag that left the picture where it was is a click on it, and a click is a pick: moving
        // the view and aiming at it are the same gesture, told apart by whether it went anywhere.
        if (this.FindControl<ScrollViewer>("Scroll") is { } room
            && (room.Offset - _panOrigin).Length < 2)
        {
            var place = e.GetPosition(board);
            Pick(LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y));
        }
    }

    /// <summary>
    /// Zooms about the pointer. The whole screen is a lot of picture for one window, so the wheel is
    /// how a particular button is brought up to a size it can be aimed at.
    /// </summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (this.FindControl<ScrollViewer>("Scroll") is not { } room || e.Delta.Y == 0)
        {
            return;
        }

        // Only over the picture: the list beside it scrolls with the same wheel, and turning the
        // picture into nothing but a zoom would take that away.
        if (!Inside(e.Source, room))
        {
            return;
        }

        e.Handled = true;
        var at = e.GetPosition(room);
        var was = _zoom;
        var next = Math.Clamp(LookGeometry.Step(_zoom, e.Delta.Y > 0),
            LookGeometry.SmallestZoom, LookGeometry.LargestZoom);
        if (Math.Abs(next - was) < 0.0001)
        {
            return;
        }

        var before = room.Offset + new Vector(at.X, at.Y);
        _zoom = next;
        Draw();
        room.UpdateLayout();

        var ratio = _zoom / was;
        room.Offset = new Vector(before.X * ratio, before.Y * ratio) - new Vector(at.X, at.Y);
    }
}
