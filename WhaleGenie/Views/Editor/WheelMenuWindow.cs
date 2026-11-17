using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The floating wheel. It never takes focus: the button that opened it is still held down, and the
/// pointer is being aimed at a direction. Everything it draws comes from an immutable layout taken
/// when the session started, so a window coming forward cannot quietly change what a direction
/// means halfway through the gesture.
/// </summary>
public sealed class WheelMenuWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;

    // The wheel never answers the pointer itself: the global hook reports where it is. Letting
    // mouse hits pass through the transparent square is what keeps it from swallowing left clicks,
    // including after a release in the centre circle.
    private const long WsExTransparent = 0x00000020L;

    private readonly WheelCanvas _canvas;
    private readonly Canvas _labels;
    private readonly TextBlock _centre;
    private readonly WheelOptions _options;
    private readonly MenuIconPicture _icons = new();
    private readonly DispatcherTimer? _timeout;
    private readonly Grid _root;
    private readonly bool _preview;
    private readonly double _canvasSize;
    private WheelRuntimeLayout _layout;
    private WheelSlotRef? _hover;
    private Point? _lastAim;
    private double _scale = 1;
    private bool _shown;
    private string? _previewText;

    public WheelMenuWindow(WheelRuntimeLayout layout, WheelOptions options, PixelPoint origin,
        bool preview = false)
    {
        _layout = layout;
        _options = options;
        _preview = preview;
        var diameter = options.Diameter;

        // A transparent margin twice as wide as the wheel lets the guide line follow a swipe out
        // to the invisible circle instead of being cut off at the outer edge. On the live window
        // that margin never answers the pointer; the preview uses the same square so what it shows
        // (including a swipe to the extended circle) is exactly what the live wheel will show.
        _canvasSize = diameter * 2;

        ShowActivated = preview;
        ShowInTaskbar = false;
        Topmost = !preview;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = WindowDecorations.None;
        // A preview has to receive the pointer itself. One 1/255 alpha pixel keeps the whole
        // square hit-testable without painting anything the person can see.
        Background = preview
            ? new SolidColorBrush(Color.FromArgb(1, 255, 255, 255))
            : Brushes.Transparent;
        Width = _canvasSize;
        Height = _canvasSize;

        _canvas = new WheelCanvas { Width = _canvasSize, Height = _canvasSize };
        _labels = new Canvas
        {
            Width = _canvasSize,
            Height = _canvasSize,
            IsHitTestVisible = false,
        };
        _centre = new TextBlock
        {
            Width = diameter * 0.34,
            FontSize = Math.Max(9, options.FontSize - 1),
            Foreground = ThemeBrush.Of("TextSecondary"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _root = new Grid
        {
            Width = _canvasSize,
            Height = _canvasSize,
            // A preview has to answer the pointer everywhere in the square, including the
            // transparent margin outside the visible circles: that is where the extended ring is
            // aimed. The live wheel must stay unhittable — the global hook reports the pointer.
            Background = preview
                ? new SolidColorBrush(Color.FromArgb(1, 255, 255, 255))
                : null,
            Children = { _canvas, _labels, _centre },
        };
        Content = _root;

        if (preview)
        {
            _root.PointerMoved += OnPreviewMove;
            _root.PointerReleased += OnPreviewRelease;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        // The press point is the wheel's centre. It is calculated here as well as on Opened so the
        // caller can read Center straight after showing the window, before the first hover arrives.
        Position = PlaceAt(origin);
        Opened += (_, _) =>
        {
            _shown = true;
            if (!preview)
            {
                ApplyNoActivate();
            }

            Position = PlaceAt(origin);
            PlayPop();
        };

        if (!preview && options.TimeoutMs > 0)
        {
            _timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(options.TimeoutMs) };
            _timeout.Tick += (_, _) =>
            {
                _timeout.Stop();
                TimedOut?.Invoke();
            };
            _timeout.Start();
        }

        Closed += (_, _) =>
        {
            _timeout?.Stop();
            _icons.Dispose();
        };
        Redraw();
    }

    /// <summary>Scales and fades the wheel in, so a press reads as something opening.</summary>
    /// <remarks>
    /// The progress is read from the clock rather than counted in ticks. Each tick draws the wheel
    /// again, and on a fast swipe the queue can delay one; counting ticks made those delays add up
    /// and the pop noticeably longer whenever the pointer was busy.
    /// </remarks>
    private void PlayPop()
    {
        if (_preview || !_options.PopAnimation)
        {
            return;
        }

        _root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        var scale = new ScaleTransform(0.86, 0.86);
        _root.RenderTransform = scale;
        _root.Opacity = 0;
        var started = Stopwatch.GetTimestamp();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var fraction = Math.Clamp(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds / 160, 0, 1);
            scale.ScaleX = 0.86 + 0.14 * fraction;
            scale.ScaleY = 0.86 + 0.14 * fraction;
            _root.Opacity = fraction;
            if (fraction >= 1)
            {
                timer.Stop();
                _root.RenderTransform = null;
            }
        };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    /// <summary>The wheel's centre in screen pixels; a screen edge can move it away from the press.</summary>
    public PixelPoint Center { get; private set; }

    public WheelMetrics Metrics => _canvas.Metrics;

    /// <summary>What the centre circle says: the aim, or "close" when the pointer is inside it.</summary>
    public string CenterHint => _centre.Text ?? string.Empty;

    public event Action? TimedOut;

    public WheelRuntimeSlot? SlotAt(WheelSlotRef reference) => _layout.At(reference);

    public void SetLayout(WheelRuntimeLayout layout)
    {
        _layout = layout;
        Redraw();
    }

    public void Hover(WheelSlotRef? reference)
    {
        if (_hover == reference)
        {
            return;
        }

        _hover = reference;
        Redraw();
        if (_lastAim is { } last)
        {
            _canvas.AimAt(last, reference is { Ring: WheelRing.Extended });
        }
    }

    /// <summary>Aims the guide line at a pointer position in screen pixels.</summary>
    public void Aim(double screenX, double screenY)
    {
        AimLocal(new Point(
            _canvasSize / 2 + (screenX - Center.X) / _scale,
            _canvasSize / 2 + (screenY - Center.Y) / _scale));
    }

    private void AimLocal(Point point)
    {
        _lastAim = point;
        _canvas.AimAt(point, _hover is { Ring: WheelRing.Extended });
    }

    /// <summary>
    /// Empty outer circles can be hidden so the wheel shrinks to the circles that carry actions.
    /// The geometry, the sectors and the labels all read the same count.
    /// </summary>
    private int EffectiveRings()
    {
        var capacity = _layout.ThirdCapacity;
        if (!_options.HideEmptyOuter)
        {
            return capacity > 0 ? 3 : 2;
        }

        if (capacity > 0 && _layout.Slots.Any(slot =>
                slot.Reference.Ring == WheelRing.Third && slot.Macro is not null))
        {
            return 3;
        }

        return _layout.Slots.Any(slot =>
            slot.Reference.Ring == WheelRing.Outer && slot.Macro is not null) ? 2 : 1;
    }

    private void Redraw()
    {
        var diameter = _options.Diameter;
        var rings = EffectiveRings();
        var metrics = WheelGeometry.For(diameter, _layout.WideOuter,
            _layout.ThirdCapacity, rings);
        var painted = _layout.Slots.Select(slot => new WheelPaintSlot(
            slot.Reference,
            slot.Label,
            slot.Reference.Ring == WheelRing.Extended
                ? WheelPaintTone.Extended
                : slot.Macro is null
                    ? WheelPaintTone.Empty
                    : slot.FromProgram
                        ? WheelPaintTone.Overridden
                        : WheelPaintTone.Global,
            _hover == slot.Reference)).ToList();
        _canvas.SetStyle(new WheelPaintStyle(_options.FollowTheme, _options.Gloss,
            _options.HoverBorder, _options.ShowGuide));
        _canvas.Draw(diameter, _layout.WideOuter, painted, false, _canvasSize,
            _layout.ThirdCapacity, rings);

        _labels.Children.Clear();
        var ink = TextInk();
        foreach (var slot in _layout.Slots)
        {
            if (slot.Reference.Ring == WheelRing.Extended || slot.Macro is null
                || slot.Reference.Ring == WheelRing.Outer && !metrics.HasOuter
                || slot.Reference.Ring == WheelRing.Third && !metrics.HasThird)
            {
                continue;
            }

            // An icon is drawn whether or not the label is: choosing one used to change nothing
            // unless "labels off" was also on, which made the picker look broken.
            var picture = slot.Icon is { IsEmpty: false } icon
                ? _icons.Create(icon, _options.HideLabelWithIcon ? 28 : 18, ink)
                : null;
            if (picture is not null && _options.HideLabelWithIcon)
            {
                var (x, y) = WheelGeometry.LabelAt(slot.Reference, metrics, diameter);
                Canvas.SetLeft(picture, _canvasSize / 2 + x - 14);
                Canvas.SetTop(picture, _canvasSize / 2 + y - 14);
                _labels.Children.Add(picture);
                continue;
            }

            if (string.IsNullOrWhiteSpace(slot.Label) && picture is null)
            {
                continue;
            }

            _labels.Children.Add(MakeLabel(slot, diameter, metrics, picture));
        }

        _centre.Text = HintFor(_hover);
        var answer = _hover is null || _hover is { Ring: WheelRing.Extended };
        _centre.Foreground = answer ? WarningInk() : TextInk();
        // "Close" and an invisible extended action have to read at a glance.
        _centre.FontSize = answer
            ? Math.Max(12, _options.FontSize + 1)
            : Math.Max(9, _options.FontSize - 1);
    }

    private Control MakeLabel(WheelRuntimeSlot slot, double diameter, WheelMetrics metrics,
        Control? picture)
    {
        var width = Math.Max(56, diameter * (slot.Reference.Ring == WheelRing.Outer
            && _layout.WideOuter ? 0.13 : 0.20));
        var content = new StackPanel
        {
            Width = width,
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (picture is not null)
        {
            picture.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(picture);
        }

        if (!string.IsNullOrWhiteSpace(slot.Label))
        {
            content.Children.Add(new TextBlock
            {
                Text = slot.Label,
                Width = width,
                FontSize = _options.FontSize,
                Foreground = TextInk(),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var height = (picture is null ? 0 : 20) + _options.FontSize * 1.4;
        var (x, y) = WheelGeometry.LabelAt(slot.Reference, metrics, diameter);
        Canvas.SetLeft(content, _canvasSize / 2 + x - width / 2);
        Canvas.SetTop(content, _canvasSize / 2 + y - height / 2);
        return content;
    }

    /// <summary>
    /// Preview mode answers the pointer inside Avalonia instead of through the global hook, so the
    /// wheel can be seen and tried from the settings window without any system input at all.
    /// </summary>
    private void OnPreviewMove(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(_canvas);
        Hover(WheelGeometry.At(point.X - _canvas.Size / 2,
            point.Y - _canvas.Size / 2, _canvas.Metrics));
        AimLocal(point);
    }

    private void OnPreviewRelease(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(_canvas);
        var slot = WheelGeometry.At(point.X - _canvas.Size / 2,
            point.Y - _canvas.Size / 2, _canvas.Metrics);
        if (slot is not { } chosen)
        {
            _previewText = Strings.Get("Wheel.PreviewCancel");
        }
        else
        {
            var picked = _layout.At(chosen);
            _previewText = picked?.Macro is { } macro
                ? Strings.Format("Wheel.PreviewPicked", macro.Name)
                : Strings.Get("Wheel.PreviewEmpty");
        }

        Hover(slot);
    }

    /// <summary>
    /// The centre is a real answer, not just a scene label: inside it the release closes the
    /// wheel, and on the invisible extended circle it shows the action a swipe would run. That
    /// action is written there as its name or its note, the same words the sectors use; a word
    /// such as "swipe" would name the gesture instead of the action it is about to run.
    /// </summary>
    private string HintFor(WheelSlotRef? reference)
    {
        if (_previewText is not null)
        {
            return _previewText;
        }

        if (reference is null)
        {
            return Strings.Get("Wheel.Close");
        }

        var slot = _layout.At(reference.Value);
        var name = slot is null ? string.Empty : DisplayName(slot);
        var extended = reference.Value.Ring == WheelRing.Extended;

        if (extended && _options.ExtendedHint && name.Length > 0)
        {
            return name;
        }

        return _options.CenterName && slot?.Macro is not null && name.Length > 0
            ? name
            : _layout.SceneName;
    }

    /// <summary>
    /// A note written on the wheel position wins, then the macro's name. The macro's description
    /// is deliberately not used: it is prose, and the centre has one line to name an action.
    /// </summary>
    private static string DisplayName(WheelRuntimeSlot slot)
        => slot.Note.Trim().Length > 0 ? slot.Note.Trim() : slot.Name.Trim();

    /// <summary>A fixed palette has to ink its own text, or light theme words vanish on it.</summary>
    private IBrush TextInk() => _options.FollowTheme
        ? ThemeBrush.Of("TextPrimary") ?? Brushes.Black
        : new SolidColorBrush(Color.Parse("#FF303030"));

    private IBrush WarningInk() => _options.FollowTheme
        ? ThemeBrush.Of("Warning") ?? ThemeBrush.Of("TextPrimary") ?? Brushes.Black
        : new SolidColorBrush(Color.Parse("#FFC0520A"));

    public PixelPoint PlaceAt(PixelPoint origin)
    {
        PixelRect area;
        double scale;
        try
        {
            var screen = Screens.ScreenFromPoint(origin) ?? Screens.Primary;
            area = screen?.WorkingArea ?? new PixelRect(0, 0,
                (int)Math.Ceiling(Width), (int)Math.Ceiling(Height));
            scale = screen?.Scaling ?? RenderScaling;
        }
        catch
        {
            // A headless session has no screen list; the wheel still needs a centre to aim at.
            area = new PixelRect(0, 0, (int)Math.Ceiling(Width), (int)Math.Ceiling(Height));
            scale = 1;
        }

        _scale = scale > 0 ? scale : 1;
        var size = (int)Math.Ceiling(Width * scale);
        var wheel = (int)Math.Ceiling(_options.Diameter * scale);
        var centerX = origin.X;
        var centerY = origin.Y;

        // Only the visible wheel has to stay on screen. The transparent margin can hang off an
        // edge: it draws nothing outside the wheel and never answers the pointer, and clamping the
        // whole square would drag the wheel away from the point the button was pressed at.
        if (_options.ClampToScreen)
        {
            centerX = Math.Clamp(centerX, area.X + wheel / 2,
                Math.Max(area.X + wheel / 2, area.Right - wheel / 2));
            centerY = Math.Clamp(centerY, area.Y + wheel / 2,
                Math.Max(area.Y + wheel / 2, area.Bottom - wheel / 2));
        }

        Center = new PixelPoint(centerX, centerY);
        // Only a wheel that had to move because of a screen edge moves the pointer, and never
        // before the window is on screen: constructing one must not grab the mouse on its own.
        if (_shown && !_preview && _options.MovePointerToCenter && _options.ClampToScreen
            && Center != origin)
        {
            MovePointer(Center);
        }

        return new PixelPoint(centerX - size / 2, centerY - size / 2);
    }

    private static void MovePointer(PixelPoint point)
    {
        if (OperatingSystem.IsWindows())
        {
            SetCursorPos(point.X, point.Y);
        }
    }

    private void ApplyNoActivate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle,
            new IntPtr(style | WsExNoActivate | WsExToolWindow | WsExTransparent));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
