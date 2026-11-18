using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// The eight handles: which one a press grabs, moving the rectangle, and pulling an edge.
public partial class RegionPickerWindow : Window
{
    /// <summary>Which part of the rectangle a place on the window is on, if any.</summary>
    private Grip HitTest(Point point)
    {
        if (_arrow)
        {
            return Grip.None;
        }

        if (Picked is not { } region
            || region.Width < MinimumSize
            || region.Height < MinimumSize)
        {
            return Grip.None;
        }

        var rect = OnWindow(region);
        var west = point.X >= rect.Left - GrabOutside && point.X <= rect.Left + HandleSize;
        var east = point.X >= rect.Right - HandleSize && point.X <= rect.Right + GrabOutside;
        var north = point.Y >= rect.Top - GrabOutside && point.Y <= rect.Top + HandleSize;
        var south = point.Y >= rect.Bottom - HandleSize && point.Y <= rect.Bottom + GrabOutside;
        var acrossX = point.X >= rect.Left - GrabOutside && point.X <= rect.Right + GrabOutside;
        var acrossY = point.Y >= rect.Top - GrabOutside && point.Y <= rect.Bottom + GrabOutside;

        if (west && north)
        {
            return Grip.NorthWest;
        }

        if (east && north)
        {
            return Grip.NorthEast;
        }

        if (west && south)
        {
            return Grip.SouthWest;
        }

        if (east && south)
        {
            return Grip.SouthEast;
        }

        if (west && acrossY)
        {
            return Grip.West;
        }

        if (east && acrossY)
        {
            return Grip.East;
        }

        if (north && acrossX)
        {
            return Grip.North;
        }

        if (south && acrossX)
        {
            return Grip.South;
        }

        return rect.Contains(point) ? Grip.Move : Grip.None;
    }

    /// <summary>
    /// The pointer this draws with, or null to leave the ordinary one alone. Drawing a rectangle is
    /// one of the things the ordinary pointer is for; the system's own crosshair, which is what a
    /// picker like this usually reaches for, is a plain black one that disappears over a dark
    /// picture — and a pointer that cannot be found is worse than a pointer that says nothing.
    /// </summary>
    private static Cursor? CursorFor(Grip grip) => grip switch
    {
        Grip.Move => MoveCursor,
        Grip.West or Grip.East => WestEast,
        Grip.North or Grip.South => NorthSouth,
        Grip.NorthWest => NorthWestCursor,
        Grip.NorthEast => NorthEastCursor,
        Grip.SouthWest => SouthWestCursor,
        Grip.SouthEast => SouthEastCursor,
        _ => null,
    };

    /// <summary>Moves the whole rectangle so the place it was grabbed stays under the pointer.</summary>
    private void CarryTo(PixelPoint point)
    {
        var dx = point.X - _grab.X;
        var dy = point.Y - _grab.Y;
        if (_bounds is { } bounds)
        {
            var width = _right - _left;
            var height = _bottom - _top;
            var left = Math.Clamp(_left + dx, bounds.X,
                bounds.X + Math.Max(0, bounds.Width - width));
            var top = Math.Clamp(_top + dy, bounds.Y,
                bounds.Y + Math.Max(0, bounds.Height - height));
            _start = new PixelPoint(left, top);
            _end = new PixelPoint(left + width, top + height);
            return;
        }

        var moved = Rectangle(new PixelPoint(_left + dx, _top + dy),
            new PixelPoint(_right + dx, _bottom + dy));
        if (ClampRegion(moved) is { } kept)
        {
            _start = new PixelPoint(kept.X, kept.Y);
            _end = new PixelPoint(kept.X + kept.Width, kept.Y + kept.Height);
        }
    }

    /// <summary>
    /// Moves the edges the grabbed handle belongs to, leaving the opposite ones put. Dragged past
    /// the opposite edge the rectangle turns over rather than folding up into a line: what the hand
    /// is doing is drawing that part of the screen, and a line is not a rectangle anybody asked for.
    /// </summary>
    private void PullTo(PixelPoint point)
    {
        var left = _left;
        var top = _top;
        var right = _right;
        var bottom = _bottom;

        if (_grip is Grip.West or Grip.NorthWest or Grip.SouthWest)
        {
            left = point.X;
        }

        if (_grip is Grip.East or Grip.NorthEast or Grip.SouthEast)
        {
            right = point.X;
        }

        if (_grip is Grip.North or Grip.NorthWest or Grip.NorthEast)
        {
            top = point.Y;
        }

        if (_grip is Grip.South or Grip.SouthWest or Grip.SouthEast)
        {
            bottom = point.Y;
        }

        if (left > right)
        {
            (left, right) = (right, left);
        }

        if (top > bottom)
        {
            (top, bottom) = (bottom, top);
        }

        if (_bounds is { } bounds)
        {
            left = Math.Max(left, bounds.X);
            top = Math.Max(top, bounds.Y);
            right = Math.Min(right, bounds.X + bounds.Width);
            bottom = Math.Min(bottom, bounds.Y + bounds.Height);
        }

        var pulled = Rectangle(new PixelPoint(left, top), new PixelPoint(right, bottom));
        _start = new PixelPoint(pulled.X, pulled.Y);
        _end = new PixelPoint(pulled.X + pulled.Width, pulled.Y + pulled.Height);
    }

    /// <summary>
    /// A target window limits where a rectangle may be drawn. The frozen picture still shows the
    /// whole desk, but the selection itself is kept inside the client area in screen pixels.
    /// </summary>
    private PixelPoint PointIn(PixelPoint point)
    {
        if (_bounds is not { } bounds)
        {
            return point;
        }

        var right = bounds.X + bounds.Width;
        var bottom = bounds.Y + bounds.Height;
        return new PixelPoint(
            Math.Clamp(point.X, bounds.X, right),
            Math.Clamp(point.Y, bounds.Y, bottom));
    }

    private Region? ClampRegion(Region? region)
    {
        if (region is not { } value || _bounds is not { } bounds)
        {
            return region;
        }

        var left = Math.Max(bounds.X, value.X);
        var top = Math.Max(bounds.Y, value.Y);
        var right = Math.Min(bounds.X + bounds.Width, value.X + value.Width);
        var bottom = Math.Min(bounds.Y + bounds.Height, value.Y + value.Height);
        return right > left && bottom > top
            ? new Region(left, top, right - left, bottom - top)
            : null;
    }
}
