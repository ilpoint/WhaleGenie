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
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// Shows the desk and lets the user drag a rectangle on it, with everything outside the rectangle
/// pushed back so that the part being framed reads plainly. A rectangle drawn in one go is never
/// quite where it should be, so one that is already there can be moved by dragging inside it and
/// resized by dragging any of its eight handles. The rectangle comes back in screen pixels, which
/// is what every action that works on a region expects.
/// </summary>
public partial class RegionPickerWindow : Window
{
    /// <summary>A rectangle of the screen, in the pixels the engine works in.</summary>
    public readonly record struct Region(int X, int Y, int Width, int Height);

    /// <summary>The two ends of a drag, in the order the pointer visited them.</summary>
    public readonly record struct DragSpan(int StartX, int StartY, int EndX, int EndY);

    /// <summary>A drag shorter than this is a click rather than a selection, and no handle pulls a
    /// rectangle smaller than this either: a rectangle too small to be seen cannot be confirmed.</summary>
    private const int MinimumSize = 4;

    /// <summary>How big a handle is drawn, which is also how close to an edge counts as being on it.</summary>
    private const double HandleSize = 7;

    /// <summary>Where a handle sits on a rectangle, as a fraction of it: the four corners first,
    /// then the middle of each side.</summary>
    private static readonly (double X, double Y)[] HandlePlaces =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (0.5, 0), (0.5, 1), (0, 0.5), (1, 0.5),
    ];

    internal static readonly Cursor MoveCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor WestEast = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor NorthSouth = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor NorthWestCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NorthEastCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor SouthWestCursor = new(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor SouthEastCursor = new(StandardCursorType.BottomRightCorner);

    /// <summary>What a press is doing: drawing a new rectangle, moving the one that is there, or
    /// pulling one of its edges or corners.</summary>
    private enum Grip
    {
        None,
        Move,
        North,
        South,
        East,
        West,
        NorthEast,
        NorthWest,
        SouthEast,
        SouthWest,
    }

    private readonly IWindowDevice _windows;
    private readonly IScreenDevice _screen;
    private readonly Region? _initial;
    private readonly Region? _bounds;
    private readonly bool _arrow;
    private readonly long _liveWindow;
    private readonly Region? _liveOriginal;
    private readonly Border[] _handles;
    private readonly Border[] _targetDim;

    /// <summary>The four bands that push back what is outside the rectangle.</summary>
    private readonly Border[] _dim;

    private readonly Image? _desk;

    private readonly Line _arrowShaft;
    private readonly Line _arrowLeft;
    private readonly Line _arrowRight;
    private readonly Ellipse _arrowStart;

    /// <summary>The desk as it was when this opened, which is what stands in for the screen.</summary>
    private WriteableBitmap? _picture;

    /// <summary>Where the screen starts, in screen pixels; the window sits exactly there.</summary>
    private PixelPoint _origin;

    private double _scaling = 1;

    private Border? _selection;
    private Border? _allowedArea;
    private Border? _readout;
    private TextBlock? _sizeText;
    private TextBlock? _hintText;

    /// <summary>Whether a press is being followed, and what it is doing.</summary>
    private bool _active;
    private Grip _grip;

    /// <summary>Where the pointer went down, and the rectangle as it stood then.</summary>
    private PixelPoint _grab;
    private int _left;
    private int _top;
    private int _right;
    private int _bottom;

    /// <summary>The rectangle as two opposite corners, which is how a drag is followed.</summary>
    private PixelPoint _start;
    private PixelPoint _end;
    private PixelPoint? _pointer;
    private Region? _liveAt;
    private bool _done;

    public RegionPickerWindow()
        : this(SharedDevices.Current.Screen, SharedDevices.Current.Windows, null, null)
    {
    }

    /// <summary>
    /// The picker over the desk the device layer describes, which is what a check hands over when it
    /// wants a rectangle drawn against a desk of its own.
    /// </summary>
    internal RegionPickerWindow(IScreenDevice screen, IWindowDevice windows, Region? initial = null,
        Region? bounds = null, long liveWindow = 0, bool arrow = false)
    {
        _screen = screen;
        _windows = windows;
        _initial = initial;
        _bounds = bounds is { Width: > 0, Height: > 0 } ? bounds : null;
        _arrow = arrow;
        _liveWindow = liveWindow;
        _liveOriginal = liveWindow != 0 ? initial : null;
        InitializeComponent();

        Title = Strings.Get("Region.Title");
        _selection = this.FindControl<Border>("Selection");
        _allowedArea = this.FindControl<Border>("AllowedArea");
        _readout = this.FindControl<Border>("Readout");
        _sizeText = this.FindControl<TextBlock>("SizeText");
        _hintText = this.FindControl<TextBlock>("HintText");
        _desk = this.FindControl<Image>("Desk");
        _dim =
        [
            .. new[] { "DimTop", "DimBottom", "DimLeft", "DimRight" }
                .Select(name => this.FindControl<Border>(name))
                .OfType<Border>(),
        ];
        _targetDim =
        [
            .. new[] { "TargetDimTop", "TargetDimBottom", "TargetDimLeft", "TargetDimRight" }
                .Select(name => this.FindControl<Border>(name))
                .OfType<Border>(),
        ];

        if (_hintText is not null)
        {
            _hintText.Text = Strings.Get(_arrow
                ? "Region.ArrowHint"
                : _bounds is null ? "Region.Hint" : "Region.WindowHint");
        }

        _arrowShaft = new Line { Name = "DragArrowShaft", IsVisible = false, IsHitTestVisible = false };
        _arrowLeft = new Line { Name = "DragArrowLeft", IsVisible = false, IsHitTestVisible = false };
        _arrowRight = new Line { Name = "DragArrowRight", IsVisible = false, IsHitTestVisible = false };
        _arrowStart = new Ellipse
        {
            Name = "DragArrowStart",
            Width = 8,
            Height = 8,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        foreach (var line in new[] { _arrowShaft, _arrowLeft, _arrowRight })
        {
            // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
            line.Stroke = new SolidColorBrush(Color.Parse("#FF3B30"));
            line.StrokeThickness = 3;
        }

        _arrowStart.Fill = new SolidColorBrush(Color.Parse("#FF3B30"));

        // The handles are found by the same hit test as the rectangle itself, so they neither take
        // the press nor have to be looked up in the visual tree.
        _handles = [.. HandlePlaces.Select(_ => new Border
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3B30")),
            BorderThickness = new Thickness(1),
            IsVisible = false,
            IsHitTestVisible = false,
        })];

        if (this.FindControl<Canvas>("Surface") is { } surface)
        {
            foreach (var handle in _handles)
            {
                surface.Children.Add(handle);
            }

            if (_arrow)
            {
                foreach (var shape in new Control[] { _arrowShaft, _arrowLeft, _arrowRight, _arrowStart })
                {
                    surface.Children.Add(shape);
                }
            }
        }

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The rectangle the user has dragged out so far, or null when there is none yet.</summary>
    internal Region? Picked => Size(_start, _end) > 0 ? Rectangle(_start, _end) : null;

    /// <summary>What the picker was closed with, which is what the dialog that opened it gets.</summary>
    internal Region? Confirmed { get; private set; }

    /// <summary>The two ends in the order they were dragged, for the arrow-shaped picker.</summary>
    internal DragSpan? ConfirmedDrag { get; private set; }

    /// <summary>The rectangle a drag covers, whichever way it was dragged.</summary>
    internal static Region Rectangle(PixelPoint start, PixelPoint end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        return new Region(x, y, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    /// <summary>How long the longer side of a drag is, which is how a click is told from a drag.</summary>
    private static int Size(PixelPoint start, PixelPoint end)
        => Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

    /// <summary>An arrow needs length in any direction; a rectangle needs two sides.</summary>
    private bool Ready(Region region) => _arrow
        ? Size(_start, _end) >= MinimumSize
        : region.Width >= MinimumSize && region.Height >= MinimumSize;

    /// <summary>
    /// Shows the picker over <paramref name="owner"/> and reports the rectangle that was dragged.
    /// Every window this program has goes out of the way first, the window that asked included: the
    /// region being picked is usually behind one of them.
    /// </summary>
    public static async Task<Region?> PickAsync(Window owner, IScreenDevice screen,
        IWindowDevice windows)
        => await PickAsync(owner, screen, windows, null, null);

    public static async Task<Region?> PickAsync(Window owner, IScreenDevice screen,
        IWindowDevice windows, Region? initial, Region? bounds = null, long liveWindow = 0)
    {
        using var aside = AppWindows.StepAside(owner);
        return await new RegionPickerWindow(screen, windows, initial, bounds, liveWindow).TakeAsync();
    }

    /// <summary>
    /// The picker that draws a direction rather than a rectangle, reporting the start and end in the
    /// order the pointer visited them.
    /// </summary>
    public static async Task<DragSpan?> PickDragAsync(Window owner, IScreenDevice screen,
        IWindowDevice windows, Region? bounds = null)
    {
        using var aside = AppWindows.StepAside(owner);
        var picker = new RegionPickerWindow(screen, windows, null, bounds, arrow: true);
        _ = await picker.TakeAsync();
        return picker.ConfirmedDrag;
    }

    /// <summary>
    /// The same, and the picture inside the rectangle is kept. What a rectangle is usually dragged
    /// for is a reference picture, and the windows stay away until that picture has been taken: a
    /// window put back in front of where the rectangle was drawn would be in the picture.
    /// </summary>
    public static async Task<string?> CaptureAsync(Window owner, IScreenDevice screen,
        IWindowDevice windows, string folder, Region? bounds = null)
    {
        using var aside = AppWindows.StepAside(owner);

        if (await new RegionPickerWindow(screen, windows, null, bounds).TakeAsync() is not { } region)
        {
            return null;
        }

        // The screen is asked what it is showing, and it answers with the last frame it drew — the
        // one this picker was drawn on. Waiting for the desktop to be drawn again is what keeps the
        // picker's own overlay out of the picture.
        ScreenComposed.Wait();

        return ImageAssets.Capture(screen, region.X, region.Y, region.Width, region.Height, folder);
    }

    /// <summary>
    /// Stands the picker on its own — nothing else of this program is on the screen to own it — and
    /// reports the rectangle the user answered with, or null when it was left without one.
    /// </summary>
    internal async Task<Region?> TakeAsync()
    {
        TakeDesk();
        if (ClampRegion(_initial) is { } initial)
        {
            _start = new PixelPoint(initial.X, initial.Y);
            _end = new PixelPoint(initial.X + initial.Width, initial.Y + initial.Height);
        }
        await AppWindows.ShowAndWaitAsync(this);
        return Confirmed;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Cover();
        Activate();
        Focus();
        var at = _screen.Cursor;
        _pointer = PointIn(new PixelPoint(at.X, at.Y));
        Update();
    }

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
        // last frame it drew — the one that still has them in it.
        ScreenComposed.Wait();

        ImageFrame frame;
        try
        {
            frame = _screen.Capture(origin.X, origin.Y, size.Width, size.Height);
        }
        catch (Exception)
        {
            // A desk that will not be handed over is a picker with no picture under it: the
            // rectangle is still drawn, and its numbers still say where it is.
            return;
        }

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

    /// <summary>The other way round: a rectangle of the screen as it sits on the window.</summary>
    private Rect OnWindow(Region region)
        => new((region.X - _origin.X) / _scaling,
               (region.Y - _origin.Y) / _scaling,
               region.Width / _scaling,
               region.Height / _scaling);

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

        Cursor = CursorFor(_active ? _grip : HitTest(position));
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

        Cursor = CursorFor(HitTest(e.GetPosition(this)));
        Update();
    }

    /// <summary>F1 takes the rectangle, Esc leaves without one.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Finish(null);
                break;

            case Key.F1 when Picked is { } region && Ready(region):
                e.Handled = true;
                Finish(region);
                break;
        }
    }

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
        var west = Math.Abs(point.X - rect.Left) <= HandleSize;
        var east = Math.Abs(point.X - rect.Right) <= HandleSize;
        var north = Math.Abs(point.Y - rect.Top) <= HandleSize;
        var south = Math.Abs(point.Y - rect.Bottom) <= HandleSize;
        var acrossX = point.X >= rect.Left - HandleSize && point.X <= rect.Right + HandleSize;
        var acrossY = point.Y >= rect.Top - HandleSize && point.Y <= rect.Bottom + HandleSize;

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

    /// <summary>Moves the edges the grabbed handle belongs to, leaving the opposite ones put.</summary>
    private void PullTo(PixelPoint point)
    {
        var left = _left;
        var top = _top;
        var right = _right;
        var bottom = _bottom;

        if (_grip is Grip.West or Grip.NorthWest or Grip.SouthWest)
        {
            left = Math.Min(point.X, right - MinimumSize);
        }

        if (_grip is Grip.East or Grip.NorthEast or Grip.SouthEast)
        {
            right = Math.Max(point.X, left + MinimumSize);
        }

        if (_grip is Grip.North or Grip.NorthWest or Grip.NorthEast)
        {
            top = Math.Min(point.Y, bottom - MinimumSize);
        }

        if (_grip is Grip.South or Grip.SouthWest or Grip.SouthEast)
        {
            bottom = Math.Max(point.Y, top + MinimumSize);
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
        // dragged can put its corner under the text, and a hint nobody can find is no hint.
        var at = _pointer ?? _end;
        var left = ((at.X - _origin.X) / _scaling) + 18;
        var top = ((at.Y - _origin.Y) / _scaling) + 18;
        var width = _readout.Bounds.Width > 0 ? _readout.Bounds.Width : 220;
        var height = _readout.Bounds.Height > 0 ? _readout.Bounds.Height : 60;

        Canvas.SetLeft(_readout, Math.Clamp(left, 0, Math.Max(0, Bounds.Width - width)));
        Canvas.SetTop(_readout, Math.Clamp(top, 0, Math.Max(0, Bounds.Height - height)));

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

    /// <summary>The picture belongs to this picker, so it goes when the picker does.</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (!_done)
        {
            RestoreWindow();
        }

        _picture?.Dispose();
        _picture = null;
    }

    private void Finish(Region? region)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Confirmed = region;
        ConfirmedDrag = _arrow && region is not null
            ? new DragSpan(_start.X, _start.Y, _end.X, _end.Y)
            : null;
        if (region is null)
        {
            RestoreWindow();
        }

        Close();
    }

    /// <summary>Esc or closing the picker leaves the mover's window where it was before the drag.</summary>
    private void RestoreWindow()
    {
        if (_liveWindow == 0 || _liveOriginal is not { } original)
        {
            return;
        }

        try
        {
            _windows.Move(_liveWindow, original.X, original.Y, original.Width,
                original.Height);
        }
        catch (Exception)
        {
            // The window has gone; there is nothing left to put back.
        }
    }
}
