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

// Dragging on the desk, and the rectangle or drag that comes of it.
public partial class RegionPickerWindow : Window
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        var point = PointIn(ToScreen(position));

        _active = true;
        _grab = point;
        _pointer = point;

        var grip = HitTest(position);
        if (grip != Grip.None && Picked is { } current)
        {
            _grip = grip;
            _left = current.X;
            _top = current.Y;
            _right = current.X + current.Width;
            _bottom = current.Y + current.Height;
        }
        else
        {
            // Nowhere on the rectangle: this press draws a new one from here.
            _grip = Grip.None;
            _start = point;
            _end = point;
        }

        e.Pointer.Capture(this);
        Cursor = CursorFor(_grip);
        e.Handled = true;

        // 先摆放大镜，再画：读数气泡要挂在放大镜下面，得先知道它落在哪。
        Aim(position, true);
        Update();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);
        _pointer = PointIn(ToScreen(position));

        if (_active)
        {
            var point = _pointer.Value;

            switch (_grip)
            {
                case Grip.None:
                    _end = point;
                    break;

                case Grip.Move:
                    CarryTo(point);
                    break;

                default:
                    PullTo(point);
                    break;
            }
        }

        var pointing = _active ? _grip : HitTest(position);
        Cursor = CursorFor(pointing);
        Aim(position, _active);
        Update();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_active)
        {
            return;
        }

        var drawing = _grip == Grip.None;
        _active = false;
        _grip = Grip.None;
        e.Pointer.Capture(null);
        e.Handled = true;
        _pointer = PointIn(ToScreen(e.GetPosition(this)));

        if (drawing)
        {
            // A drag that never moved is a click: with nothing to keep, it just clears the rectangle.
            _end = PointIn(ToScreen(e.GetPosition(this)));
            if (Size(_start, _end) < MinimumSize)
            {
                _end = _start;
            }
        }

        var released = e.GetPosition(this);
        Cursor = CursorFor(HitTest(released));
        Aim(released, false);
        Update();
    }
}
