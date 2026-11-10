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

// Drawing the frame, the arrow, the window preview and the shaded rest of the desk.
public partial class RegionPickerWindow : Window
{
    /// <summary>Redraws the rectangle, its handles and the readout beside it.</summary>
    private void Update()
    {
        if (_selection is null || _sizeText is null || _readout is null)
        {
            return;
        }

        var region = Rectangle(_start, _end);
        var big = Ready(region);

        _selection.IsVisible = big && !_arrow;
        foreach (var handle in _handles)
        {
            handle.IsVisible = big && !_arrow;
        }

        if (_bounds is { } bounds)
        {
            var allowed = OnWindow(bounds);
            _allowedArea!.IsVisible = true;
            Canvas.SetLeft(_allowedArea, allowed.Left);
            Canvas.SetTop(_allowedArea, allowed.Top);
            _allowedArea.Width = allowed.Width;
            _allowedArea.Height = allowed.Height;
            PushTargetBack(allowed);
        }
        else
        {
            if (_allowedArea is not null)
            {
                _allowedArea.IsVisible = false;
            }

            foreach (var band in _targetDim)
            {
                Lay(band, 0, 0, 0, 0);
            }
        }

        if (big && !_arrow)
        {
            var rect = OnWindow(region);
            Canvas.SetLeft(_selection, rect.X);
            Canvas.SetTop(_selection, rect.Y);
            _selection.Width = rect.Width;
            _selection.Height = rect.Height;

            for (var i = 0; i < _handles.Length; i++)
            {
                Canvas.SetLeft(_handles[i], rect.X + (rect.Width * HandlePlaces[i].X) - (HandleSize / 2));
                Canvas.SetTop(_handles[i], rect.Y + (rect.Height * HandlePlaces[i].Y) - (HandleSize / 2));
            }
        }

        if (_arrow)
        {
            DrawArrow(big);
            PushBack(null);
        }
        else
        {
            // The desk is pushed back once there is a rectangle to leave alone, and not before: with
            // nothing drawn the screen is the screen, which is what the user is looking for a place on.
            PushBack(Picked is { } drawn ? OnWindow(drawn) : null);
        }

        _sizeText.Text = big
            ? _arrow
                ? Strings.Format("Region.Arrow", _start.X, _start.Y, _end.X, _end.Y)
                : Strings.Format("Region.Size", region.X, region.Y, region.Width, region.Height)
            : Strings.Get("Region.Drag");

        // The readout follows the pointer rather than a corner of the rectangle: a rectangle being
        // dragged can put its corner under the text, and a hint nobody can find is no hint. While
        // the loupe is up it hangs under the loupe instead of beside the pointer, so the two never
        // cover each other — both follow the pointer, so they have to be stacked.
        var width = _readout.Bounds.Width > 0 ? _readout.Bounds.Width : 220;
        var height = _readout.Bounds.Height > 0 ? _readout.Bounds.Height : 60;
        var roomWidth = Width > 0 ? Width : Bounds.Width;
        var roomHeight = Height > 0 ? Height : Bounds.Height;

        double left;
        double top;
        if (_loupeAt is { } loupe)
        {
            left = loupe.X;
            top = loupe.Bottom + 8;
        }
        else
        {
            var at = _pointer ?? _end;
            left = ((at.X - _origin.X) / _scaling) + 18;
            top = ((at.Y - _origin.Y) / _scaling) + 18;
        }

        Canvas.SetLeft(_readout, Math.Clamp(left, 0, Math.Max(0, roomWidth - width)));
        Canvas.SetTop(_readout, Math.Clamp(top, 0, Math.Max(0, roomHeight - height)));

        PreviewWindow(region, big);
    }

    /// <summary>
    /// Draws the start point and an arrowhead at the end, so the picker says which way the drag
    /// goes instead of showing an undirected box.
    /// </summary>
    private void DrawArrow(bool visible)
    {
        foreach (var line in new[] { _arrowShaft, _arrowLeft, _arrowRight })
        {
            line.IsVisible = visible;
        }

        _arrowStart.IsVisible = visible;
        if (!visible)
        {
            return;
        }

        var from = OnWindow(_start);
        var to = OnWindow(_end);
        _arrowShaft.StartPoint = from;
        _arrowShaft.EndPoint = to;

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var head = Math.Min(18, length * 0.35);
        var across = head * 0.5;
        var baseX = to.X - ((dx / length) * head);
        var baseY = to.Y - ((dy / length) * head);
        var sideX = -(dy / length) * across;
        var sideY = (dx / length) * across;

        _arrowLeft.StartPoint = to;
        _arrowLeft.EndPoint = new Point(baseX + sideX, baseY + sideY);
        _arrowRight.StartPoint = to;
        _arrowRight.EndPoint = new Point(baseX - sideX, baseY - sideY);

        Canvas.SetLeft(_arrowStart, from.X - 4);
        Canvas.SetTop(_arrowStart, from.Y - 4);
    }

    private Point OnWindow(PixelPoint point)
        => new((point.X - _origin.X) / _scaling, (point.Y - _origin.Y) / _scaling);

    /// <summary>
    /// The mover's picker changes the target window while its frame is dragged, so the frame is the
    /// window rather than a drawing of somewhere it could be. A window that will not move is left
    /// alone; the numbers can still be corrected by hand.
    /// </summary>
    private void PreviewWindow(Region region, bool visible)
    {
        if (_liveWindow == 0 || !visible || region == _liveAt)
        {
            return;
        }

        try
        {
            if (_windows.Move(_liveWindow, region.X, region.Y, region.Width, region.Height))
            {
                _liveAt = region;
            }
        }
        catch (Exception)
        {
            // A window that has gone, or one that refuses this size, is still worth framing.
        }
    }

    /// <summary>
    /// Pushes back everything outside <paramref name="hole"/>, which is the part of the desk being
    /// looked at, with a band on each of its four sides. No rectangle at all is nothing to look
    /// through, and then nothing is pushed back either.
    /// </summary>
    private void PushBack(Rect? hole)
    {
        if (_picture is null || _dim.Length < 4)
        {
            return;
        }

        if (hole is not { } rect)
        {
            foreach (var band in _dim)
            {
                Lay(band, 0, 0, 0, 0);
            }

            return;
        }

        var left = Math.Max(0, rect.Left);
        var top = Math.Max(0, rect.Top);
        var right = Math.Min(Width, Math.Max(left, rect.Right));
        var bottom = Math.Min(Height, Math.Max(top, rect.Bottom));

        Lay(_dim[0], 0, 0, Width, top);
        Lay(_dim[1], 0, bottom, Width, Height - bottom);
        Lay(_dim[2], 0, top, left, bottom - top);
        Lay(_dim[3], right, top, Width - right, bottom - top);
    }

    private void PushTargetBack(Rect allowed)
    {
        if (_targetDim.Length < 4)
        {
            return;
        }

        var left = Math.Max(0, allowed.Left);
        var top = Math.Max(0, allowed.Top);
        var right = Math.Min(Width, Math.Max(left, allowed.Right));
        var bottom = Math.Min(Height, Math.Max(top, allowed.Bottom));

        Lay(_targetDim[0], 0, 0, Width, top);
        Lay(_targetDim[1], 0, bottom, Width, Height - bottom);
        Lay(_targetDim[2], 0, top, left, bottom - top);
        Lay(_targetDim[3], right, top, Width - right, bottom - top);
    }

    /// <summary>Puts one band where it goes, measuring it to nothing rather than back to front.</summary>
    private static void Lay(Border band, double x, double y, double width, double height)
    {
        band.Width = Math.Max(0, width);
        band.Height = Math.Max(0, height);
        Canvas.SetLeft(band, x);
        Canvas.SetTop(band, y);
    }
}
