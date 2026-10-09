using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// Picks the point a step should click, counted from the middle of the picture it looks for. The
/// point lands on the reference picture itself, which is the one place a person can see what they
/// are aiming at: a template with a health bar above the head is exactly the case where clicking
/// the match's middle is wrong, and the offset that fixes it is hard to work out in the head.
/// </summary>
public partial class OffsetPickerWindow : Window
{
    /// <summary>How big the picture is drawn, so a small template can be aimed at by eye.</summary>
    private const double Room = 420;
    private const double MostZoom = 6;

    private readonly ImageFrame _picture;
    private readonly WriteableBitmap _bitmap;
    private readonly double _zoom;

    private Canvas? _board;
    private TextBlock? _pointText;
    private Button? _confirm;
    private Border? _mark;

    private ScreenPoint? _picked;

    public OffsetPickerWindow()
    {
        InitializeComponent();
        _picture = ImageFrame.Empty;
        _bitmap = null!;
    }

    public OffsetPickerWindow(ImageFrame picture, string title)
    {
        InitializeComponent();

        _picture = picture;
        _bitmap = ImageAssets.ToBitmap(picture);

        // A template is usually a small patch of screen, so it is drawn larger than life: at one
        // to one a twenty pixel button leaves no room to aim at the middle of it.
        _zoom = Math.Clamp(Math.Min(Room / Math.Max(1, picture.Width),
            Room / Math.Max(1, picture.Height)), 1, MostZoom);

        Title = title;
        _board = this.FindControl<Canvas>("Board");
        _pointText = this.FindControl<TextBlock>("PointText");
        _confirm = this.FindControl<Button>("Confirm");

        if (this.FindControl<TextBlock>("Status") is { } status)
        {
            status.Text = Strings.Format("Offset.Zoom", (int)Math.Round(_zoom * 100));
        }

        Draw();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Shows the picture and reports the point picked, as an offset from its middle, or null when
    /// the user changed their mind.
    /// </summary>
    public static async Task<ScreenPoint?> PickAsync(Window owner, ImageFrame picture, string title)
        => await new OffsetPickerWindow(picture, title).ShowDialogOver<ScreenPoint?>(owner);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _bitmap?.Dispose();
    }

    /// <summary>Draws the picture, the cross that marks its middle, and the point picked so far.</summary>
    private void Draw()
    {
        if (_board is null)
        {
            return;
        }

        var width = _picture.Width * _zoom;
        var height = _picture.Height * _zoom;
        _board.Width = Math.Max(1, width);
        _board.Height = Math.Max(1, height);
        _board.Children.Clear();

        var picture = new Image
        {
            Source = _bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
        };

        RenderOptions.SetBitmapInterpolationMode(picture,
            _zoom >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);

        Canvas.SetLeft(picture, 0);
        Canvas.SetTop(picture, 0);
        _board.Children.Add(picture);

        // The middle is where an offset of nothing lands, which is the thing to look at first.
        var middle = new Border
        {
            Width = 11,
            Height = 11,
            BorderBrush = new SolidColorBrush(Color.Parse("#E0A030")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };

        var centre = Middle();
        Canvas.SetLeft(middle, (centre.X * _zoom) - 5);
        Canvas.SetTop(middle, (centre.Y * _zoom) - 5);
        _board.Children.Add(middle);

        _mark = null;
        if (_picked is { } point)
        {
            _mark = new Border
            {
                Width = 13,
                Height = 13,
                BorderBrush = new SolidColorBrush(Color.Parse("#FF3B30")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(7),
                Child = new Border
                {
                    Width = 3,
                    Height = 3,
                    Background = new SolidColorBrush(Color.Parse("#FF3B30")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            };

            Canvas.SetLeft(_mark, (point.X * _zoom) - 6);
            Canvas.SetTop(_mark, (point.Y * _zoom) - 6);
            _board.Children.Add(_mark);
        }
    }

    /// <summary>The middle of the picture, in the picture's own pixels.</summary>
    private ScreenPoint Middle()
        => new(_picture.Width / 2, _picture.Height / 2);

    /// <summary>
    /// Takes a click as the point to aim at, in the picture's own pixels, kept inside the picture:
    /// an offset that falls outside the reference picture would send the click somewhere the
    /// search never looked at.
    /// </summary>
    private void OnPickPoint(object? sender, PointerPressedEventArgs e)
    {
        var place = e.GetPosition(_board);
        var point = LookGeometry.Inside(_picture.Width, _picture.Height, _zoom, place.X, place.Y);

        _picked = point;
        Draw();
        Report(point);
    }

    private void Report(ScreenPoint point)
    {
        var offset = LookGeometry.Offset(_picture.Width, _picture.Height, point);
        if (_pointText is not null)
        {
            _pointText.Text = Strings.Format("Offset.Point", offset.X, offset.Y)
                + "   "
                + string.Create(CultureInfo.CurrentCulture,
                    $"({point.X}, {point.Y}) / {_picture.Width}x{_picture.Height}");
        }

        if (_confirm is not null)
        {
            _confirm.IsEnabled = true;
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (_picked is { } point)
        {
            Close(LookGeometry.Offset(_picture.Width, _picture.Height, point));
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close(null);
                break;

            case Key.Enter:
                e.Handled = true;
                OnConfirm(sender, new RoutedEventArgs());
                break;

            default:
                break;
        }
    }
}
