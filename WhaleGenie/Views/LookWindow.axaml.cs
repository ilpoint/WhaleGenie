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
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
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

    /// <summary>How far down the window the title bar it draws reaches, margins included: this is
    /// the one part of a window with no decorations that acts like a title bar.</summary>
    private const double TitleBarStrip = 44;

    /// <summary>The room the writing drawn over the picture takes, and the gap above its box.</summary>
    private const double LabelHeight = 18;
    private const double LabelGap = 2;

    /// <summary>
    /// The step this window was opened on, and the machine it looks at, when whoever opened it had
    /// them: with both of these the window can look again by itself. Null when the picture belongs
    /// to a run rather than to a step being written, and then there is nothing to look again at.
    /// </summary>
    private readonly ExecutableStep? _step;
    private readonly IDeviceLayer? _devices;

    private StepLook _look;
    private WriteableBitmap? _frame;
    private WriteableBitmap? _needle;

    private ScrollViewer? _scroll;
    private Canvas? _board;
    private StackPanel? _marks;
    private TextBlock? _zoomText;
    private Border? _needleFrame;
    private Image? _needleShot;
    private StackPanel? _textPanel;

    private readonly List<(LookBox Box, Border Visual)> _drawn = [];

    private double _zoom = 1;

    /// <summary>Whether the zoom on show is the one that fits; anything else is the user's own.</summary>
    private bool _fitted = true;

    /// <summary>Whether the writing that was read is drawn over the picture as well as listed.</summary>
    private bool _labelsOnPicture;

    /// <summary>What was read off this picture, one reading to a line, for taking away.</summary>
    private string _readText = string.Empty;

    /// <summary>True while a second look is being taken, so one press is one look.</summary>
    private bool _looking;

    public LookWindow()
    {
        InitializeComponent();
        _look = null!;
    }

    public LookWindow(StepLook look)
        : this(look, null, null)
    {
    }

    /// <summary>
    /// What one step saw, with the step itself and the machine to look at when the window is meant
    /// to be able to look again. The picture and the step are handed over together because a second
    /// look is the same step asked again, and only the window opened on that step has it.
    /// </summary>
    public LookWindow(StepLook look, ExecutableStep? step, IDeviceLayer? devices)
    {
        InitializeComponent();

        _look = look;
        _step = step;
        _devices = devices;
        _frame = look.Frame.IsEmpty ? null : ImageAssets.ToBitmap(look.Frame);
        _needle = look.Needle is { IsEmpty: false } needle ? ImageAssets.ToBitmap(needle) : null;

        _scroll = this.FindControl<ScrollViewer>("Scroll");
        _board = this.FindControl<Canvas>("Board");
        _marks = this.FindControl<StackPanel>("Marks");
        _zoomText = this.FindControl<TextBlock>("ZoomText");
        _needleFrame = this.FindControl<Border>("NeedleFrame");
        _needleShot = this.FindControl<Image>("NeedleShot");
        _textPanel = this.FindControl<StackPanel>("TextPanel");

        Title = Strings.Get("Look.Title");
        ShowFullScreenLabel();
        FillHeader(look);
        FillText(look);
        ShowNeedle();
        Zoom(Fit(), false, fitted: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Double-clicking the picture is the zoom everybody already knows from a viewer: it goes
        // between the size the pixels really are and the size that fits the window.
        if (_board is not null)
        {
            _board.PointerPressed += OnPicturePressed;
        }

        // A second look needs the step and something to look with, and is offered only when the
        // window was opened with both. Everything else this window shows works without them.
        if (this.FindControl<Button>("Again") is { } again)
        {
            again.IsVisible = _step is not null && _devices is not null;
        }

        // How much room the picture has is not known until the window has been laid out, so the fit
        // worked out above is a guess and these are the correction: one for the first layout, and
        // one for every change of size after it — a window made smaller with a picture at the size
        // that used to fit shows a corner of it and nothing to say so.
        if (_scroll is not null)
        {
            _scroll.SizeChanged += (_, _) => FitToRoom();
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(FitToRoom, DispatcherPriority.Loaded);
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

    /// <summary>
    /// The same, for the window that has the step in hand: it can be looked at again from here, so
    /// a threshold that was set too high is tried again without closing this window and going back
    /// to the one underneath it.
    /// </summary>
    public static void Show(StepLook look, Window owner, ExecutableStep step, IDeviceLayer devices)
    {
        var open = owner.OwnedWindows.OfType<LookWindow>().FirstOrDefault();
        if (open is not null)
        {
            open.Close();
        }

        new LookWindow(look, step, devices).ShowOver(owner);
    }

    /// <summary>
    /// The same, for a window that has the machine but not the step: the picture a run took belongs
    /// to that run and there is nothing to look at again, but what was read off it is still worth
    /// being able to take away. Without the machine the 「复制」 that is drawn there would do nothing.
    /// </summary>
    public static void Show(StepLook look, Window owner, IDeviceLayer devices)
    {
        var open = owner.OwnedWindows.OfType<LookWindow>().FirstOrDefault();
        if (open is not null)
        {
            open.Close();
        }

        new LookWindow(look, null, devices).ShowOver(owner);
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

        // Which of several pictures the step went with is part of what it was looking for: a step
        // that lists three of them and found the second one is telling the user something the name
        // of that one alone does not.
        var looking = look.Looking.Length == 0
            ? string.Empty
            : look.PictureCount > 1
                ? Strings.Format("Look.Picture", look.PictureNumber, look.PictureCount, look.Looking)
                : Strings.Format("Look.Looking", look.Looking);

        Set("LookingLine", string.Join(" · ",
            new[] { looking, Where(look) }.Where(part => part.Length > 0)));

        Set("OutcomeLine", Outcome(look));

        if (!string.IsNullOrEmpty(look.Note))
        {
            Set("NoteLine", Strings.Format("Look.Note", look.Note));
            if (this.FindControl<TextBlock>("NoteLine") is { } note)
            {
                note.IsVisible = true;
            }
        }

    }

    /// <summary>
    /// The reference picture, small, beside what the step was after: it says at a glance which of
    /// several pictures the step went with, and there is nothing to show for the steps that were
    /// not looking for one.
    /// </summary>
    private void ShowNeedle()
    {
        if (_needleShot is null || _needleFrame is null)
        {
            return;
        }

        _needleShot.Source = _needle;
        _needleFrame.IsVisible = _needle is not null;
    }

    /// <summary>
    /// The writing this look read, one reading to a line, and the box it is read out of. A step
    /// that read writing is the one case where what is on the picture is meant to be taken away:
    /// a line read off the screen is usually wanted for somewhere else, and this is the way out
    /// that does not go through the picture.
    /// </summary>
    private void FillText(StepLook look)
    {
        if (_textPanel is not { } panel)
        {
            return;
        }

        // Every reading the engine handed over, in the order it read them, which is also what the
        // marks on the picture are made of.
        _readText = string.Join(Environment.NewLine,
            look.Boxes.Select(box => box.Label).Where(label => !string.IsNullOrEmpty(label)));

        panel.IsVisible = _readText.Length > 0;
        if (this.FindControl<TextBox>("ReadText") is { } box)
        {
            box.Text = _readText;
        }
    }

    /// <summary>
    /// Takes a fresh look at the screen and fills this window in with it, in place rather than as
    /// another window: what is being tried over and over is one step's own reading.
    /// </summary>
    private async void OnLookAgain(object? sender, RoutedEventArgs e)
    {
        if (_looking || _step is not { } step || _devices is not { } devices)
        {
            return;
        }

        _looking = true;
        SetStatus(Strings.Get("Look.AgainBusy"));
        try
        {
            // Off the thread that draws the window, the same way the dialog's own run does it, and
            // through the same entrance: looking and nothing else — no click, no key, no variable.
            var outcome = await Task.Run(() => MacroRunner.LookOnce(step, devices));
            if (outcome.Look is { } look)
            {
                Refill(look);
                SetStatus(string.Empty);
            }
            else
            {
                SetStatus(Strings.Format("Look.AgainFailed", Strings.Get(outcome.Key)));
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            SetStatus(Strings.Format("Look.AgainFailed", error.Message));
        }
        finally
        {
            _looking = false;
        }
    }

    /// <summary>
    /// Replaces everything this window is showing with another look at the same step: the picture,
    /// the header, the marks and the writing that was read.
    /// </summary>
    private void Refill(StepLook look)
    {
        _look = look;
        _frame?.Dispose();
        _needle?.Dispose();
        _frame = look.Frame.IsEmpty ? null : ImageAssets.ToBitmap(look.Frame);
        _needle = look.Needle is { IsEmpty: false } needle ? ImageAssets.ToBitmap(needle) : null;

        FillHeader(look);
        FillText(look);
        ShowNeedle();
        Draw();
    }

    /// <summary>Puts what was read on the clipboard, which is where the person reading it wants it.</summary>
    private void OnCopyText(object? sender, RoutedEventArgs e)
    {
        if (_devices?.Clipboard is not { } clipboard || _readText.Length == 0)
        {
            return;
        }

        clipboard.WriteText(_readText);
        SetStatus(Strings.Get("Look.TextCopied"));
    }

    /// <summary>
    /// Shows the writing over the picture as well, or stops showing it, when the switch on the
    /// toolbar is used. Off by default: the writing is what the box on the picture is for reading,
    /// and a label drawn over it is in the way of exactly that.
    /// </summary>
    private void OnToggleLabels(object? sender, RoutedEventArgs e)
    {
        _labelsOnPicture = sender is CheckBox { IsChecked: true };
        Draw();
    }

    private void Set(string name, string text)
    {
        if (this.FindControl<TextBlock>(name) is { } block)
        {
            block.Text = text;
        }
    }

    /// <summary>
    /// Which window the picture came out of and how it was taken, beside what the step was after.
    /// A step that found nothing is answered by this line: it says whether the step was reading the
    /// right window at all, which the picture underneath it cannot say on its own.
    /// </summary>
    private static string Where(StepLook look)
    {
        if (look.Method == CaptureWay.Auto)
        {
            return string.Empty;
        }

        var source = look.Source.Length == 0
            ? Strings.Get("Look.Source.desktop")
            : Strings.Format("Look.Source.window", look.Source);

        return Strings.Format("Look.Where", source,
            Strings.Get("Look.Way." + CaptureWayNames.Written(look.Method)));
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
        LookKind.Template or LookKind.Detection
            => value.ToString("P0", CultureInfo.CurrentCulture),
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

    /// <summary>
    /// Fits the picture to the room there really is for it, once the window has been laid out or
    /// changed size. It is the correction to the fit worked out when the window was built, which
    /// had no room to measure yet. A size the person chose by hand is left alone: the picture was
    /// zoomed in on purpose, and moving the window is not asking for that to be undone.
    /// </summary>
    private void FitToRoom()
    {
        if (_fitted)
        {
            Zoom(Fit(), false, fitted: true);
        }
    }

    private void Zoom(double zoom, bool keepPlace, bool fitted = false)
    {
        var was = _zoom;
        _zoom = Math.Clamp(zoom, LookGeometry.SmallestZoom, LookGeometry.LargestZoom);
        _fitted = fitted;

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
            visual.Tag = index;

            Canvas.SetLeft(visual, visible.X);
            Canvas.SetTop(visual, visible.Y);
            _board.Children.Add(visual);

            // The writing, when it is asked for, goes over the picture above the box it belongs to.
            // Inside the box is where the writing that was read is, and covering it is exactly what
            // this window is for looking at.
            if (_labelsOnPicture && Label(box) is { } label)
            {
                var above = visible.Y - LabelHeight - LabelGap;
                // The writing and its box carry the same number, so which writing belongs over
                // which box is a question with one answer rather than a guess from the positions.
                label.Tag = index;
                Canvas.SetLeft(label, visible.X);
                Canvas.SetTop(label, above >= 0 ? above : visible.Y + visible.Height + LabelGap);
                _board.Children.Add(label);
            }

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

    /// <summary>
    /// One mark: a box, and nothing else. What was read where the box is sits over the box rather
    /// than in it, and only when it was asked for — see <see cref="Label"/>.
    /// </summary>
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

        return mark;
    }

    /// <summary>
    /// What was read where a mark is, ready to be put over the picture above the mark's own box, or
    /// null when there is nothing to write or the writing is not being shown on the picture. Named
    /// so that what is drawn over the picture can be told apart from everything else on it.
    /// </summary>
    private TextBlock? Label(LookBox box)
    {
        if (string.IsNullOrEmpty(box.Label))
        {
            return null;
        }

        return new TextBlock
        {
            Name = "MarkText",
            Text = box.Label,
            // Fixed height, so where the writing ends and the box begins is the same every time:
            // the two must not overlap, and a line whose height follows its glyphs would decide
            // that for itself.
            Height = LabelHeight,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#F0F0F0")),
            Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
            Padding = new Thickness(3, 0),
        };
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

        // What was read here, written out in the list as well as on the picture: this is the line
        // somebody wants to copy, and reading it off a picture is not copying it.
        if (!string.IsNullOrEmpty(box.Label))
        {
            words.Children.Add(new TextBlock
            {
                Text = box.Label,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.Parse("#BFE3BF")),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // How far this one was from counting, for the steps that named a score to reach. The line
        // at the top says what the best of them was; this says what each one needed.
        if (Short(box) is { } short_)
        {
            words.Children.Add(new TextBlock
            {
                Text = short_,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.Parse("#D8B36A")),
                TextWrapping = TextWrapping.Wrap,
            });
        }

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

    /// <summary>
    /// Where a mark is on the screen, in the numbers the step's own fields take. The middle comes
    /// first: that is the point a click and a drag are aimed at, it is what a hit's own record
    /// carries, and the arithmetic behind it is the engine's (<see cref="ImageMatch.Center"/> is
    /// the same integer division the record's x and y come from). The top left and the size follow,
    /// for the fields that ask for a rectangle rather than a place.
    /// </summary>
    private static string Detail(LookBox box)
    {
        var centre = box.Match.Center;
        var at = string.Create(CultureInfo.CurrentCulture, $"({centre.X}, {centre.Y})");
        if (box.Role == LookRole.Target)
        {
            return Strings.Format("Look.Centre", at);
        }

        var corner = string.Create(CultureInfo.CurrentCulture,
            $"{box.Match.Location.X}, {box.Match.Location.Y}");
        return Strings.Format("Look.Box", at, corner, box.Match.Size.Width, box.Match.Size.Height);
    }

    /// <summary>
    /// How far one candidate was from the score the step asked for, or null when it is not a
    /// candidate at all or the step never named a score: a search area was never compared with
    /// anything, and a step without a threshold has nothing for a mark to fall short of.
    /// </summary>
    private string? Short(LookBox box)
    {
        if (_look.Minimum is not { } needed
            || box.Role is not (LookRole.Candidate or LookRole.Hit)
            || box.Match.Score >= needed)
        {
            return null;
        }

        return Strings.Format("Look.Short", Score(needed - box.Match.Score, _look.Kind));
    }

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

    /// <summary>
    /// Puts the window away without closing it, so what was found can be held up against the screen
    /// it came from and read again afterwards.
    /// </summary>
    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        // A window filling the screen has no title bar to come back to, so it is put back to its
        // normal size first; minimizing straight out of full screen leaves a taskbar button that
        // restores it full screen with nothing to close it with.
        if (WindowState == WindowState.FullScreen)
        {
            ToggleFullScreen();
        }

        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Whether the window fills the screen, which is how a reference picture and a screenful of
    /// hits are read: a window shrunk to fit a monitor shows neither at its own size.
    /// </summary>
    private void OnToggleFullScreen(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
        ShowFullScreenLabel();
    }

    /// <summary>The button says what it will do next, so there is nothing to remember.</summary>
    private void ShowFullScreenLabel()
    {
        if (this.FindControl<Button>("FullScreen") is { } button)
        {
            button.Content = Strings.Get(WindowState == WindowState.FullScreen
                ? "Look.FullScreenExit"
                : "Look.FullScreen");
        }
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, true), true);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, false), true);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Zoom(Fit(), false);

    /// <summary>Double-clicking the picture goes between its real size and the size that fits.</summary>
    private void OnPicturePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        e.Handled = true;
        ToggleRealSize();
    }

    /// <summary>
    /// Switches the picture between the size its pixels really are and the size that fits the room
    /// there is for it, which is what double-clicking the picture does.
    /// </summary>
    public void ToggleRealSize() => Zoom(Math.Abs(_zoom - 1) < 0.001 ? Fit() : 1, false);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // Esc leaves the full screen before it leaves the window: the two are one key because
            // filling the screen is the last thing that happened, and that is what one wants undone.
            case Key.Escape:
                e.Handled = true;
                if (WindowState == WindowState.FullScreen)
                {
                    ToggleFullScreen();
                }
                else
                {
                    Close();
                }

                break;

            case Key.F11:
                e.Handled = true;
                ToggleFullScreen();
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
    private void OnSave(object? sender, RoutedEventArgs e)
        => SaveWith(title: "Look.SaveTitle", name: "look-" + _look.StepId, marks: true);

    /// <summary>
    /// Writes the picture out as it came off the screen, without a single mark on it. What a step
    /// looked at is also what somebody cuts a fresh reference picture out of, and marks drawn over
    /// it would come along into the template and make it match only itself.
    /// </summary>
    private void OnSaveRaw(object? sender, RoutedEventArgs e)
        => SaveWith(title: "Look.SaveRawTitle", name: "screen-" + _look.StepId, marks: false);

    private async void SaveWith(string title, string name, bool marks)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get(title),
            SuggestedFileName = name + ".png",
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
            Save(path, marks);
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

    private void Save(string path, bool marks)
    {
        var width = Math.Max(1, _look.Frame.Width);
        var height = Math.Max(1, _look.Frame.Height);
        var board = SavedBoard(1, marks);
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
    /// The picture as a saved file gets it — with every mark drawn over it, or bare. Built fresh
    /// each time: what is on screen is one instance of it and this is another, because a control
    /// cannot hang in two places at once.
    /// </summary>
    internal Canvas SavedBoard(double zoom, bool marks)
    {
        var board = new Canvas { Background = new SolidColorBrush(Color.Parse("#141414")) };
        Picture(board, zoom);
        if (!marks)
        {
            return board;
        }

        for (var index = 0; index < _look.Boxes.Count; index++)
        {
            var box = _look.Boxes[index];
            var place = LookGeometry.Place(_look.Origin, box.Match.Location, box.Match.Size, zoom);
            var visible = LookGeometry.Visible(place, SmallestMark);
            var mark = MarkVisual(box, visible, index + 1 == _look.ChosenIndex);
            mark.Tag = index;

            Canvas.SetLeft(mark, visible.X);
            Canvas.SetTop(mark, visible.Y);
            board.Children.Add(mark);

            // The same writing the window is showing goes into the picture it saves, so what is
            // handed to somebody else is what is on the screen here.
            if (_labelsOnPicture && Label(box) is { } label)
            {
                var above = visible.Y - LabelHeight - LabelGap;
                label.Tag = index;
                Canvas.SetLeft(label, visible.X);
                Canvas.SetTop(label, above >= 0 ? above : visible.Y + visible.Height + LabelGap);
                board.Children.Add(label);
            }
        }

        return board;
    }

    private void Picture(Canvas board, double zoom)
    {
        if (_frame is null)
        {
            return;
        }

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

    /// <summary>
    /// The window is moved by dragging the title bar it draws itself, the way every window here is.
    /// A window filling the screen has nowhere to be moved to, and pulling one out of full screen
    /// would leave it in a state its own button no longer describes.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && WindowState != WindowState.FullScreen
            && e.GetPosition(this).Y <= TitleBarStrip)
        {
            BeginMoveDrag(e);
        }
    }
}
