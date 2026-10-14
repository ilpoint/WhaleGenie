using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// Dims the screen and lets the user drag a rectangle on it. A rectangle drawn in one go is never
/// quite where it should be, so one that is already there can be moved by dragging inside it and
/// resized by dragging any of its eight handles. The rectangle comes back in screen pixels, which
/// is what every action that works on a region expects.
/// </summary>
public partial class RegionPickerWindow : Window
{
    /// <summary>A rectangle of the screen, in the pixels the engine works in.</summary>
    public readonly record struct Region(int X, int Y, int Width, int Height);

    /// <summary>A drag shorter than this is a click rather than a selection, and no handle pulls a
    /// rectangle smaller than this either: a rectangle too small to be seen cannot be confirmed.</summary>
    private const int MinimumSize = 4;

    /// <summary>How big a handle is drawn, which is also how close to an edge counts as being on it.</summary>
    private const double HandleSize = 7;

    /// <summary>How far the pointer strays before a press counts as a drag rather than a click.</summary>
    private const int ClickSlack = 2;

    /// <summary>Where a handle sits on a rectangle, as a fraction of it: the four corners first,
    /// then the middle of each side.</summary>
    private static readonly (double X, double Y)[] HandlePlaces =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (0.5, 0), (0.5, 1), (0, 0.5), (1, 0.5),
    ];

    private static readonly Cursor Crosshair = new(StandardCursorType.Cross);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
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

    private readonly IScreenDevice _screen;
    private readonly Border[] _handles;

    /// <summary>Where the screen starts, in screen pixels; the window sits exactly there.</summary>
    private PixelPoint _origin;

    private double _scaling = 1;

    private Border? _selection;
    private Border? _readout;
    private TextBlock? _sizeText;

    /// <summary>Whether a press is being followed, and what it is doing.</summary>
    private bool _active;
    private Grip _grip;
    private bool _moved;

    /// <summary>Where the pointer went down, and the rectangle as it stood then.</summary>
    private PixelPoint _grab;
    private int _left;
    private int _top;
    private int _right;
    private int _bottom;

    /// <summary>The rectangle as two opposite corners, which is how a drag is followed.</summary>
    private PixelPoint _start;
    private PixelPoint _end;
    private bool _done;

    public RegionPickerWindow()
        : this(SharedDevices.Current)
    {
    }

    /// <summary>
    /// The picker over the desk the device layer describes, which is what a check hands over when it
    /// wants a rectangle drawn against a desk of its own.
    /// </summary>
    internal RegionPickerWindow(IDeviceLayer devices)
    {
        _screen = devices.Screen;
        InitializeComponent();

        Title = Strings.Get("Region.Title");
        _selection = this.FindControl<Border>("Selection");
        _readout = this.FindControl<Border>("Readout");
        _sizeText = this.FindControl<TextBlock>("SizeText");

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
        }

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The rectangle the user has dragged out so far, or null when there is none yet.</summary>
    internal Region? Picked => Size(_start, _end) > 0 ? Rectangle(_start, _end) : null;

    /// <summary>What the picker was closed with, which is what the dialog that opened it gets.</summary>
    internal Region? Confirmed { get; private set; }

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

    /// <summary>
    /// Shows the picker over <paramref name="owner"/> and reports the rectangle that was dragged.
    /// The owner steps out of the way, because the region being picked is usually behind it.
    /// </summary>
    public static async Task<Region?> PickAsync(Window owner, IDeviceLayer devices)
    {
        var previous = owner.WindowState;
        owner.WindowState = WindowState.Minimized;

        try
        {
            return await new RegionPickerWindow(devices).ShowDialogOver<Region?>(owner);
        }
        finally
        {
            owner.WindowState = previous == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
            owner.Activate();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Cover();
        Activate();
        Focus();
        Update();
    }

    /// <summary>
    /// Stretches the window over the whole desk, whatever its scaling is. A desk of two displays is
    /// covered corner to corner, so a place on the second one can be picked as easily as one on the
    /// first — and the origin travels with the window, because what the pointer reads against has to
    /// be the same number the macro will be written with.
    /// </summary>
    private void Cover()
    {
        var desk = _screen.Desktop.Bounds;
        _origin = new PixelPoint(desk.Left, desk.Top);
        _scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        var size = new ScreenSize(desk.Width, desk.Height);

        if (Screens.All.Count > 0)
        {
            var left = Screens.All.Min(screen => screen.Bounds.X);
            var top = Screens.All.Min(screen => screen.Bounds.Y);
            var right = Screens.All.Max(screen => screen.Bounds.X + screen.Bounds.Width);
            var bottom = Screens.All.Max(screen => screen.Bounds.Y + screen.Bounds.Height);

            _origin = new PixelPoint(left, top);
            size = new ScreenSize(right - left, bottom - top);
        }

        Width = size.Width / _scaling;
        Height = size.Height / _scaling;
        Position = _origin;
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
        var point = ToScreen(position);

        // A double click means "use this one" wherever on the rectangle it lands.
        if (e.ClickCount > 1 && Picked is { } clickable)
        {
            e.Handled = true;
            Finish(clickable);
            return;
        }

        _active = true;
        _moved = false;
        _grab = point;

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

        if (_active)
        {
            var point = ToScreen(position);
            _moved |= Math.Abs(point.X - _grab.X) > ClickSlack
                      || Math.Abs(point.Y - _grab.Y) > ClickSlack;

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

        var grip = _grip;
        _active = false;
        _grip = Grip.None;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (grip == Grip.Move)
        {
            // Pressing inside the rectangle without moving it is the click that says "use this one".
            if (!_moved)
            {
                Finish(Picked);
                return;
            }
        }
        else if (grip == Grip.None)
        {
            // A drag that never moved is a click: with nothing to keep, it just clears the rectangle.
            _end = ToScreen(e.GetPosition(this));
            if (Size(_start, _end) < MinimumSize)
            {
                _end = _start;
            }
        }

        Cursor = Crosshair;
        Update();
    }

    /// <summary>Enter takes the rectangle, Esc leaves without one.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Finish(null);
                break;

            case Key.Enter when Picked is { } region && region.Width >= MinimumSize
                                                  && region.Height >= MinimumSize:
                e.Handled = true;
                Finish(region);
                break;
        }
    }

    /// <summary>Which part of the rectangle a place on the window is on, if any.</summary>
    private Grip HitTest(Point point)
    {
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

    private static Cursor CursorFor(Grip grip) => grip switch
    {
        Grip.Move => MoveCursor,
        Grip.West or Grip.East => WestEast,
        Grip.North or Grip.South => NorthSouth,
        Grip.NorthWest => NorthWestCursor,
        Grip.NorthEast => NorthEastCursor,
        Grip.SouthWest => SouthWestCursor,
        Grip.SouthEast => SouthEastCursor,
        _ => Crosshair,
    };

    /// <summary>Moves the whole rectangle so the place it was grabbed stays under the pointer.</summary>
    private void CarryTo(PixelPoint point)
    {
        var dx = point.X - _grab.X;
        var dy = point.Y - _grab.Y;
        _start = new PixelPoint(_left + dx, _top + dy);
        _end = new PixelPoint(_right + dx, _bottom + dy);
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

        _start = new PixelPoint(left, top);
        _end = new PixelPoint(right, bottom);
    }

    /// <summary>Redraws the rectangle, its handles and the readout beside it.</summary>
    private void Update()
    {
        if (_selection is null || _sizeText is null || _readout is null)
        {
            return;
        }

        var region = Rectangle(_start, _end);
        var big = region.Width >= MinimumSize && region.Height >= MinimumSize;

        _selection.IsVisible = big;
        foreach (var handle in _handles)
        {
            handle.IsVisible = big;
        }

        if (big)
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

        _sizeText.Text = big
            ? Strings.Format("Region.Size", region.X, region.Y, region.Width, region.Height)
            : Strings.Get("Region.Drag");

        // The readout follows the corner in hand, kept inside the screen.
        var left = ((_end.X - _origin.X) / _scaling) + 18;
        var top = ((_end.Y - _origin.Y) / _scaling) + 18;
        var width = _readout.Bounds.Width > 0 ? _readout.Bounds.Width : 220;
        var height = _readout.Bounds.Height > 0 ? _readout.Bounds.Height : 60;

        Canvas.SetLeft(_readout, Math.Clamp(left, 0, Math.Max(0, Bounds.Width - width)));
        Canvas.SetTop(_readout, Math.Clamp(top, 0, Math.Max(0, Bounds.Height - height)));
    }

    private void Finish(Region? region)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Confirmed = region;
        Close(region);
    }
}
