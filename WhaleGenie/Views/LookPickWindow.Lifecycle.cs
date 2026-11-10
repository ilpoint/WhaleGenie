using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Opening the window: what it is handed, the two ways it is asked for a place, and the confirm button.
public partial class LookPickWindow : AppWindow
{
    public LookPickWindow()
    {
        InitializeComponent();
        _frame = ImageFrame.Empty;
        _origin = default;
        _boxes = [];
        _variables = [];
        _mode = LookPickMode.Offset;
    }

    /// <param name="look">What the step saw: the marks that go on the picture, and which was used.</param>
    /// <param name="area">
    /// The picture to pick on — the whole screen, or the whole of the window the step reads. It is
    /// deliberately larger than the part the step searched: a click often lands just outside what
    /// was matched, and an offset the picker cannot reach is one the user has to work out by hand.
    /// </param>
    /// <param name="origin">Where that picture starts on the screen.</param>
    /// <param name="mode">What a click means: one place, or one more colour of a pattern.</param>
    /// <param name="centre">The middle of the thing the step went with, which offsets count from.</param>
    /// <param name="variables">The names a typed line may use, offered by the box that edits it.</param>
    /// <param name="existing">
    /// The pattern the step already holds, one line each. Starting from it is what makes this window
    /// the only place the pattern is edited: what is on the list is the step's own content, not a
    /// fresh list that would be added to it.
    /// </param>
    public LookPickWindow(StepLook look, ImageFrame area, ScreenPoint origin, LookPickMode mode,
        ScreenPoint centre, IReadOnlyList<VariableChoice>? variables = null,
        IReadOnlyList<string>? existing = null)
    {
        InitializeComponent();
        WindowChrome.Attach(this);

        _frame = area;
        _origin = origin;
        _boxes = look.Boxes;
        _chosen = look.ChosenIndex;
        _mode = mode;
        _centre = centre;
        _variables = variables ?? [];

        foreach (var line in existing ?? [])
        {
            if (!ColourPointLine.TryParse(line, out var entry) || Contains(entry))
            {
                continue;
            }

            _added.Add(entry);
            _addedAt.Add(Offsets(entry, out var dx, out var dy)
                ? new ScreenPoint(_centre.X + dx, _centre.Y + dy)
                : _centre);
        }

        _bitmap = area.IsEmpty ? null : ImageAssets.ToBitmap(area);
        _zoom = area.IsEmpty
            ? 1
            : Math.Clamp(LookGeometry.Fit(area.Width, area.Height,
                RoomWidth, RoomHeight), LookGeometry.SmallestZoom, MostZoom);

        Title = Strings.Get(mode == LookPickMode.Colours
            ? "LookPick.ColoursTitle"
            : "LookPick.OffsetTitle");

        _board = this.FindControl<Canvas>("Board");
        _readout = this.FindControl<TextBlock>("Readout");
        _entries = this.FindControl<StackPanel>("Entries");
        _confirm = this.FindControl<Button>("Confirm");
        _loupeLayer = this.FindControl<Canvas>("LoupeLayer");
        _loupeLayer?.Children.Add(_magnifier);

        if (this.FindControl<TextBlock>("HintText") is { } hint)
        {
            hint.Text = Strings.Get(mode == LookPickMode.Colours
                ? "LookPick.ColoursHint"
                : "LookPick.OffsetHint");
        }

        // The list of what has been added is only a list in one of the two modes; in the other there
        // is exactly one point and it is already written under the picture.
        if (this.FindControl<Border>("EntriesPanel") is { } panel)
        {
            panel.IsVisible = mode == LookPickMode.Colours;

            // The empty part of the list is a place to click to put a line back: without it the only
            // way out of "picked" was clicking the same line again, which is not something anybody
            // would guess.
            panel.PointerPressed += (_, e) =>
            {
                if (!OnRow(e.Source))
                {
                    ClearSelection();
                }
            };
        }

        // The whole screen at the size a window has for it is small, and an offset is often one or
        // two pixels out: the wheel zooms about the pointer and the left button moves the picture,
        // the same two gestures the window that shows what a step saw answers to.
        if (_board is not null)
        {
            _board.Cursor = new Cursor(StandardCursorType.Hand);
            _board.PointerMoved += OnPanMoved;
            _board.PointerReleased += OnPanReleased;
            _board.PointerExited += (_, _) => _magnifier.Hide();
        }

        // Tunnelled: the scroll viewer answers the wheel itself and marks it handled, so a handler
        // beside it only ever sees the wheel when the picture has nowhere to scroll — the wheel then
        // meant two different things depending on the zoom. Zooming has to win either way.
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Draw();
        RebuildRows();
        UpdateConfirm();
    }

    /// <summary>
    /// 确定只在有东西可交的时候可点：一处也没有时没有内容可写回；有内容时必须可点——
    /// 包括「只给已有的一处加了个备选色」，那不是新加一条，但确实改了这一步的内容。
    /// </summary>
    private void UpdateConfirm()
    {
        if (_confirm is not null)
        {
            _confirm.IsEnabled = _added.Count > 0;
        }
    }

    /// <summary>
    /// Shows the picture the step saw and hands back the place clicked, as an offset from the middle
    /// of what it found; null when the user changed their mind.
    /// </summary>
    public static async Task<ScreenPoint?> PickOffsetAsync(Window owner, StepLook look,
        ImageFrame area, ScreenPoint origin, ScreenPoint centre,
        IReadOnlyList<VariableChoice>? variables = null)
        => await new LookPickWindow(look, area, origin, LookPickMode.Offset, centre, variables)
            .ShowDialogOver<ScreenPoint?>(owner);

    /// <summary>
    /// The same picture, picked over and over: every click adds the offset and the colour there,
    /// written the way the pattern field reads them. Null when nothing was added or the user
    /// changed their mind.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> PickColoursAsync(Window owner, StepLook look,
        ImageFrame area, ScreenPoint origin, ScreenPoint centre,
        IReadOnlyList<VariableChoice>? variables = null, IReadOnlyList<string>? existing = null)
        => await new LookPickWindow(look, area, origin, LookPickMode.Colours, centre, variables,
                existing)
            .ShowDialogOver<IReadOnlyList<string>?>(owner);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _bitmap?.Dispose();
        _magnifier.Release();
    }

    // ------------------------------------------------------------------- picture

    /// <summary>How far the picture is drawn from its own pixels, as the canvas needs them.</summary>
    internal double ZoomLevel => _zoom;

    /// <summary>True while the picture is being dragged, and where the drag started.</summary>
    private bool _panning;
    private Point _panFrom;
    private Vector _panOrigin;

    /// <summary>The line whose marker is being dragged, or -1 when none is.</summary>
    private int _dragging = -1;

    /// <summary>What has been added so far, in the order it was added.</summary>
    internal IReadOnlyList<string> Added => _added;

    /// <summary>Where the dot of each line sits, in the same order as <see cref="Added"/>.</summary>
    internal IReadOnlyList<ScreenPoint> MarkerPlaces => _addedAt;
}
