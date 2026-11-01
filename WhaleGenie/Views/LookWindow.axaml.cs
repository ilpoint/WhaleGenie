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
using Avalonia.VisualTree;
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
public partial class LookWindow : AppWindow
{
    /// <summary>How small a mark may be drawn: a colour hit is one pixel of a whole screen.</summary>
    private const double SmallestMark = 9;

    /// <summary>How far down the window the title bar it draws reaches, margins included: this is
    /// the one part of a window with no decorations that acts like a title bar.</summary>
    private const double TitleBarStrip = 44;

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

    /// <summary>The rows beside the picture, in the order they were drawn: what each one says,
    /// what it stands for, and the switch that shows it on the picture.</summary>
    private readonly List<string> _markTexts = [];

    private readonly List<string> _markRoles = [];
    private readonly List<Border> _markRows = [];
    private readonly List<CheckBox> _markToggles = [];
    private TextBlock? _zoomText;
    private Border? _needleFrame;
    private Image? _needleShot;

    private readonly List<(LookBox Box, Border Visual)> _drawn = [];

    private double _zoom = 1;

    /// <summary>Whether the zoom on show is the one that fits; anything else is the user's own.</summary>
    private bool _fitted = true;

    /// <summary>True while the picture is being dragged, and where the drag started.</summary>
    private bool _panning;
    private Point _panFrom;
    private Vector _panOrigin;

    /// <summary>What was read off this picture, one reading to a line, for taking away.</summary>

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
        WindowChrome.Attach(this);

        _look = look;
        _step = step;
        _devices = devices;
        _frame = look.Frame.IsEmpty ? null : ImageAssets.ToBitmap(look.Frame);
        _needle = look.Needle is { IsEmpty: false } needle ? ImageAssets.ToBitmap(needle) : null;

        _scroll = this.FindControl<ScrollViewer>("Scroll");
        _board = this.FindControl<Canvas>("Board");
        _marks = this.FindControl<StackPanel>("Marks");
        if (_marks is not null)
        {
            // The empty space under the rows is part of the same list, so a right-click there does
            // what a right-click on it does: copies the lot.
            var copyAll = new MenuItem { Header = Strings.Get("Look.CopyMarks") };
            copyAll.Click += OnCopyMarks;
            _marks.ContextMenu = new ContextMenu { Items = { copyAll } };
        }

        _zoomText = this.FindControl<TextBlock>("ZoomText");
        _needleFrame = this.FindControl<Border>("NeedleFrame");
        _needleShot = this.FindControl<Image>("NeedleShot");

        Title = Strings.Get("Look.Title");
        FillHeader(look);
        ShowNeedle();
        Zoom(Fit(), false, fitted: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // A picture viewer's three gestures: drag to move it, wheel to zoom it, double-click to go
        // between its own size and the size that fits. The picture is larger than the window most
        // of the time — a screenful of it at 100% always is — so moving it is the commonest thing
        // asked of it and it was the one thing the window could not do.
        if (_board is not null)
        {
            _board.Cursor = new Cursor(StandardCursorType.Hand);
            _board.PointerPressed += OnPicturePressed;
            _board.PointerMoved += OnPictureMoved;
            _board.PointerReleased += OnPictureReleased;
        }

        // Tunnelled: the scroll viewer answers the wheel itself and marks it handled, so a handler
        // beside it only ever sees the wheel when the picture has nowhere to scroll — the wheel then
        // meant two different things depending on the zoom. Zooming wins either way.
        AddHandler(PointerWheelChangedEvent, OnPictureWheel, RoutingStrategies.Tunnel);

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

        // What was read is in the list beside the picture, one reading to a line, in a box of its own.
        // Repeating it up here as well was the same words twice: with a screenful of writing the top
        // grew until the picture was a strip. The box stays for the looks that read nothing — the
        // engine's own note is the only place that says what happened, and then nothing else does.
        if (Readings(look).Count == 0 && !string.IsNullOrEmpty(look.Note))
        {
            Set("NoteLine", Strings.Format("Look.Note", look.Note));
            if (this.FindControl<Control>("NoteLine") is { } box)
            {
                box.IsVisible = true;
            }
        }

    }

    /// <summary>
    /// What was read off the picture, one reading to a line. The mark that stands for the area the
    /// step looked at carries a label of its own — where it is and how big — which is a description
    /// of the search rather than something a person would want to paste anywhere.
    /// </summary>
    private static IReadOnlyList<string> Readings(StepLook look)
        => [.. look.Boxes
            .Where(box => box.Role is not LookRole.Area)
            .Select(box => box.Label ?? string.Empty)
            .Where(label => label.Length > 0)];

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
            // Out of the way first: this window covers the very screen the step is looking at, and a
            // look taken with it standing finds the window rather than what is behind it. Same
            // entrance as the dialog's own run — looking and nothing else: no click, no key, no
            // variable — and the window comes back when the look is done.
            LookOutcome outcome;
            using (var aside = AppWindows.StepAside(this))
            {
                // Waited for, not assumed: hiding is one call, but the desktop still holds the frame
                // that had these windows in it until it has drawn again.
                await AppWindows.SettleAsync();
                outcome = await Task.Run(() => MacroRunner.LookOnce(step, devices));
            }

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
        ShowNeedle();
        Draw();
    }

    /// <summary>Puts what was read on the clipboard, which is where the person reading it wants it.</summary>
    /// <summary>
    /// Puts the checked rows' writing on the clipboard, one reading to a line. A row that found
    /// nothing still says what it is — where it looked, what it was after — so the list copied out
    /// of is the list that was read, not only the rows that happened to hold a label.
    /// </summary>
    private void OnCopyMarks(object? sender, RoutedEventArgs e) => CopyRows(null);

    private void OnCopyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: int index })
        {
            CopyRows(index);
        }
    }

    private void CopyRows(int? only)
    {
        if (_devices?.Clipboard is not { } clipboard)
        {
            return;
        }

        var lines = new List<string>();
        for (var index = 0; index < _markTexts.Count; index++)
        {
            if (only is { } wanted && wanted != index)
            {
                continue;
            }

            if (!_markRows[index].IsVisible || (only is null && _markToggles[index].IsChecked != true))
            {
                continue;
            }

            // The row that stands for the search area is left out: what is being copied is what the
            // step read, not where it looked.
            if (_markRoles[index] == Strings.Get("Look.Role.Area"))
            {
                continue;
            }

            if (_markTexts[index].Length > 0)
            {
                lines.Add(_markTexts[index]);
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        clipboard.WriteText(string.Join(Environment.NewLine, lines));
        SetStatus(Strings.Get("Look.TextCopied"));
    }

    /// <summary>
    /// Checks every row, which is what a person wants before copying the lot: the rows are the
    /// marks drawn on the picture, and a list where some are already put away is a list that copies
    /// part of itself without saying so.
    /// </summary>
    private void OnSelectAllMarks(object? sender, RoutedEventArgs e)
    {
        foreach (var toggle in _markToggles)
        {
            toggle.IsChecked = true;
        }
    }

    /// <summary>
    /// Turns every row the other way: what was ticked is left out, what was left out is ticked. The
    /// rows a filter narrowed away are not touched — the list on show is what "all of them" means
    /// while a filter is on, and turning rows nobody can see is a change with nothing to show for it.
    /// </summary>
    private void OnInvertMarks(object? sender, RoutedEventArgs e)
    {
        foreach (var item in _markToggles
                     .Select((toggle, index) => (Toggle: toggle, index))
                     .Where(item => _markRows[item.index].IsVisible))
        {
            item.Toggle.IsChecked = item.Toggle.IsChecked != true;
        }
    }

    /// <summary>Narrows the list to the rows whose writing or role mentions what was typed.</summary>
    private void OnMarkFilterChanged(object? sender, TextChangedEventArgs e) => ApplyMarkFilter();

    private void ApplyMarkFilter()
    {
        var wanted = this.FindControl<TextBox>("MarkFilter")?.Text?.Trim() ?? string.Empty;
        for (var index = 0; index < _markRows.Count; index++)
        {
            _markRows[index].IsVisible = wanted.Length == 0
                || _markTexts[index].Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || _markRoles[index].Contains(wanted, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void Set(string name, string text)
    {
        // Asked for by name rather than by type: the lookup that names a type fails loudly when the
        // control is another one, and the note is a box while the lines above it are text.
        switch (this.FindControl<Control>(name))
        {
            case TextBlock block:
                block.Text = text;
                break;
            case TextBox box:
                box.Text = text;
                break;
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

    /// <summary>The zoom the picture is drawn at right now, as a number rather than as the label.</summary>
    internal double ZoomLevel => _zoom;

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
        _markTexts.Clear();
        _markRoles.Clear();
        _markRows.Clear();
        _markToggles.Clear();

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

            var row = MarkRow(index, box, chosen, visible);
            _marks.Children.Add(row);

            _drawn.Add((box, visual));
        }

        // A redraw keeps the filter that was typed: looking again at the same step should not put
        // back the rows the person just narrowed away.
        ApplyMarkFilter();
    }

    /// <summary>
    /// 盖在画面上的半透明罩。**留字面值，不走令牌**：它必须能透出底下的截图，
    /// 而令牌表里没有"半透明填充"这一档（已记进待立族）。换成不透明令牌就等于把画面盖住。
    /// </summary>
    private static IBrush Wash(LookRole role) => role switch
    {
        LookRole.Hit => new SolidColorBrush(Color.FromArgb(46, 63, 191, 95)),
        LookRole.Target => new SolidColorBrush(Color.FromArgb(40, 255, 59, 48)),
        _ => Brushes.Transparent,
    };

    /// <summary>
    /// 框的描边。**外观是逐行算的、不走类**（`MarkRow` 也一样）：选中的、候选的、目标的外观各不相同，
    /// 类描述的是"这一类长什么样"，两件事。但**颜色走语义令牌**——这是"用类"与"自绘"之间的第三态。
    /// </summary>
    private static IBrush? Line(LookRole role) => ThemeBrush.Of(role switch
    {
        LookRole.Hit => "Success",
        LookRole.Candidate => "Warning",
        LookRole.Target => "Danger",
        _ => "TextTertiary",
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

        // What this row says, kept beside it: the filter reads it, and so does copying the list.
        var role = Strings.Get("Look.Role." + box.Role);
        _markRoles.Add(role);
        _markTexts.Add(string.IsNullOrEmpty(box.Label) ? Detail(box) : box.Label);
        _markRows.Add(row);

        var copy = new MenuItem { Header = Strings.Get("Look.CopyRow"), Tag = index };
        copy.Click += OnCopyRow;
        row.ContextMenu = new ContextMenu { Items = { copy } };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
        };

        var toggle = new CheckBox { IsChecked = true, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        _markToggles.Add(toggle);
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
            Text = role,
            FontSize = 12,
            Foreground = ThemeBrush.Of("TextPrimary"),
        });

        words.Children.Add(new TextBlock
        {
            Text = Detail(box),
            FontSize = 11.5,
            Foreground = ThemeBrush.Of("TextTertiary"),
            TextWrapping = TextWrapping.Wrap,
        });

        // What was read here, in a box of its own and under a caption of its own. It used to be one
        // more line of the same grey as the numbers above it, and the two are not the same kind of
        // thing: one is where on the screen the mark is, the other is what the step read there. The
        // caption says which is which; the box keeps the writing together when it wraps to four
        // lines and the numbers stay two lines above it.
        if (!string.IsNullOrEmpty(box.Label))
        {
            var reading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            reading.Children.Add(new TextBlock
            {
                Text = Strings.Get("Look.ReadTag"),
                FontSize = 10.5,
                Foreground = ThemeBrush.Of("TextTertiary"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 6, 0),
            });

            var text = new TextBlock
            {
                Text = box.Label,
                FontSize = 11.5,
                // "这是程序读出来的内容"——由**等宽**说，不由绿色说：
                // 绿在这套界面已经表示"成功"，再借一次就是两种含义共用一个色。
                Classes = { "Code" },
                TextWrapping = TextWrapping.Wrap,
            };

            Grid.SetColumn(text, 1);
            reading.Children.Add(text);

            words.Children.Add(new Border
            {
                Background = ThemeBrush.Of("SurfaceSunken"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4),
                Margin = new Thickness(0, 3, 0, 0),
                Child = reading,
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
                Foreground = ThemeBrush.Of("Warning"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(words, 2);
        grid.Children.Add(words);

        var score = new TextBlock
        {
            Text = box.Role == LookRole.Area ? string.Empty : Score(box.Match.Score, _look.Kind),
            FontSize = 12,
            // 分数是附注级读数，与详情同档；要区分靠位置和标签，不靠颜色。
            Foreground = ThemeBrush.Of("TextSecondary"),
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

    /// <summary>
    /// Fills the screen, or leaves it: a reference picture and a screenful of hits are read at their
    /// own size. No button of its own — the title bar's maximize is the button, and F11 does it from
    /// the keyboard.
    /// </summary>
    private void ToggleFullScreen()
        => WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;

    /// <summary>Whether the event came from inside one part of the window.</summary>
    private static bool Inside(object? source, Visual part)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (ReferenceEquals(node, part))
            {
                return true;
            }
        }

        return false;
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, true), true);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom(LookGeometry.Step(_zoom, false), true);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Zoom(Fit(), false);

    /// <summary>
    /// A press on the picture starts a drag, or — on the second click — goes between its real size
    /// and the size that fits. The drag is taken here rather than left to the scroll viewer so that
    /// it works the same at every zoom, including the zoom where the picture fits and there is
    /// nothing to scroll yet.
    /// </summary>
    private void OnPicturePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            ToggleRealSize();
            return;
        }

        if (_board is null || _scroll is null
            || !e.GetCurrentPoint(_board).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _panning = true;
        _panFrom = e.GetPosition(_scroll);
        _panOrigin = _scroll.Offset;
        e.Pointer.Capture(_board);
        e.Handled = true;
    }

    private void OnPictureMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning || _scroll is null)
        {
            return;
        }

        // What the pointer moved by is what the picture moves by, the other way round: dragging to
        // the left brings the part that was off the right edge into view.
        _scroll.Offset = _panOrigin - (e.GetPosition(_scroll) - _panFrom);
        e.Handled = true;
    }

    private void OnPictureReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// The wheel zooms, and zooms about the pointer: whatever was under it stays under it, which is
    /// what makes a mark worth aiming at. Scrolling a picture that fills the window would otherwise
    /// move the one thing the person is pointing at.
    /// </summary>
    private void OnPictureWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_scroll is null || e.Delta.Y == 0)
        {
            return;
        }

        // Only over the picture: the mark list beside it scrolls with the same wheel.
        if (!Inside(e.Source, _scroll))
        {
            return;
        }

        e.Handled = true;
        ZoomAt(LookGeometry.Step(_zoom, e.Delta.Y > 0), e.GetPosition(_scroll));
    }

    /// <summary>
    /// Zooms while keeping one place on the picture where it is. The place is read before the
    /// picture is redrawn: after it, the same screen point is a different part of the picture.
    /// </summary>
    private void ZoomAt(double zoom, Point at)
    {
        var was = _zoom;
        if (_scroll is null || was <= 0)
        {
            Zoom(zoom, false);
            return;
        }

        var before = _scroll.Offset + new Vector(at.X, at.Y);
        Zoom(zoom, false);

        // The picture has just been given a new size, and the viewer answers a scroll position
        // against the size it knew before the layout that comes next: without this pass the offset
        // below is quietly cut down to the old range, and the place under the pointer drifts.
        _scroll.UpdateLayout();

        var ratio = _zoom / was;
        _scroll.Offset = new Vector(before.X * ratio, before.Y * ratio) - new Vector(at.X, at.Y);
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
        // 自绘：**这是"图还没画上去时的画布"**，不是界面色。别把它当"深色背景令牌"换掉——
        // 换掉之后图一出来，底下会透出别的颜色。
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

}
