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
using WhaleGenie.Assets;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Opening the window: the three ways it is made, what it fills in, and how it is closed.
public partial class LookWindow : AppWindow
{
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
        _frame = look.Frame.IsEmpty ? null : BitmapImages.ToBitmap(look.Frame);
        _needle = look.Needle is { IsEmpty: false } needle ? BitmapImages.ToBitmap(needle) : null;

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

        // A picture taken from the clipboard has nothing marked on it: nothing was searched, so the
        // column that lists the marks would stand there empty. The picture takes the whole width.
        if (look.Kind == LookKind.Clipboard
            && this.FindControl<Grid>("BoardRow") is { } boardRow
            && this.FindControl<Border>("MarksPanel") is { } marksPanel)
        {
            marksPanel.IsVisible = false;
            boardRow.ColumnDefinitions[1].Width = new GridLength(0);
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
}
