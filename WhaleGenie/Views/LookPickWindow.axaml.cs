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

/// <summary>What a pick on the look's own picture is for.</summary>
public enum LookPickMode
{
    /// <summary>One point, handed back as an offset from the middle of what the step found.</summary>
    Offset,

    /// <summary>Any number of points, each handed back as an offset and the colour there.</summary>
    Colours,
}

/// <summary>
/// Picks points on the picture the step itself was looking at, rather than on the reference picture
/// it was looking for. The difference is what the two questions need: a template is a stamp that
/// says nothing about where on the screen it turned up, while the look has the whole screen on it —
/// so a place beside the thing found, and a colour a few pixels away from it, can both be aimed at
/// by eye.
/// </summary>
public partial class LookPickWindow : AppWindow
{
    /// <summary>The room the picture is fitted into, and how far it may be blown up.</summary>
    private const double RoomWidth = 690;
    private const double RoomHeight = 430;
    private const double MostZoom = 4;

    /// <summary>How small a mark may be drawn: a colour hit is one pixel of a whole screen.</summary>
    private const double SmallestMark = 4;

    private readonly ImageFrame _frame;
    private readonly ScreenPoint _origin;
    private readonly IReadOnlyList<LookBox> _boxes;
    private readonly int _chosen;
    private readonly LookPickMode _mode;
    private readonly ScreenPoint _centre;
    private readonly IReadOnlyList<VariableChoice> _variables;
    private readonly WriteableBitmap? _bitmap;
    private double _zoom;

    private readonly List<string> _added = [];
    private readonly List<ScreenPoint> _addedAt = [];

    private Canvas? _board;
    private TextBlock? _readout;
    private StackPanel? _entries;
    private Button? _confirm;
    private Canvas? _loupeLayer;

    /// <summary>放大镜：整块画面缩到窗口里之后，一个像素的偏移靠眼睛是分不出来的。</summary>
    private readonly Magnifier _magnifier = new();

    /// <summary>The row of the list that is being worked on, or -1 when none is.</summary>
    private int _selected = -1;

    private ScreenPoint? _picked;

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

    private void Draw()
    {
        if (_board is null)
        {
            return;
        }

        var width = Math.Max(1, _frame.Width * ZoomLevel);
        var height = Math.Max(1, _frame.Height * ZoomLevel);
        _board.Width = width;
        _board.Height = height;
        _board.Children.Clear();

        if (_bitmap is not null)
        {
            var picture = new Image
            {
                Source = _bitmap,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
            };

            RenderOptions.SetBitmapInterpolationMode(picture, ZoomLevel >= 1
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality);

            Canvas.SetLeft(picture, 0);
            Canvas.SetTop(picture, 0);
            _board.Children.Add(picture);
        }

        for (var index = 0; index < _boxes.Count; index++)
        {
            var box = _boxes[index];
            var place = LookGeometry.Place(_origin, box.Match.Location, box.Match.Size, ZoomLevel);
            var visible = LookGeometry.Visible(place, SmallestMark);
            var mark = Mark(box, visible, index + 1 == _chosen);

            Canvas.SetLeft(mark, visible.X);
            Canvas.SetTop(mark, visible.Y);
            _board.Children.Add(mark);
        }

        // What offsets count from: the pixel the step went with, marked in orange. The ring grows
        // with the zoom so that at 800% it still hugs that one pixel instead of spilling into the
        // ones beside it — a ring bigger than the cell it names is read as naming the next cell.
        var centre = ToBoardCentre(_centre);
        var ringSize = Math.Clamp((ZoomLevel * 1.2) + 4, 11, 22);
        var ring = new Border
        {
            Width = ringSize,
            Height = ringSize,
            // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
            BorderBrush = new SolidColorBrush(Color.Parse("#E0A030")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(ringSize / 2),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(ring, centre.X - (ringSize / 2));
        Canvas.SetTop(ring, centre.Y - (ringSize / 2));
        _board.Children.Add(ring);

        for (var index = 0; index < _addedAt.Count; index++)
        {
            var at = ToBoardCentre(_addedAt[index]);
            var dotSize = Math.Clamp((ZoomLevel * 0.8) + 4, 8, 15);
            var dot = new Border
            {
                Width = dotSize,
                Height = dotSize,
                // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
                Background = new SolidColorBrush(Color.Parse(index == _selected ? "#FF3B30" : "#3FBF5F")),
                CornerRadius = new CornerRadius(dotSize / 2),
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(dot, at.X - (dotSize / 2));
            Canvas.SetTop(dot, at.Y - (dotSize / 2));
            _board.Children.Add(dot);

            // 行号写在圆点旁边：右侧那一列靠编号与图上的点对应，靠数一串同色的圆点是数不清的。
            var number = new TextBlock
            {
                Name = "MarkerNumber",
                Text = (index + 1).ToString(CultureInfo.CurrentCulture),
                FontSize = 10.5,
                Foreground = Brushes.White,
                // 自绘：浮在截图上的深色薄板，令牌表里没有这一档。
                Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 20)),
                Padding = new Thickness(3, 0, 3, 1),
                IsHitTestVisible = false,
            };

            var above = at.Y - 17;
            Canvas.SetLeft(number, at.X + 5);
            Canvas.SetTop(number, above >= 0 ? above : at.Y + 4);
            _board.Children.Add(number);
        }

        if (_picked is { } picked)
        {
            var at = ToBoardCentre(picked);
            var cross = new Border
            {
                Width = 15,
                Height = 15,
                BorderBrush = new SolidColorBrush(Color.Parse("#FF3B30")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Child = new Border
                {
                    Width = 3,
                    Height = 3,
                    Background = new SolidColorBrush(Color.Parse("#FF3B30")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(cross, at.X - 7);
            Canvas.SetTop(cross, at.Y - 7);
            _board.Children.Add(cross);
        }
    }

    /// <summary>One mark of the look on the picture: the chosen one heavier than its neighbours.</summary>
    private static Border Mark(LookBox box, LookRect rect, bool chosen) => new()
    {
        Width = rect.Width,
        Height = rect.Height,
        BorderThickness = new Thickness(chosen ? 3 : 1.5),
        BorderBrush = ThemeBrush.Of(box.Role switch
        {
            LookRole.Hit => "Success",
            LookRole.Candidate => "Warning",
            LookRole.Target => "Danger",
            _ => "TextTertiary",
        }),
        Background = Brushes.Transparent,
        CornerRadius = new CornerRadius(1),
        IsHitTestVisible = false,
    };

    /// <summary>
    /// 一个像素在画布上的中心。指针停在哪个像素、偏移算到哪个像素，说的都是这一格，而不是它的左上角：
    /// 画标记时补上半格，否则放大到 800% 时整枚标记会偏半格，看着就是「位置不对」。
    /// </summary>
    private Point ToBoardCentre(ScreenPoint point)
        => new((point.X - _origin.X + 0.5) * ZoomLevel,
               (point.Y - _origin.Y + 0.5) * ZoomLevel);

    /// <summary>
    /// 把放大镜摆到指针旁边，读的是这张图自己的像素：图上的一像素落进宏里就是一个偏移，
    /// 所以在图上瞄的时候要看得见它落在哪一格。
    /// </summary>
    private void Aim(PointerEventArgs e)
    {
        if (_board is null || _loupeLayer is null || _frame.IsEmpty)
        {
            return;
        }

        var place = e.GetPosition(_board);
        var local = LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y);
        var at = e.GetPosition(_loupeLayer);
        var room = _loupeLayer.Bounds;
        _magnifier.Follow(_frame, local.X, local.Y);
        _magnifier.PlaceNear(at.X, at.Y,
            room.Width > 0 ? room.Width : Width,
            room.Height > 0 ? room.Height : Height);
    }

    // ------------------------------------------------------------------ picking

    /// <summary>
    /// A click on the picture is a place on the screen: the picture's own corner is where the look
    /// started, so a point on it plus that corner is the place the step would have worked at.
    /// </summary>
    private void OnPickPoint(object? sender, PointerPressedEventArgs e)
    {
        if (_board is null || _frame.IsEmpty)
        {
            return;
        }

        var place = e.GetPosition(_board);

        switch (e.GetCurrentPoint(_board).Properties.PointerUpdateKind)
        {
            // 右键属于图上的点：它给出改这一条、删这一条。空白处按右键不做什么——画面上的
            // 右键若是"又加一条"，想改一条时就会先多出一条。
            case PointerUpdateKind.RightButtonPressed:
                if (MenuFor(place) is { } menu)
                {
                    e.Handled = true;
                    menu.Open(_board);
                }

                return;

            default:
                break;
        }

        // 只有左键是「点一处」。滚轮键以前也新增一条，撤掉了：中键常被当成拖动画面的手势，
        // 手一滑就多出一处，而多出来的那一处不点开列表是看不出来的。
        if (!e.GetCurrentPoint(_board).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 按住 Shift 点一下，是给一处再加一种颜色，不是新加一处：同一个按钮换一套配色长什么样，
        // 只能靠另一个像素上的颜色说得清。
        if (_mode == LookPickMode.Colours && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            AddAlternativeAt(place);
            return;
        }

        // A press on one of the dots takes hold of that line: dragging the marker is how a point that
        // landed a pixel or two off is put right, and it is the gesture the picture itself invites.
        if (_mode == LookPickMode.Colours && MarkerAt(place) is { } marker)
        {
            _dragging = marker;
            _selected = marker;
            _picked = _addedAt[marker];
            e.Pointer.Capture(_board);
            RebuildRows();
            Draw();
            e.Handled = true;
            return;
        }

        // Dragging a picture that fits the window has nothing to move, and the press is a pick; one
        // that does not fit is moved, and a press is not a pick until the button comes back up.
        if (this.FindControl<ScrollViewer>("Scroll") is { } room && Moves(room))
        {
            _panning = true;
            _panFrom = e.GetPosition(room);
            _panOrigin = room.Offset;
            e.Pointer.Capture(_board);
            e.Handled = true;
            return;
        }

        Pick(LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y));
    }

    /// <summary>
    /// The menu for the dot under the pointer, or null when there is none there. It is built for the
    /// dot rather than for the picture: with nothing under the pointer there is nothing to edit.
    /// </summary>
    internal ContextMenu? MenuFor(Point place)
    {
        if (_mode != LookPickMode.Colours || MarkerAt(place) is not { } index)
        {
            return null;
        }

        return Menu(index);
    }

    /// <summary>The line whose dot — or whose number, which belongs to the same dot — is under the pointer.</summary>
    private int? MarkerAt(Point place)
    {
        for (var index = 0; index < _addedAt.Count; index++)
        {
            var at = ToBoardCentre(_addedAt[index]);
            if (Math.Abs(at.X - place.X) <= 8 && Math.Abs(at.Y - place.Y) <= 8)
            {
                return index;
            }

            // 序号牌也算这一处：指它、右键它，说的都是同一条。
            if (place.X >= at.X + 2 && place.X <= at.X + 22
                && place.Y >= at.Y - 19 && place.Y <= at.Y + 3)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Drags the line being held to wherever the pointer is now: the dot follows, the numbers of that
    /// line follow with it, and the row beside the picture is rewritten as it goes.
    /// </summary>
    private void DragTo(Point place)
    {
        if (_dragging < 0 || _dragging >= _added.Count)
        {
            return;
        }

        var local = LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y);
        var point = PointAt(local);
        var offset = OffsetAt(point);
        var hex = _frame[local.X, local.Y].ToHex();

        // 拖的是「这一处」，不是「这一行的颜色」：备选色和按行写下的容差跟着一起走。
        _added[_dragging] = ColourPointLine.WithCore(_added[_dragging],
            offset.X.ToString(CultureInfo.InvariantCulture),
            offset.Y.ToString(CultureInfo.InvariantCulture), hex);
        _addedAt[_dragging] = point;
        _picked = point;

        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.Moved", offset.X, offset.Y));
    }

    /// <summary>Drops the line that was picked out, which is what clicking the empty part of the list does.</summary>
    internal void ClearSelection()
    {
        if (_selected < 0)
        {
            return;
        }

        _selected = -1;
        _picked = null;
        RebuildRows();
        Draw();
        Say(Strings.Get("LookPick.NothingPicked"));
    }

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

    /// <summary>Whether a press landed on one of the rows rather than on the space around them.</summary>
    private static bool OnRow(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (node is Border { Tag: int })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the picture is bigger than the room it is in, and so has somewhere to move.</summary>
    private static bool Moves(ScrollViewer room)
        => room.Extent.Width > room.Viewport.Width + 0.5 || room.Extent.Height > room.Viewport.Height + 0.5;

    private void OnPanMoved(object? sender, PointerEventArgs e)
    {
        Aim(e);

        if (_dragging >= 0 && _board is not null)
        {
            DragTo(e.GetPosition(_board));
            e.Handled = true;
            return;
        }

        if (!_panning || this.FindControl<ScrollViewer>("Scroll") is not { } room)
        {
            return;
        }

        room.Offset = _panOrigin - (e.GetPosition(room) - _panFrom);
        e.Handled = true;
    }

    /// <summary>
    /// The button coming back up ends a drag, and a drag that did not go anywhere was a click: that
    /// is what picks the spot, so one gesture on a zoomed-in picture can both move it and pick on it.
    /// </summary>
    private void OnPanReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging >= 0)
        {
            _dragging = -1;
            e.Pointer.Capture(null);
            e.Handled = true;
            RebuildRows();
            return;
        }

        if (!_panning || _board is not { } board)
        {
            return;
        }

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        // A drag that left the picture where it was is a click on it, and a click is a pick: moving
        // the view and aiming at it are the same gesture, told apart by whether it went anywhere.
        if (this.FindControl<ScrollViewer>("Scroll") is { } room
            && (room.Offset - _panOrigin).Length < 2)
        {
            var place = e.GetPosition(board);
            Pick(LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y));
        }
    }

    /// <summary>
    /// Zooms about the pointer. The whole screen is a lot of picture for one window, so the wheel is
    /// how a particular button is brought up to a size it can be aimed at.
    /// </summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (this.FindControl<ScrollViewer>("Scroll") is not { } room || e.Delta.Y == 0)
        {
            return;
        }

        // Only over the picture: the list beside it scrolls with the same wheel, and turning the
        // picture into nothing but a zoom would take that away.
        if (!Inside(e.Source, room))
        {
            return;
        }

        e.Handled = true;
        var at = e.GetPosition(room);
        var was = _zoom;
        var next = Math.Clamp(LookGeometry.Step(_zoom, e.Delta.Y > 0),
            LookGeometry.SmallestZoom, LookGeometry.LargestZoom);
        if (Math.Abs(next - was) < 0.0001)
        {
            return;
        }

        var before = room.Offset + new Vector(at.X, at.Y);
        _zoom = next;
        Draw();
        room.UpdateLayout();

        var ratio = _zoom / was;
        room.Offset = new Vector(before.X * ratio, before.Y * ratio) - new Vector(at.X, at.Y);
    }

    /// <summary>
    /// A place on the picture, in the picture's own pixels, taken as the answer: one place in one
    /// mode, one more colour of the pattern in the other.
    /// </summary>
    internal void Pick(ScreenPoint local)
    {
        var point = PointAt(local);

        if (_mode == LookPickMode.Offset)
        {
            _picked = point;
            Draw();

            var offset = OffsetAt(point);
            Say(Strings.Format("LookPick.OffsetAt", offset.X, offset.Y));
            if (_confirm is not null)
            {
                _confirm.IsEnabled = true;
            }

            return;
        }

        if (_selected >= 0 && _selected < _added.Count)
        {
            MoveColour(_selected, point, local);
            return;
        }

        AddColour(point, local);
    }

    /// <summary>Where a place on the picture sits on the screen.</summary>
    internal ScreenPoint PointAt(ScreenPoint local)
        => new(_origin.X + local.X, _origin.Y + local.Y);

    /// <summary>
    /// Adds the offset and the colour at one place, once. The colour is read out of the picture the
    /// step was given rather than off the screen now: what is being described is the pattern as it
    /// was when the step looked, and the screen has moved on since.
    /// </summary>
    private void AddColour(ScreenPoint point, ScreenPoint local)
    {
        var offset = OffsetAt(point);
        var colour = _frame[local.X, local.Y].ToHex();
        var entry = string.Create(CultureInfo.InvariantCulture, $"{offset.X},{offset.Y},{colour}");

        if (Contains(entry))
        {
            Say(Strings.Get("LookPick.ColoursRepeat"));
            return;
        }

        _added.Add(entry);
        _addedAt.Add(point);
        _picked = null;

        // Adding leaves nothing selected: a click after this one is another colour, which is what
        // "one click, one colour" means. Moving is asked for by picking a line first.
        _selected = -1;
        Draw();
        RebuildRows();

        Say(Strings.Format("LookPick.ColoursAdded", entry, _added.Count));
        UpdateConfirm();
    }

    /// <summary>
    /// Adds the colour under the pointer to the place the pointer names — the dot it is on, or the
    /// line picked out in the list — rather than starting another place.
    /// </summary>
    private void AddAlternativeAt(Point place)
    {
        var index = MarkerAt(place) ?? _selected;
        if (index < 0)
        {
            Say(Strings.Get("LookPick.AlternativeNeedPoint"));
            return;
        }

        var local = LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y);
        AddAlternative(index, _frame[local.X, local.Y].ToHex());
    }

    /// <summary>
    /// Adds one more colour this place may show. The place stays where it is: what is being said is
    /// "this spot may look like either of these", which is a different thing from another spot. The
    /// engine reads the colours one after another with a bar between them, and is satisfied by any
    /// one of them.
    /// </summary>
    internal void AddAlternative(int index, string colour)
    {
        if (index < 0 || index >= _added.Count)
        {
            return;
        }

        var colours = ColourPointLine.Colours(_added[index]);
        if (colours.Any(one => string.Equals(one, colour, StringComparison.OrdinalIgnoreCase)))
        {
            Say(Strings.Get("LookPick.AlternativeRepeat"));
            return;
        }

        if (colours.Count >= ColourPointLine.MostColours)
        {
            Say(Strings.Format("LookPick.AlternativeFull", ColourPointLine.MostColours));
            return;
        }

        // 选中状态不动：加完颜色紧接着点画面，那一下仍然是「再加一处」，而不是把这一处挪走。
        _added[index] = ColourPointLine.WithColours(_added[index], [.. colours, colour]);
        RebuildRows();
        Draw();
        UpdateConfirm();
        Say(Strings.Format("LookPick.AlternativeAdded", colour, colours.Count + 1));
    }

    /// <summary>
    /// 从屏幕上另取一种颜色加给这一处：同一个按钮在另一套配色下的样子，这张图上没有，
    /// 只能在别处取。打开时这一窗会让开，和别的取色一样。
    /// </summary>
    private async void OnAddAlternative(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int index } || index < 0 || index >= _added.Count)
        {
            return;
        }

        var picked = await ColorPickerWindow.PickAsync(this,
            SharedDevices.Current.Screen, SharedDevices.Current.Input, SharedDevices.Current.Hook);
        if (picked is { } pick)
        {
            AddAlternative(index, pick.Colour.ToHex());
        }
    }

    /// <summary>
    /// The list of lines as they stand, drawn again whenever one of them changes: adding, moving,
    /// editing and removing all end here, so what is on screen is always the list itself.
    /// </summary>
    private void RebuildRows()
    {
        if (_entries is null)
        {
            return;
        }

        if (this.FindControl<TextBlock>("CountText") is { } count)
        {
            count.Text = _added.Count.ToString(CultureInfo.CurrentCulture);
        }

        _entries.Children.Clear();

        // 空列表也说出它是什么：一块空白的薄板看起来只是一个空框，写一行字才像"还没有加入"。
        if (_added.Count == 0)
        {
            _entries.Children.Add(new TextBlock
            {
                Text = Strings.Get("LookPick.NothingAdded"),
                Classes = { "Hint" },
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 3, 2, 3),
            });

            return;
        }

        for (var index = 0; index < _added.Count; index++)
        {
            _entries.Children.Add(Row(index));
        }
    }

    /// <summary>
    /// One line of the list: where the place is, which colours it may be, and whether it is strict
    /// about them. Right-clicking it edits it, gives it another colour or removes it; left-clicking
    /// picks it out, which is how the next click on the picture moves it — that is how a marker that
    /// landed a pixel off is put right without retyping its numbers.
    /// </summary>
    private Border Row(int index)
    {
        var line = _added[index];
        var fields = ColourPointText.Split(line);
        var offsets = fields.Count >= 2 ? $"{fields[0]}, {fields[1]}" : line;

        // 一行就是一处：序号、颜色（几种并排）、偏移，右边再写它是不是「不能是」、容差多少。
        var colours = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };

        foreach (var colour in ColourPointLine.Colours(line))
        {
            colours.Children.Add(Swatch(colour));
        }

        var number = new TextBlock
        {
            Text = (index + 1).ToString(CultureInfo.CurrentCulture) + ".",
            FontSize = 12,
            MinWidth = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush.Of("TextTertiary"),
        };

        var places = new TextBlock
        {
            Text = offsets,
            FontSize = 11.5,
            Classes = { "Mono" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush.Of("TextSecondary"),
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 7,
        };

        grid.Children.Add(number);
        Grid.SetColumn(colours, 1);
        grid.Children.Add(colours);
        Grid.SetColumn(places, 2);
        grid.Children.Add(places);

        var marks = Marks(line);
        if (marks.Length > 0)
        {
            var badge = new TextBlock
            {
                Text = marks,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush.Of("TextTertiary"),
            };

            Grid.SetColumn(badge, 3);
            grid.Children.Add(badge);
        }

        var row = new Border
        {
            // 每条自带一圈细边、底板比外框亮一档：只把几行字排在一起，看起来不像一张能逐条点选的列表。
            Background = ThemeBrush.Of(index == _selected ? "AccentSurface" : "SurfaceCard"),
            BorderBrush = ThemeBrush.Of(index == _selected ? "AccentText" : "BorderControl"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 5),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,

            // 整条写在提示里：色块省掉了字的长度，但把这一行到底存了什么写全仍然有用。
            Tag = index,
        };

        ToolTip.SetTip(row, line);
        row.ContextMenu = Menu(index);

        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                PickLine(index);
            }
        };

        return row;
    }

    /// <summary>One colour of a place, or what was written there when it is not a colour.</summary>
    private static Control Swatch(string colour)
    {
        if (ColourPointLine.TryColourText(colour, out var hex))
        {
            var swatch = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = ThemeBrush.Of("BorderControl"),
                BorderThickness = new Thickness(1),
            };

            ToolTip.SetTip(swatch, hex);
            return swatch;
        }

        // 写的是算式或变量：画不出颜色，就把写的东西摆出来，免得看起来像一格空的。
        return new TextBlock
        {
            Text = colour,
            FontSize = 11,
            Classes = { "Mono" },
            Foreground = ThemeBrush.Of("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// The two things the table says about a place that the picture cannot show: that it must be
    /// none of those colours, and that it allows a tolerance of its own.
    /// </summary>
    private static string Marks(string line)
    {
        var marks = new List<string>();
        if (ColourPointLine.MustNot(line))
        {
            marks.Add("≠");
        }

        var tolerance = ColourPointLine.Tolerance(line);
        if (tolerance.Length > 0)
        {
            marks.Add($"±{tolerance}");
        }

        return string.Join(" ", marks);
    }

    /// <summary>The menu a place has, whether it was right-clicked on the picture or on the list.</summary>
    private ContextMenu Menu(int index)
    {
        var edit = new MenuItem { Header = Strings.Get("LookPick.Edit"), Tag = index };
        edit.Click += OnEditEntry;
        var alternative = new MenuItem { Header = Strings.Get("LookPick.AddAlternative"), Tag = index };
        alternative.Click += OnAddAlternative;
        var remove = new MenuItem { Header = Strings.Get("LookPick.Remove"), Tag = index };
        remove.Click += OnRemoveEntry;
        return new ContextMenu { Items = { edit, alternative, remove } };
    }

    /// <summary>Picks a line out, or puts it back if it was already picked out.</summary>
    internal void PickLine(int index)
    {
        if (_selected == index)
        {
            _selected = -1;
            _picked = null;
        }
        else
        {
            _selected = index;
            _picked = index < _addedAt.Count ? _addedAt[index] : null;
        }

        RebuildRows();
        Draw();
        Say(_selected < 0
            ? Strings.Get("LookPick.NothingPicked")
            : Strings.Get("LookPick.Picked"));
    }

    /// <summary>
    /// Changes one line by typing it. The marker follows the numbers when they still name a place:
    /// editing "12" into "20" is asking for the point twenty pixels along, not for a line whose
    /// marker stayed behind.
    /// </summary>
    private async void OnEditEntry(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int index } || index < 0 || index >= _added.Count)
        {
            return;
        }

        if (await PickEntryWindow.EditAsync(this, _added[index], _variables) is { } edited)
        {
            ApplyEntry(index, edited);
        }
    }

    /// <summary>Adds one line by typing it, for a point that is easier to state than to aim at.</summary>
    private async void OnAddEntry(object? sender, RoutedEventArgs e)
    {
        if (await PickEntryWindow.AddAsync(this, StartingLine(), _variables) is { } line)
        {
            AppendEntry(line);
        }
    }

    /// <summary>
    /// What a new line starts as: the point the offsets are counted from, with the colour that is
    /// there. Starting from nothing would leave two numbers and a colour to be filled in from memory.
    /// </summary>
    private string StartingLine()
    {
        var local = new ScreenPoint(_centre.X - _origin.X, _centre.Y - _origin.Y);
        var colour = local.X >= 0 && local.Y >= 0
            && local.X < _frame.Width && local.Y < _frame.Height
            ? _frame[local.X, local.Y].ToHex()
            : "#000000";
        return string.Create(CultureInfo.InvariantCulture, $"0,0,{colour}");
    }

    /// <summary>
    /// Adds a line that was typed. It goes at the end, where a click would have put it, and a place
    /// already in the list is not written twice.
    /// </summary>
    internal void AppendEntry(string edited)
    {
        if (!ColourPointLine.TryParse(edited, out var line))
        {
            return;
        }

        if (Contains(line))
        {
            Say(Strings.Get("LookPick.ColoursRepeat"));
            return;
        }

        _added.Add(line);
        _addedAt.Add(Offsets(line, out var dx, out var dy)
            ? new ScreenPoint(_centre.X + dx, _centre.Y + dy)
            : _centre);
        _selected = -1;
        _picked = null;
        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.ColoursAdded", line, _added.Count));
        UpdateConfirm();
    }

    /// <summary>
    /// Whether the list already holds this place, spaces and letter case aside. Only the offset and
    /// the colours are compared: whether a place is strict, or allows a tolerance of its own, does
    /// not make it another place.
    /// </summary>
    private bool Contains(string entry)
        => _added.Any(kept => string.Equals(
            Squeeze(ColourPointLine.Core(kept)),
            Squeeze(ColourPointLine.Core(entry)),
            StringComparison.OrdinalIgnoreCase));

    private static string Squeeze(string line) => line.Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// The two numbers at the front of a line, when they are numbers: a line that names its offset by
    /// a formula has no place on the picture to draw until the macro runs, and stays at the middle.
    /// </summary>
    private static bool Offsets(string line, out int dx, out int dy)
    {
        dx = 0;
        dy = 0;
        var parts = ColourPointText.Split(line);
        return parts.Count >= 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out dx)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out dy);
    }

    /// <summary>Puts a line that was typed in place of one that was clicked.</summary>
    internal void ApplyEntry(int index, string edited)
    {
        if (index < 0 || index >= _added.Count
            || !ColourPointLine.TryParse(edited, out var line))
        {
            return;
        }

        _added[index] = line;
        if (Offsets(line, out var dx, out var dy))
        {
            _addedAt[index] = new ScreenPoint(_centre.X + dx, _centre.Y + dy);
        }

        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.ColoursEdited", line));
    }

    private void OnRemoveEntry(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: int index })
        {
            RemoveEntry(index);
        }
    }

    /// <summary>Takes one line out of the list, and its marker with it.</summary>
    internal void RemoveEntry(int index)
    {
        if (index < 0 || index >= _added.Count)
        {
            return;
        }

        _added.RemoveAt(index);
        _addedAt.RemoveAt(index);
        if (_selected == index)
        {
            _selected = -1;
            _picked = null;
        }
        else if (_selected > index)
        {
            _selected--;
        }

        RebuildRows();
        Draw();
        UpdateConfirm();
        Say(Strings.Format("LookPick.ColoursLeft", _added.Count));
    }

    /// <summary>
    /// Moves the picked line to where the picture was clicked: the same arithmetic as adding one,
    /// only it replaces a line instead of appending one.
    /// </summary>
    private void MoveColour(int index, ScreenPoint point, ScreenPoint local)
    {
        var offset = OffsetAt(point);
        var colour = _frame[local.X, local.Y].ToHex();

        _added[index] = ColourPointLine.WithCore(_added[index],
            offset.X.ToString(CultureInfo.InvariantCulture),
            offset.Y.ToString(CultureInfo.InvariantCulture), colour);
        _addedAt[index] = point;
        _picked = point;
        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.Moved", offset.X, offset.Y));
    }

    /// <summary>How far a place is from the middle of what the step found, which is what it clicks by.</summary>
    internal ScreenPoint OffsetAt(ScreenPoint point)
        => new(point.X - _centre.X, point.Y - _centre.Y);

    private void Say(string text)
    {
        if (_readout is not null)
        {
            _readout.Text = text;
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (_mode == LookPickMode.Offset)
        {
            if (_picked is { } point)
            {
                Close(OffsetAt(point));
            }

            return;
        }

        if (_added.Count > 0)
        {
            Close(_added.ToArray());
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
