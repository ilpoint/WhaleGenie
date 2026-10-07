using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// Dims the screen and lets the user drag a rectangle on it. The rectangle comes back in screen
/// pixels, which is what every action that works on a region expects.
/// </summary>
public partial class RegionPickerWindow : Window
{
    /// <summary>A rectangle of the screen, in the pixels the engine works in.</summary>
    public readonly record struct Region(int X, int Y, int Width, int Height);

    /// <summary>A drag shorter than this is a click, not a selection.</summary>
    private const int MinimumSize = 4;

    private readonly IScreenDevice _screen = new WindowsScreenDevice();

    /// <summary>Where the screen starts, in screen pixels; the window sits exactly there.</summary>
    private PixelPoint _origin;

    private double _scaling = 1;

    private Border? _selection;
    private Border? _readout;
    private TextBlock? _sizeText;

    private bool _dragging;
    private PixelPoint _start;
    private PixelPoint _end;
    private bool _done;

    public RegionPickerWindow()
    {
        InitializeComponent();

        Title = Strings.Get("Region.Title");
        _selection = this.FindControl<Border>("Selection");
        _readout = this.FindControl<Border>("Readout");
        _sizeText = this.FindControl<TextBlock>("SizeText");

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
    public static async Task<Region?> PickAsync(Window owner)
    {
        var previous = owner.WindowState;
        owner.WindowState = WindowState.Minimized;

        try
        {
            return await new RegionPickerWindow().ShowDialogOver<Region?>(owner);
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

    /// <summary>Stretches the window over the main screen, whatever its scaling is.</summary>
    private void Cover()
    {
        var size = _screen.PrimarySize;
        _origin = new PixelPoint(0, 0);
        _scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        if (Screens.Primary is { } screen)
        {
            _origin = screen.Bounds.Position;
            size = new ScreenSize(screen.Bounds.Width, screen.Bounds.Height);
        }

        Width = size.Width / _scaling;
        Height = size.Height / _scaling;
        Position = _origin;
    }

    /// <summary>Turns a place on the window into a place on the screen, in screen pixels.</summary>
    private PixelPoint ToScreen(Point point)
        => new(_origin.X + (int)Math.Round(point.X * _scaling),
               _origin.Y + (int)Math.Round(point.Y * _scaling));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = ToScreen(e.GetPosition(this));

        // A click inside a rectangle that is already there means "use this one".
        if (e.ClickCount > 1 || (Picked is { } chosen && e.ClickCount == 1 && Contains(chosen, point)
                && Size(_start, _end) >= MinimumSize))
        {
            e.Handled = true;
            Finish(Picked);
            return;
        }

        _dragging = true;
        _start = point;
        _end = point;
        e.Pointer.Capture(this);
        e.Handled = true;
        Update();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragging)
        {
            _end = ToScreen(e.GetPosition(this));
        }

        Update();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        // A drag that never moved is a click: with nothing to keep, it just clears the rectangle.
        _end = ToScreen(e.GetPosition(this));
        if (Size(_start, _end) < MinimumSize)
        {
            _end = _start;
        }

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

    private static bool Contains(Region region, PixelPoint point)
        => point.X >= region.X && point.X < region.X + region.Width
           && point.Y >= region.Y && point.Y < region.Y + region.Height;

    /// <summary>Redraws the rectangle and the readout beside it.</summary>
    private void Update()
    {
        if (_selection is null || _sizeText is null || _readout is null)
        {
            return;
        }

        var region = Rectangle(_start, _end);
        var big = region.Width >= MinimumSize && region.Height >= MinimumSize;

        _selection.IsVisible = big;
        if (big)
        {
            Canvas.SetLeft(_selection, (region.X - _origin.X) / _scaling);
            Canvas.SetTop(_selection, (region.Y - _origin.Y) / _scaling);
            _selection.Width = region.Width / _scaling;
            _selection.Height = region.Height / _scaling;
        }

        _sizeText.Text = big
            ? Strings.Format("Region.Size", region.X, region.Y, region.Width, region.Height)
            : Strings.Get("Region.Drag");

        // The readout follows the corner being dragged, kept inside the screen.
        var follow = _dragging ? _end : new PixelPoint(_end.X, _end.Y);
        var left = (follow.X - _origin.X) / _scaling + 18;
        var top = (follow.Y - _origin.Y) / _scaling + 18;
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
