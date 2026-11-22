using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Views;

/// <summary>
/// A loupe that follows the pointer: the few pixels under it, blown up, with the pointer's own pixel
/// boxed. Picking a corner or a single pixel off a whole screen is aiming at something smaller than
/// the pointer itself, and the crosshair says where the pointer is without saying what is there.
/// </summary>
/// <remarks>
/// It reads the picture the picker already took rather than the live screen: what is being aimed at
/// is the frame the numbers are counted on, and a window that comes up or a video that moves must
/// not change what the loupe shows halfway through the aim.
/// </remarks>
internal sealed class Magnifier : Border
{
    /// <summary>How many pixels across are shown, and how big each one is drawn.</summary>
    private const int Cells = 17;
    private const int Cell = 10;

    /// <summary>The pixel the pointer is on, boxed so it can be told from its neighbours.</summary>
    private readonly Border _at;

    private readonly Image _picture;

    private WriteableBitmap? _bitmap;

    /// <summary>What the loupe is already showing, so a pointer that has not moved costs nothing.</summary>
    private ImageFrame? _shown;
    private int _shownX;
    private int _shownY;

    public Magnifier()
    {
        // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
        Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x11, 0x11, 0x11));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(3);
        ClipToBounds = true;
        IsHitTestVisible = false;
        IsVisible = false;

        Width = (Cells * Cell) + 2;
        Height = (Cells * Cell) + 2;

        _picture = new Image
        {
            Width = Cells * Cell,
            Height = Cells * Cell,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };

        RenderOptions.SetBitmapInterpolationMode(_picture, BitmapInterpolationMode.None);

        // 十字线：指针指着哪一格，四条短线在那一格四周对齐，中间留出来不挡住像素本身。
        var mid = (Cells / 2) * Cell;
        var half = Cell / 2;
        var span = Cells * Cell;
        var line = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        var surface = new Canvas { Width = span, Height = span, ClipToBounds = true };
        surface.Children.Add(_picture);

        // 中间那一格占 mid..mid+Cell，十字线穿过它的正中，缺口正好是这一格——差半格看起来就是没对齐。
        var middle = mid + half;
        foreach (var (x1, y1, x2, y2) in new (double X1, double Y1, double X2, double Y2)[]
                 {
                     (0, middle, mid, middle),
                     (mid + Cell, middle, span, middle),
                     (middle, 0, middle, mid),
                     (middle, mid + Cell, middle, span),
                 })
        {
            surface.Children.Add(new Line
            {
                StartPoint = new Point(x1, y1),
                EndPoint = new Point(x2, y2),
                Stroke = line,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });
        }

        // 指针那一格用红框圈住：十字线说的是「上下左右」，这一格说的是「就是它」。
        _at = new Border
        {
            Width = Cell,
            Height = Cell,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3B30")),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(_at, mid);
        Canvas.SetTop(_at, mid);
        surface.Children.Add(_at);
        Child = surface;
    }

    /// <summary>
    /// Shows the pixels around <paramref name="x"/>,<paramref name="y"/> of the picture. What falls
    /// outside the picture stays black, the way a capture at the edge of the screen comes back
    /// short, rather than the loupe moving its own box off the place it is naming.
    /// </summary>
    public void Follow(ImageFrame? frame, int x, int y)
    {
        // 定点拾取器每帧都问一次，指针没动就不必再画一遍。
        IsVisible = true;
        if (_bitmap is not null && ReferenceEquals(frame, _shown) && x == _shownX && y == _shownY)
        {
            return;
        }

        _shown = frame;
        _shownX = x;
        _shownY = y;

        var bitmap = new WriteableBitmap(new PixelSize(Cells, Cells), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);

        var patch = new byte[Cells * Cells * 4];
        var half = Cells / 2;
        for (var row = 0; row < Cells; row++)
        {
            var from = y - half + row;
            for (var column = 0; column < Cells; column++)
            {
                var across = x - half + column;
                var to = ((row * Cells) + column) * 4;
                if (frame is null || frame.IsEmpty
                    || across < 0 || from < 0 || across >= frame.Width || from >= frame.Height)
                {
                    patch[to + 3] = 0xFF;
                    continue;
                }

                var at = ((from * frame.Width) + across) * 4;
                patch[to] = frame.Bgra[at];
                patch[to + 1] = frame.Bgra[at + 1];
                patch[to + 2] = frame.Bgra[at + 2];
                patch[to + 3] = 0xFF;
            }
        }

        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < Cells; row++)
            {
                Marshal.Copy(patch, row * Cells * 4,
                    IntPtr.Add(buffer.Address, row * buffer.RowBytes), Cells * 4);
            }
        }

        var previous = _bitmap;
        _bitmap = bitmap;
        _picture.Source = bitmap;
        previous?.Dispose();
    }

    /// <summary>
    /// Puts the loupe beside the pointer, inside the room it is drawn in. It is placed away from the
    /// pointer rather than under it, on the side asked for, and flips to the opposite side when
    /// there is no room — a loupe that covers the place being aimed at, or the line that says what
    /// is being aimed at, is worse than no loupe.
    /// </summary>
    public Rect PlaceNear(double x, double y, double roomWidth, double roomHeight,
        double gap = 26, bool above = false, bool left = false)
    {
        var width = Width;
        var height = Height;

        var side = left ? x - width - gap : x + gap;
        var top = above ? y - height - gap : y + gap;

        if (left ? side < 0 : side + width > roomWidth)
        {
            side = left ? x + gap : x - width - gap;
        }

        if (above ? top < 0 : top + height > roomHeight)
        {
            top = above ? y + gap : y - height - gap;
        }

        var at = new Rect(
            Math.Clamp(side, 0, Math.Max(0, roomWidth - width)),
            Math.Clamp(top, 0, Math.Max(0, roomHeight - height)),
            width, height);

        Canvas.SetLeft(this, at.X);
        Canvas.SetTop(this, at.Y);
        return at;
    }

    /// <summary>Puts the loupe away, for a pointer that is no longer aiming at anything.</summary>
    public void Hide() => IsVisible = false;

    /// <summary>
    /// Lets go of the last picture when the window it was drawn in closes. It is dropped from the
    /// view first and released once the frame that used it has been drawn, the same way the colour
    /// picker lets go of its own.
    /// </summary>
    public void Release()
    {
        var bitmap = _bitmap;
        _bitmap = null;
        _shown = null;
        _picture.Source = null;
        if (bitmap is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                bitmap.Dispose();
            }
            catch (Exception)
            {
                // A picture the renderer still holds is not worth taking the program down for.
            }
        }, DispatcherPriority.Background);
    }
}
