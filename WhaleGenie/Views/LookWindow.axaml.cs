using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// What one step saw when it looked at the screen: the picture, every place it found what it was
/// after, and the scores. This is the window that answers "为什么没找到" — it shows what the
/// machine was actually looking at, which a line of text in a log cannot.
/// </summary>
public partial class LookWindow : Window
{
    /// <summary>How small a mark may be drawn: a colour hit is one pixel of a whole screen.</summary>
    private const double SmallestMark = 9;

    private readonly StepLook _look;
    private readonly WriteableBitmap? _frame;
    private readonly WriteableBitmap? _needle;

    private ScrollViewer? _scroll;
    private Canvas? _board;
    private StackPanel? _marks;
    private TextBlock? _zoomText;
    private Border? _needleFrame;
    private Image? _needleShot;

    private readonly List<(LookBox Box, Border Visual)> _drawn = [];

    private double _zoom = 1;

    public LookWindow()
    {
        InitializeComponent();
        _look = null!;
    }

    public LookWindow(StepLook look)
    {
        InitializeComponent();

        _look = look;
        _frame = look.Frame.IsEmpty ? null : ImageAssets.ToBitmap(look.Frame);
        _needle = look.Needle is { IsEmpty: false } needle ? ImageAssets.ToBitmap(needle) : null;

        _scroll = this.FindControl<ScrollViewer>("Scroll");
        _board = this.FindControl<Canvas>("Board");
        _marks = this.FindControl<StackPanel>("Marks");
        _zoomText = this.FindControl<TextBlock>("ZoomText");
        _needleFrame = this.FindControl<Border>("NeedleFrame");
        _needleShot = this.FindControl<Image>("NeedleShot");

        Title = Strings.Get("Look.Title");
        FillHeader(look);
        Zoom(Fit(), false);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Shows what a step saw, over the window that asked for it. One window per owner: a person
    /// trying a step over and over wants the last answer in front of them, not a pile of windows.
    /// </summary>
    public static void Show(StepLook look, Window owner)
    {
        var open = owner.OwnedWindows.OfType<LookWindow>().FirstOrDefault();
        if (open is not null)
        {
            open.Close();
        }

        new LookWindow(look).ShowOver(owner);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _frame?.Dispose();
        _needle?.Dispose();
    }

    // ------------------------------------------------------------------ filling in

    /// <summary>The three lines at the top: which step, what it was after, and how it went.</summary>
    private void FillHeader(StepLook look)
    {
        var action = ActionCatalog.Find(look.StepType)?.LocalName ?? look.StepType;
        Set("StepLine", Strings.Format("Look.Step", action, look.StepId));

        Set("LookingLine", look.Looking.Length == 0
            ? string.Empty
            : Strings.Format("Look.Looking", look.Looking));

        Set("OutcomeLine", Outcome(look));

        if (!string.IsNullOrEmpty(look.Note))
        {
            Set("NoteLine", Strings.Format("Look.Note", look.Note));
            if (this.FindControl<TextBlock>("NoteLine") is { } note)
            {
                note.IsVisible = true;
            }
        }

        if (_needle is not null && _needleShot is not null && _needleFrame is not null)
        {
            _needleShot.Source = _needle;
            _needleFrame.IsVisible = true;
        }
    }

    private void Set(string name, string text)
    {
        if (this.FindControl<TextBlock>(name) is { } block)
        {
            block.Text = text;
        }
    }

    /// <summary>
    /// How the step went, in a line: found or not, the best score on the picture, and — when the
    /// step wanted a score and did not get one — what it would have taken.
    /// </summary>
    private string Outcome(StepLook look)
    {
        if (look.Kind == LookKind.Capture)
        {
            return Strings.Get("Look.Outcome.Capture");
        }

        if (look.Kind == LookKind.Pixel)
        {
            return Strings.Get("Look.Outcome.Pixel");
        }

        var best = look.Boxes
            .Where(box => box.Role is LookRole.Candidate or LookRole.Hit)
            .Select(box => box.Match.Score)
            .DefaultIfEmpty(double.NaN)
            .Max();

        if (double.IsNaN(best))
        {
            return Strings.Get("Look.Outcome.Nothing");
        }

        if (look.ChosenIndex > 0)
        {
            return Strings.Format("Look.Outcome.Hit", Score(best, look.Kind));
        }

        var line = Strings.Format("Look.Outcome.Miss", Score(best, look.Kind));
        return look.Minimum is { } needed
            ? line + Strings.Format("Look.Needed", Score(needed, look.Kind))
            : line;
    }

    /// <summary>
    /// One score as a person reads it. A picture match is a confidence, so it is a percentage; a
    /// reading of writing is the model's own number and has no range to be a percentage of.
    /// </summary>
    private static string Score(double value, LookKind kind) => kind switch
    {
        LookKind.Template => value.ToString("P0", CultureInfo.CurrentCulture),
        LookKind.Colour => value.ToString("0.00", CultureInfo.CurrentCulture),
        _ => value.ToString("0.#", CultureInfo.CurrentCulture),
    };

    // --------------------------------------------------------------------- picture

    /// <summary>The zoom that shows the whole picture in the room the window has for it.</summary>
    private double Fit()
    {
        var room = _scroll?.Viewport ?? default;
        return LookGeometry.Fit(_look.Frame.Width, _look.Frame.Height,
            (room.Width > 0 ? room.Width : 640) - 8, (room.Height > 0 ? room.Height : 400) - 8);
    }

    private void Zoom(double zoom, bool keepPlace)
    {
        var was = _zoom;
        _zoom = Math.Clamp(zoom, LookGeometry.SmallestZoom, LookGeometry.LargestZoom);

        if (_zoomText is not null)
        {
            _zoomText.Text = (_zoom * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
        }

        Draw();

        // What was in the middle stays in the middle, so zooming in on a mark does not move it
        // off the window.
        if (keepPlace && _scroll is not null && was > 0)
        {
            var half = new Vector(_scroll.Viewport.Width / 2, _scroll.Viewport.Height / 2);
            var middle = _scroll.Offset + half;
            var scaled = middle * (_zoom / was);
            _scroll.Offset = scaled - half;
        }
    }

    /// <summary>Redraws the picture with every mark on it, and the list beside it.</summary>
    private void Draw()
    {
        if (_board is null || _marks is null)
        {
            return;
        }

        _drawn.Clear();
        _board.Children.Clear();
        _marks.Children.Clear();

        _board.Width = Math.Max(1, _look.Frame.Width * _zoom);
        _board.Height = Math.Max(1, _look.Frame.Height * _zoom);

        if (_frame is not null)
        {
            var picture = new Image
            {
                Source = _frame,
                Width = _board.Width,
                Height = _board.Height,
                Stretch = Stretch.Fill,
            };

            RenderOptions.SetBitmapInterpolationMode(picture, _zoom >= 1
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality);

            Canvas.SetLeft(picture, 0);
            Canvas.SetTop(picture, 0);
            _board.Children.Add(picture);
        }

        for (var index = 0; index < _look.Boxes.Count; index++)
        {
            var box = _look.Boxes[index];
            var chosen = index + 1 == _look.ChosenIndex;
            var place = LookGeometry.Place(_look.Origin, box.Match.Location, box.Match.Size, _zoom);
            var visible = LookGeometry.Visible(place, SmallestMark);
            var visual = MarkVisual(box, visible, chosen);

            Canvas.SetLeft(visual, visible.X);
            Canvas.SetTop(visual, visible.Y);
            _board.Children.Add(visual);

            var row = MarkRow(index, box, chosen, visible);
            _marks.Children.Add(row);

            _drawn.Add((box, visual));
        }
    }

    private static IBrush Wash(LookRole role) => role switch
    {
        LookRole.Hit => new SolidColorBrush(Color.FromArgb(46, 63, 191, 95)),
        LookRole.Target => new SolidColorBrush(Color.FromArgb(40, 255, 59, 48)),
        _ => Brushes.Transparent,
    };

    private static IBrush Line(LookRole role) => new SolidColorBrush(role switch
    {
        LookRole.Hit => Color.Parse("#3FBF5F"),
        LookRole.Candidate => Color.Parse("#E0A030"),
        LookRole.Target => Color.Parse("#FF3B30"),
        _ => Color.Parse("#8A8A8A"),
    });

    /// <summary>One mark: a box, with what was read inside it when there is something to read.</summary>
    private static Border MarkVisual(LookBox box, LookRect rect, bool chosen)
    {
        var mark = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            BorderBrush = Line(box.Role),
            BorderThickness = new Thickness(chosen ? 3 : 2),
            Background = Wash(box.Role),
            CornerRadius = new CornerRadius(1),
        };

        if (box.Role == LookRole.Target)
        {
            // The place a step acted on is a single point, so it is drawn as a crosshair rather
            // than a box: there is no rectangle to see.
            mark.CornerRadius = new CornerRadius(rect.Width / 2);
            mark.Child = new Border
            {
                Width = 2,
                Height = 2,
                Background = Line(box.Role),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
        }
        else if (!string.IsNullOrEmpty(box.Label))
        {
            mark.Child = new TextBlock
            {
                Text = box.Label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#F0F0F0")),
                Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
                Padding = new Thickness(3, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
        }

        return mark;
    }

    /// <summary>
    /// One row of the list beside the picture. Hovering it lights up the mark it stands for, and
    /// clicking it brings that mark into the middle of the window.
    /// </summary>
    private Border MarkRow(int index, LookBox box, bool chosen, LookRect visible)
    {
        var row = new Border { Classes = { "MarkRow" } };
        if (chosen)
        {
            row.Classes.Add("chosen");
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
        };

        var toggle = new CheckBox { IsChecked = true, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (_drawn.Count > index)
            {
                _drawn[index].Visual.IsVisible = toggle.IsChecked == true;
            }
        };

        Grid.SetColumn(toggle, 0);
        grid.Children.Add(toggle);

        var chip = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(2),
            Background = Line(box.Role),
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        Grid.SetColumn(chip, 1);
        grid.Children.Add(chip);

        var words = new StackPanel { Spacing = 1 };
        words.Children.Add(new TextBlock
        {
            Text = Strings.Get("Look.Role." + box.Role),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#E4E4E4")),
        });

        words.Children.Add(new TextBlock
        {
            Text = Detail(box),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.Parse("#9A9A9A")),
            TextWrapping = TextWrapping.Wrap,
        });

        Grid.SetColumn(words, 2);
        grid.Children.Add(words);

        var score = new TextBlock
        {
            Text = box.Role == LookRole.Area ? string.Empty : Score(box.Match.Score, _look.Kind),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#E0B080")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };

        Grid.SetColumn(score, 3);
        grid.Children.Add(score);

        row.Child = grid;

        row.PointerEntered += (_, _) => Light(index, true);
        row.PointerExited += (_, _) => Light(index, false);
        row.PointerPressed += (_, _) => Bring(index);
        return row;
    }

    /// <summary>Where a mark is on the screen, in the words the step's own fields use.</summary>
    private static string Detail(LookBox box)
        => box.Role == LookRole.Target
            ? string.Create(CultureInfo.CurrentCulture, $"({box.Match.Location.X}, {box.Match.Location.Y})")
            : string.Create(CultureInfo.CurrentCulture,
                $"{box.Match.Location.X}, {box.Match.Location.Y}  {box.Match.Size.Width}x{box.Match.Size.Height}");

    private void Light(int index, bool on)
    {
        if (_drawn.Count <= index)
        {
            return;
        }

        var (box, visual) = _drawn[index];
        visual.BorderBrush = on ? Brushes.White : Line(box.Role);
    }

    /// <summary>Brings one mark into the middle of the window, which is how a list of ten hits is read.</summary>
    private void Bring(int index)
    {
        if (_drawn.Count <= index || _scroll is null || _board is null)
        {
            return;
        }

        var (_, visual) = _drawn[index];
        var place = LookGeometry.Place(_look.Origin, _drawn[index].Box.Match.Location,
            _drawn[index].Box.Match.Size, _zoom);
        var middle = new Vector(place.X + (place.Width / 2), place.Y + (place.Height / 2));
        _scroll.Offset = middle
            - new Vector(_scroll.Viewport.Width / 2, _scroll.Viewport.Height / 2);
        visual.BorderThickness = new Thickness(3);
    }

    // -------------------------------------------------------------------- commands

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, true), true);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, false), true);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Zoom(Fit(), false);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;

            case Key.OemPlus:
            case Key.Add:
                e.Handled = true;
                Zoom(LookGeometry.Step(_zoom, true), true);
                break;

            case Key.OemMinus:
            case Key.Subtract:
                e.Handled = true;
                Zoom(LookGeometry.Step(_zoom, false), true);
                break;

            case Key.D0:
                e.Handled = true;
                Zoom(Fit(), false);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Writes the picture out with the marks on it, at its own size rather than at whatever the
    /// window happens to be zoomed to: the point of saving it is to show somebody else what was
    /// seen, and a picture shrunk to fit a window is not that.
    /// </summary>
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Look.SaveTitle"),
            SuggestedFileName = "look-" + _look.StepId + ".png",
            FileTypeChoices =
            [
                new FilePickerFileType(Strings.Get("Look.SaveFilter")) { Patterns = ["*.png"] },
            ],
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            Save(path);
            SetStatus(Strings.Format("Look.Saved", path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus(Strings.Format("Look.SaveFailed", error.Message));
        }
    }

    private void SetStatus(string text)
    {
        if (this.FindControl<TextBlock>("Status") is { } status)
        {
            status.Text = text;
        }
    }

    private void Save(string path)
    {
        var width = Math.Max(1, _look.Frame.Width);
        var height = Math.Max(1, _look.Frame.Height);
        var board = Marked(1);
        board.Width = width;
        board.Height = height;
        board.Measure(new Size(width, height));
        board.Arrange(new Rect(0, 0, width, height));
        board.UpdateLayout();

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(board);
        using var stream = File.Create(path);
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// The picture with every mark on it, built fresh: what is on screen is one instance of it and
    /// this is another, because a control cannot hang in two places at once.
    /// </summary>
    private Canvas Marked(double zoom)
    {
        var board = new Canvas { Background = new SolidColorBrush(Color.Parse("#141414")) };
        if (_frame is not null)
        {
            var picture = new Image
            {
                Source = _frame,
                Width = _look.Frame.Width * zoom,
                Height = _look.Frame.Height * zoom,
                Stretch = Stretch.Fill,
            };

            Canvas.SetLeft(picture, 0);
            Canvas.SetTop(picture, 0);
            board.Children.Add(picture);
        }

        for (var index = 0; index < _look.Boxes.Count; index++)
        {
            var box = _look.Boxes[index];
            var place = LookGeometry.Place(_look.Origin, box.Match.Location, box.Match.Size, zoom);
            var visible = LookGeometry.Visible(place, SmallestMark);
            var mark = MarkVisual(box, visible, index + 1 == _look.ChosenIndex);

            Canvas.SetLeft(mark, visible.X);
            Canvas.SetTop(mark, visible.Y);
            board.Children.Add(mark);
        }

        return board;
    }
}
