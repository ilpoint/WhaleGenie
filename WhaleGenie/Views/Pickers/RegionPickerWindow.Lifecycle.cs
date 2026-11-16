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

// Making the window: what it is handed, the two ways it is asked for a rectangle, and what it hands back.
public partial class RegionPickerWindow : Window
{
    public RegionPickerWindow()
        : this(SharedDevices.Current.Screen, SharedDevices.Current.Windows, null, null)
    {
    }

    /// <summary>
    /// The picker over the desk the device layer describes, which is what a check hands over when it
    /// wants a rectangle drawn against a desk of its own.
    /// </summary>
    internal RegionPickerWindow(IScreenDevice screen, IWindowDevice windows, Region? initial = null,
        Region? bounds = null, long liveWindow = 0, bool arrow = false, IInputHook? hook = null)
    {
        _screen = screen;
        _hookSource = hook;
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

            // 最后加：放大镜要压在选择框和遮罩之上，否则拉角时它自己会被压暗。
            surface.Children.Add(_magnifier);

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
        IWindowDevice windows, Region? initial, Region? bounds = null, long liveWindow = 0,
        IInputHook? hook = null)
    {
        using var aside = AppWindows.StepAside(owner);
        // Waited for, not assumed: the picker's overlay and the picture taken through it come from
        // the desktop's own frame, and that frame still has our windows in it until it is drawn again.
        await AppWindows.SettleAsync();
        return await new RegionPickerWindow(screen, windows, initial, bounds, liveWindow, hook: hook)
            .TakeAsync();
    }
}
