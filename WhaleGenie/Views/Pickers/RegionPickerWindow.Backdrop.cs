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

// The picture of the desk behind the window, its bounds, and where a point on it lands on screen.
public partial class RegionPickerWindow : Window
{
    /// <summary>
    /// Takes the desk down as a picture and stands it behind everything. One picture of the whole
    /// desk rather than anything read again and again: the screen cannot be seen through a window,
    /// so this is what the user aims at, and holding it still is what lets a corner be put on a
    /// pixel while the thing underneath keeps moving.
    ///
    /// Taken before the window goes up, because a desk read after that answers with this picker's
    /// own dark surface; and only when the whole of it comes back, because a part of it stretched
    /// over the window points at places the numbers do not.
    /// </summary>
    private void TakeDesk()
    {
        if (_desk is null)
        {
            return;
        }

        var (origin, size) = DeskBounds();
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // The program's own windows went off the screen a moment ago, and the desk answers with the
        // last frame it drew — the one that still has them in it. The shot is taken repeatedly until
        // the desk has stopped changing, which is the first one without them.
        var frame = ScreenReady.Capture(_screen, origin.X, origin.Y, size.Width, size.Height);

        if (frame.IsEmpty || frame.Width != size.Width || frame.Height != size.Height)
        {
            return;
        }

        var picture = new WriteableBitmap(new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using (var buffer = picture.Lock())
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Marshal.Copy(frame.Bgra, row * frame.Width * 4,
                    IntPtr.Add(buffer.Address, row * buffer.RowBytes), frame.Width * 4);
            }
        }

        _picture?.Dispose();
        _picture = picture;
        _deskFrame = frame;
        _desk.Source = picture;
    }

    /// <summary>
    /// Stretches the window over the whole desk, whatever its scaling is. A desk of two displays is
    /// covered corner to corner, so a place on the second one can be picked as easily as one on the
    /// first — and the origin travels with the window, because what the pointer reads against has to
    /// be the same number the macro will be written with.
    /// </summary>
    private void Cover()
    {
        var (origin, size) = DeskBounds();
        _origin = origin;
        _scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        Width = size.Width / _scaling;
        Height = size.Height / _scaling;
        Position = origin;

        // The picture covers the window the way the numbers are counted on it: a place on the
        // picture and a place in the numbers are then the same place.
        if (_desk is { } desk)
        {
            desk.Width = Width;
            desk.Height = Height;
        }
    }

    /// <summary>
    /// Where the desk starts and how much of it there is, in screen pixels. Every display together
    /// make one desk, so a place on a second one is picked as easily as one on the first — and the
    /// origin travels with the window, because what the pointer reads against has to be the same
    /// number the macro will be written with. A session with no display list of its own is answered
    /// from the device layer, which is where the picture comes from anyway.
    /// </summary>
    private (PixelPoint Origin, ScreenSize Size) DeskBounds()
    {
        if (Screens is { All.Count: > 0 } screens)
        {
            var left = screens.All.Min(screen => screen.Bounds.X);
            var top = screens.All.Min(screen => screen.Bounds.Y);
            var right = screens.All.Max(screen => screen.Bounds.X + screen.Bounds.Width);
            var bottom = screens.All.Max(screen => screen.Bounds.Y + screen.Bounds.Height);

            return (new PixelPoint(left, top), new ScreenSize(right - left, bottom - top));
        }

        var desk = _screen.Desktop.Bounds;
        return (new PixelPoint(desk.Left, desk.Top), new ScreenSize(desk.Width, desk.Height));
    }

    /// <summary>Turns a place on the window into a place on the screen, in screen pixels.</summary>
    private PixelPoint ToScreen(Point point)
        => new(_origin.X + (int)Math.Round(point.X * _scaling),
               _origin.Y + (int)Math.Round(point.Y * _scaling));

    /// <summary>
    /// 把放大镜摆到指针右下方，只在拖动的时候出现（画新框、拉边、拉角都算拖动），松手就收起来。
    /// 读数气泡挂在放大镜下面而不是自己也贴着指针：两样都跟着指针时，各占一处才谁也不遮谁——
    /// 放大镜压住气泡时，连这一刀切在哪都读不出来了。
    /// </summary>
    private void Aim(Point at, bool show)
    {
        if (!show || _deskFrame is not { } frame)
        {
            _magnifier.Hide();
            _loupeAt = null;
            return;
        }

        var place = ToScreen(at);
        _magnifier.Follow(frame, place.X - _origin.X, place.Y - _origin.Y);
        _loupeAt = _magnifier.PlaceNear(at.X, at.Y, Width, Height, 18);
    }

    /// <summary>The other way round: a rectangle of the screen as it sits on the window.</summary>
    private Rect OnWindow(Region region)
        => new((region.X - _origin.X) / _scaling,
               (region.Y - _origin.Y) / _scaling,
               region.Width / _scaling,
               region.Height / _scaling);
}
