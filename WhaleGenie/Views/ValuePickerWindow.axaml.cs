using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The one window that fills a cell. It lays out what a formula is made of on one page — the signs,
/// the names, the functions — because composing means reaching for the next piece, and a page that
/// puts two of the three away is a page that has to be left and come back to.
/// </summary>
public partial class ValuePickerWindow : AppWindow
{
    private readonly ValuePickerViewModel _viewModel;

    public ValuePickerWindow()
        : this(new ValuePickerViewModel(null, [], namesOnly: false))
    {
    }

    public ValuePickerWindow(ValuePickerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // The bar is where a window is dragged by; without this it is a stripe that does nothing.
        WindowChrome.Attach(this);
        Title = viewModel.Title;


        // A cell that only holds a name does not need a workbench: the window is the list.
        if (viewModel.NamesOnly)
        {
            Width = 560;
            Height = 620;
            MinWidth = 480;
            MinHeight = 520;
        }

        viewModel.CloseRequested += result => Close(result);

        // Undo has to be keyed in front of everything else: a text box keeps its own history and
        // would swallow the keys before a bubbling handler ever sees them.
        AddHandler(KeyDownEvent, OnWindowTunnelKey, RoutingStrategies.Tunnel);

        // Every edit hands the band a new tree, so the piece the pointer was over no longer exists,
        // and the standing line has to be measured against the new tree as well.
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ValuePickerViewModel.Recipe))
            {
                Dispatcher.UIThread.Post(RefreshHover, DispatcherPriority.Loaded);
                Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
            }
            else if (args.PropertyName == nameof(ValuePickerViewModel.Caret))
            {
                Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
            }
        };

        BandScroll.SizeChanged += (_, _) =>
        {
            _viewModel.RefreshStacking(BandScroll.Viewport.Width);
            Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
        };

        Opened += (_, _) =>
        {
            _viewModel.RefreshStacking(BandScroll.Viewport.Width);
            if (StartCaret >= 0)
            {
                _viewModel.SetCaret(StartCaret);
            }

            Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
            if (!viewModel.CanCompute)
            {
                NameSearch.Focus();
                return;
            }

            // A cell that had a caret in it opens with the small field ready to type into; one that
            // did not keeps the variable search, which is where a formula usually starts.
            (StartCaret >= 0 ? ValueInput : VariableSearch).Focus();
        };
    }

    /// <summary>True when the window was opened from the formula side of a cell.</summary>
    public bool StartOnFormula { get; init; }

    /// <summary>Where the caret was in the cell, or -1 when nobody said.</summary>
    public int StartCaret { get; init; } = -1;

    /// <summary>
    /// Opens the window on a cell. A cell that only holds a name passes <paramref name="namesOnly"/>:
    /// a formula there is written down as the name itself, so offering one would be a promise the
    /// step cannot keep.
    /// </summary>
    public static async Task<string?> ShowFor(Window owner, string? seed,
        IReadOnlyList<VariableChoice>? variables, bool namesOnly = false, bool startOnFormula = false,
        string? target = null, int startCaret = -1, Action<MacroStep>? lookAtStep = null)
    {
        var viewModel = new ValuePickerViewModel(seed, variables, namesOnly, target);
        if (lookAtStep is not null)
        {
            viewModel.Jumped += lookAtStep;
        }

        var window = new ValuePickerWindow(viewModel)
        {
            StartOnFormula = startOnFormula,
            StartCaret = startCaret,
        };

        return await window.ShowDialog<string?>(owner);
    }

    /// <summary>
    /// Writes one piece at the insertion point. The window stays open either way: a formula is built
    /// a piece at a time, and closing on the first piece would make composing one a series of openings.
    /// </summary>
    private void Put(string token) => _viewModel.Place(token);

    /// <summary>
    /// The keys that belong to the window rather than to a field. Undo has to be keyed in front of
    /// everything else: a text box keeps its own history and would swallow the keys before a bubbling
    /// handler ever sees them. The arrows are keyed here for the same reason — the strip they move is
    /// inside a scroll viewer that would rather scroll — and they stand aside whenever a field or a
    /// list is the thing with focus.
    /// </summary>
    private void OnWindowTunnelKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _viewModel.Redo();
                e.Handled = true;
            }
            else if (e.Key == Key.Z)
            {
                _viewModel.Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y)
            {
                _viewModel.Redo();
                e.Handled = true;
            }

            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || KeysOwnedElsewhere(e))
        {
            return;
        }

        var moved = e.Key switch
        {
            Key.Left => _viewModel.StepCaret(-1),
            Key.Right => _viewModel.StepCaret(1),
            Key.Up => StepRow(down: false),
            Key.Down => StepRow(down: true),
            _ => false,
        };

        if (!moved)
        {
            return;
        }

        e.Handled = true;
        RevealCaret();
        Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
    }

    private SlotNode? _dragPiece;
    private Point _dragFrom;
    private bool _dragging;
    private SlotNode? _hovered;
    private Point _pointer;

    /// <summary>Where the one cross is drawn, in the overlay's coordinates, or null when it is away.</summary>
    private Rect? _removeRect;

    /// <summary>True when this press was on the cross, so the tap that follows must not choose a piece.</summary>
    private bool _pressedCross;

    /// <summary>The piece last hovered during a drag, and the preview drawn for it.</summary>
    private SlotNode? _dropTarget;
    private ValuePickerViewModel.DropPreview _preview;

    /// <summary>The insertion point a press asked for, applied only if the press stayed a click.</summary>
    private int? _pendingCaret;

    /// <summary>
    /// Shows the cross on the one piece the pointer is on. Done here rather than with a
    /// <c>:pointerover</c> style because Avalonia raises that pseudo-class on every ancestor of what
    /// the pointer hit — hovering one name would light the cross of each box it sits inside — and a
    /// drag clears it for the whole band the moment the pointer is captured.
    /// </summary>
    private void SetHover(SlotNode? node)
    {
        if (ReferenceEquals(_hovered, node))
        {
            return;
        }

        if (_hovered is { } previous)
        {
            previous.IsHovered = false;
        }

        _hovered = node;
        if (node is not null)
        {
            node.IsHovered = true;
        }

        PlaceRemove();
    }

    /// <summary>
    /// Puts the one cross over the piece it belongs to. The cross lives in its own layer: a cross
    /// drawn inside a token is covered by the token's own content and by the next token along, and
    /// <c>Panel.ZIndex</c> only orders siblings inside one panel.
    /// </summary>
    private void PlaceRemove()
    {
        var node = _hovered;
        if (node is null || !node.ShowsRemove || ControlFor(node) is not { } control
            || control.TranslatePoint(default, RemoveLayer) is not { } a
            || control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height),
                RemoveLayer) is not { } b)
        {
            HoverRemove.IsVisible = false;
            _removeRect = null;
            return;
        }

        // The cross, the rectangle that notices a press on it, and the visible part of the band all
        // live in the overlay's coordinates. Mixing these with the band's own coordinates made the
        // cross drift away from its piece after the strip scrolled. It sits just above the piece's
        // top-right corner rather than on it: a cross drawn over the corner would swallow a click
        // meant to put the insertion point after that piece.
        var visible = VisibleOverlay();
        var left = Math.Clamp(b.X - 16, visible.Left, Math.Max(visible.Left, visible.Right - 16));
        var top = Math.Clamp(a.Y - 14, visible.Top, Math.Max(visible.Top, visible.Bottom - 16));
        Canvas.SetLeft(HoverRemove, left);
        Canvas.SetTop(HoverRemove, top);
        HoverRemove.DataContext = node;
        HoverRemove.IsVisible = true;
        _removeRect = new Rect(left, top, 16, 16);
    }

    /// <summary>
    /// The token control standing for one piece. DataContext alone is not enough: a ContentControl,
    /// an ItemsControl, a WrapPanel and every ancestor of a call all inherit the same SlotNode, and
    /// picking the first of them puts the cross on a whole container instead of the piece.
    /// </summary>
    private Control? ControlFor(SlotNode node)
        => Band.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(one => one.IsEffectivelyVisible
                && one.Classes.Contains("SlotPiece")
                && ReferenceEquals(one.DataContext, node));

    /// <summary>
    /// The control a drop cursor belongs around. A run has no token of its own, so its last written
    /// piece is the one the ring describes, which is what <c>Resolve</c> decided for the seam.
    /// </summary>
    private Control AnchorControl(SlotNode? anchor, Control fallback)
    {
        if (anchor is SlotSequence run)
        {
            var last = run.Items.LastOrDefault(one => one.Length > 0);
            return last is not null ? ControlFor(last) ?? fallback : fallback;
        }

        return anchor is not null ? ControlFor(anchor) ?? fallback : fallback;
    }

    /// <summary>
    /// The token controls actually on screen, with the piece each stands for and its rectangle in
    /// the band's own coordinates. Only marked pieces count: every container above them inherits the
    /// same piece, and picking one of those would put a line around a whole call instead of a token.
    /// </summary>
    private List<(Control Control, SlotNode Node, Rect Bounds)> Pieces()
        => [.. Band.GetVisualDescendants()
            .OfType<Control>()
            .Where(one => one.IsEffectivelyVisible && one.Classes.Contains("SlotPiece")
                && one.DataContext is SlotNode)
            .Select(one => (Control: one, Node: (SlotNode)one.DataContext!,
                Bounds: one.TranslatePoint(default, Band) is { } at
                    ? new Rect(at.X, at.Y, one.Bounds.Width, one.Bounds.Height)
                    : default))
            .Where(one => one.Bounds.Width > 0 && one.Bounds.Height > 0)];

    /// <summary>
    /// Which character offset a press names. The left half of a piece puts the line before it, the
    /// right half after it, and a folded container reads top to bottom. In the seams between pieces
    /// the nearest edge wins, so a click on an empty stretch still has a place to write.
    /// </summary>
    private int? CaretFor(Point point)
    {
        var pieces = Pieces();
        if (pieces.Count == 0)
        {
            return _viewModel.Caret;
        }

        var hit = pieces.Where(one => one.Bounds.Contains(point))
            .OrderBy(one => one.Bounds.Width * one.Bounds.Height)
            .FirstOrDefault();

        if (hit.Control is not null)
        {
            var right = point.X > hit.Bounds.X + hit.Bounds.Width / 2;
            var lower = point.Y > hit.Bounds.Y + hit.Bounds.Height / 2;
            var after = ValuePickerViewModel.AfterFor(hit.Node, right, lower);
            return after ? hit.Node.Start + hit.Node.Length : hit.Node.Start;
        }

        return pieces
            .SelectMany(one => new[]
            {
                (At: one.Node.Start, Edge: one.Bounds.X, Top: one.Bounds.Top,
                    Bottom: one.Bounds.Bottom),
                (At: one.Node.Start + one.Node.Length, Edge: one.Bounds.Right,
                    Top: one.Bounds.Top, Bottom: one.Bounds.Bottom),
            })
            .OrderBy(edge => Math.Abs(edge.Edge - point.X)
                + (point.Y < edge.Top || point.Y > edge.Bottom ? 60 : 0))
            .Select(edge => (int?)edge.At)
            .FirstOrDefault() ?? _viewModel.Caret;
    }

    /// <summary>
    /// The edge the standing line is drawn at: the piece edge nearest the caret, and the two points it
    /// spans. The line and the arrow keys both read this, so what moves and what is drawn cannot end
    /// up at two different places.
    /// </summary>
    private (bool After, Point Top, Point Bottom)? CaretPlace()
    {
        var caret = _viewModel.Caret;
        var spot = Pieces()
            .SelectMany(one => new[]
            {
                (Control: one.Control, At: one.Node.Start, After: false, Extent: one.Bounds),
                (Control: one.Control, At: one.Node.Start + one.Node.Length, After: true,
                    Extent: one.Bounds),
            })
            .OrderBy(edge => Math.Abs(edge.At - caret))
            .ThenBy(edge => edge.Extent.Width * edge.Extent.Height)
            .FirstOrDefault();

        return spot.Control is null || !TryBounds(spot.Control, out var top, out var bottom)
            ? null
            : (spot.After, top, bottom);
    }

    /// <summary>
    /// Draws the standing insertion line: two pixels wide and the colour of the accent, blinking the
    /// way a text caret does (the blink itself is a style animation, not something this method drives).
    /// A drag hides it in favour of the drop cursor, so the two never claim the same seam.
    /// </summary>
    private void ShowCaret()
    {
        if (_dragging || _viewModel.Recipe is null || CaretPlace() is not { } place)
        {
            TextCaret.IsVisible = false;
            return;
        }

        var visible = VisibleOverlay();
        var x = place.After ? place.Bottom.X : place.Top.X;
        var from = Math.Max(place.Top.Y, visible.Top);
        var to = Math.Min(place.Bottom.Y, visible.Bottom);
        if (to - from < 6 || x < visible.Left || x > visible.Right)
        {
            TextCaret.IsVisible = false;
            return;
        }

        TextCaret.Height = to - from;
        // The seam is the point, so the line straddles it rather than growing to one side: the drop
        // cursor is centred the same way and the two never show at once. Both are two pixels wide.
        Canvas.SetLeft(TextCaret, Math.Clamp(x - 1, visible.Left,
            Math.Max(visible.Left, visible.Right - 2)));
        Canvas.SetTop(TextCaret, from);
        TextCaret.IsVisible = true;
    }

    /// <summary>One place the standing line can stand, in the overlay's own coordinates.</summary>
    private readonly record struct CaretSpot(int At, double X, double Top, double Bottom)
    {
        public double Middle => (Top + Bottom) / 2;
    }

    /// <summary>
    /// Every place the line can stand: the two edges of every piece on screen. Offsets inside a name
    /// are left out on purpose, because the line is only ever drawn on an edge.
    /// </summary>
    private List<CaretSpot> Spots()
    {
        var spots = new List<CaretSpot>();
        foreach (var piece in Pieces())
        {
            if (!TryBounds(piece.Control, out var top, out var bottom))
            {
                continue;
            }

            spots.Add(new CaretSpot(piece.Node.Start, top.X, top.Y, bottom.Y));
            spots.Add(new CaretSpot(piece.Node.Start + piece.Node.Length, bottom.X, top.Y, bottom.Y));
        }

        return spots;
    }

    /// <summary>
    /// Moves the line to the nearest place on the row above or below. The score puts the distance
    /// between rows far ahead of the distance along them, so the next row up wins over a place that
    /// merely happens to sit at the same height at the far end of this one.
    /// </summary>
    private bool StepRow(bool down)
    {
        if (CaretPlace() is not { } here)
        {
            return false;
        }

        var from = (here.Top.Y + here.Bottom.Y) / 2;
        var x = here.After ? here.Bottom.X : here.Top.X;
        CaretSpot? best = null;
        var bestScore = double.MaxValue;
        foreach (var spot in Spots())
        {
            var gap = down ? spot.Middle - from : from - spot.Middle;
            // Pieces on one row are not exactly the same height, so a few pixels of gap are still
            // this row rather than the next one.
            if (gap < 8)
            {
                continue;
            }

            var score = (gap * 1000) + Math.Abs(spot.X - x);
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            best = spot;
        }

        if (best is not { } target)
        {
            return false;
        }

        _viewModel.SetCaret(target.At);
        return true;
    }

    /// <summary>
    /// Scrolls the band so the place the line moved to is in sight. Without this, holding an arrow key
    /// walks the line off the strip and nothing on screen says it is still moving.
    /// </summary>
    private void RevealCaret()
    {
        if (CaretPlace() is not { } place)
        {
            return;
        }

        var visible = VisibleOverlay();
        var above = visible.Top - place.Top.Y;
        var below = place.Bottom.Y - visible.Bottom;
        var shift = above > 0 ? -above : below > 0 ? below : 0;
        if (shift == 0)
        {
            return;
        }

        var offset = BandScroll.Offset;
        BandScroll.Offset = new Vector(offset.X, Math.Max(0, offset.Y + shift));
    }

    /// <summary>
    /// Works out what is under the pointer again from where it last was. The band is rebuilt from
    /// the text after every edit, so the pieces the pointer was over are new objects and no
    /// pointer-moved event arrives to say so.
    /// </summary>
    private void RefreshHover()
    {
        if (_dragging || this.TranslatePoint(_pointer, RemoveLayer) is not { } at
            || !VisibleOverlay().Contains(at))
        {
            SetHover(null);
            return;
        }

        // Reaching for the cross must not hand it to the neighbour underneath.
        if (_removeRect is { } cross && cross.Contains(at))
        {
            PlaceRemove();
            return;
        }

        SetHover(RemoveLayer.TranslatePoint(at, Band) is { } onBand
            ? TokenAt(onBand)
            : null);
    }

    private void OnBandExited(object? sender, PointerEventArgs e) => SetHover(null);

    private void OnBandScrolled(object? sender, ScrollChangedEventArgs e)
    {
        RefreshHover();
        ShowCaret();
    }

    private void OnBandCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetDrag();

    /// <summary>
    /// Dragging a piece onto another moves it there, brackets included. Pointer input rather than a
    /// platform drag session, because this is one window pointing at itself and a session would
    /// leave it while the ghost is still here.
    /// </summary>
    private void OnBandPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Band);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressedCross = false;
        _pendingCaret = CaretFor(point.Position);

        // The cross is drawn in a layer that takes no input, so its press arrives here. It belongs
        // to the piece the cross was drawn for, not to whatever token happens to sit underneath.
        if (_removeRect is { } cross && cross.Contains(e.GetCurrentPoint(RemoveLayer).Position)
            && HoverRemove.DataContext is SlotNode piece && _viewModel.RemoveArgument(piece))
        {
            _pressedCross = true;
            _pendingCaret = null;
            e.Handled = true;
            return;
        }

        // A press on a real button belongs to that button: capturing the band here would start a
        // drag of the piece it was about to work on.
        for (var walker = e.Source as Visual;
             walker is not null && !ReferenceEquals(walker, Band);
             walker = walker.GetVisualParent())
        {
            if (walker is Button)
            {
                return;
            }
        }

        _dragPiece = TokenAt(point.Position);
        _dragFrom = point.Position;
        _dragging = false;
        SetHover(_dragPiece);
    }

    private void OnBandMoved(object? sender, PointerEventArgs e)
    {
        _pointer = e.GetPosition(this);
        var point = e.GetPosition(Band);

        if (!_dragging)
        {
            // While the pointer is on the cross, keep the piece it was drawn for.
            if (_removeRect is not { } cross || !cross.Contains(e.GetPosition(RemoveLayer)))
            {
                SetHover(TokenAt(point));
            }

            if (_dragPiece is null)
            {
                return;
            }
        }

        if (_dragPiece is null)
        {
            return;
        }

        if (!_dragging
            && Math.Abs(point.X - _dragFrom.X) < 6
            && Math.Abs(point.Y - _dragFrom.Y) < 6)
        {
            return;
        }

        if (!_dragging)
        {
            _dragging = true;
            SetHover(null);
            e.Pointer.Capture(Band);
        }

        ShowDropCursor(e);
        ShowGhost(e);
    }

    /// <summary>The small copy of what is being dragged, following the pointer.</summary>
    private void ShowGhost(PointerEventArgs e)
    {
        var piece = _dragPiece!;
        DragLabel.Text = _viewModel.Text.Substring(piece.Start, piece.Length).Trim();
        DragGhost.IsVisible = true;

        var at = e.GetPosition(DragLayer);
        var width = Math.Max(DragGhost.Bounds.Width, 60);
        var left = at.X + 12;

        // A ghost lying over the drop bar hides the very place the bar is pointing at.
        if (DropLine.IsVisible && left < Canvas.GetLeft(DropLine) + 8
            && left + width > Canvas.GetLeft(DropLine) - 8)
        {
            left = at.X - width - 12;
        }

        Canvas.SetLeft(DragGhost, Math.Max(0, left));
        Canvas.SetTop(DragGhost, at.Y + 8);
    }

    /// <summary>
    /// Draws where the piece would land, from the one preview the release will also use. The anchor
    /// of that preview is the piece the seam belongs to; drawn on the control under the pointer it
    /// would show a whole run or a whole call when the drop actually happens beside one token.
    /// </summary>
    private void ShowDropCursor(PointerEventArgs e)
    {
        TextCaret.IsVisible = false;
        var piece = _dragPiece;
        var control = ControlAt(e.GetPosition(Band));
        _dropTarget = control?.DataContext as SlotNode;
        var node = _dropTarget;

        if (piece is null || control is null || node is null || ReferenceEquals(node, piece))
        {
            _preview = new ValuePickerViewModel.DropPreview(
                node ?? piece ?? _viewModel.Recipe!, false, false, false);
            DropLine.IsVisible = false;
            DropRing.IsVisible = false;
            return;
        }

        var rightHalf = e.GetPosition(control).X > control.Bounds.Width / 2;
        var lowerHalf = e.GetPosition(control).Y > control.Bounds.Height / 2;
        var after = ValuePickerViewModel.AfterFor(node, rightHalf, lowerHalf);
        _preview = _viewModel.PreviewMove(piece, node, after);

        var anchor = AnchorControl(_preview.Anchor, control);
        ShowRing(anchor, _preview.Allowed);
        ShowLine(anchor, _preview.After, _preview.Allowed && !_preview.NoOp);
    }

    /// <summary>
    /// The part of the band that is actually on screen, in the coordinates shared by both overlay
    /// layers. DragLayer and RemoveLayer sit in the same grid cell, so one rectangle serves both.
    /// </summary>
    private Rect VisibleOverlay()
    {
        var origin = BandScroll.TranslatePoint(default, RemoveLayer) ?? default;
        return new Rect(origin.X, origin.Y,
            Math.Max(0, BandScroll.Viewport.Width), Math.Max(0, BandScroll.Viewport.Height));
    }

    private void ShowRing(Control control, bool show)
    {
        if (!show || !TryBounds(control, out var top, out var bottom))
        {
            DropRing.IsVisible = false;
            return;
        }

        var visible = VisibleOverlay();
        var from = Math.Max(top.Y, visible.Top);
        var to = Math.Min(bottom.Y, visible.Bottom);
        if (to - from < 6)
        {
            DropRing.IsVisible = false;
            return;
        }

        DropRing.Width = Math.Max(0, bottom.X - top.X) + 6;
        DropRing.Height = to - from + 6;
        Canvas.SetLeft(DropRing, top.X - 3);
        Canvas.SetTop(DropRing, from - 3);
        DropRing.IsVisible = true;
    }

    private void ShowLine(Control control, bool after, bool show)
    {
        if (!show || !TryBounds(control, out var top, out var bottom))
        {
            DropLine.IsVisible = false;
            return;
        }

        var x = after ? bottom.X : top.X;
        var visible = VisibleOverlay();
        var from = Math.Max(top.Y, visible.Top);
        var to = Math.Min(bottom.Y, visible.Bottom);

        // A seam that is nearly out of sight would be a bar pinned to the edge, saying there is a
        // place to drop where the band shows none.
        if (to - from < 6 || x < visible.Left || x > visible.Right)
        {
            DropLine.IsVisible = false;
            return;
        }

        DropLine.Height = to - from;
        Canvas.SetLeft(DropLine, Math.Clamp(x - 1, visible.Left,
            Math.Max(visible.Left, visible.Right - 2)));
        Canvas.SetTop(DropLine, from);
        DropLine.IsVisible = true;
    }

    /// <summary>Where a control is drawn, in the layer the cursor is painted in.</summary>
    private bool TryBounds(Control control, out Point topLeft, out Point bottomRight)
    {
        topLeft = default;
        bottomRight = default;

        if (control.TranslatePoint(default, DragLayer) is not { } a
            || control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), DragLayer)
                is not { } b)
        {
            return false;
        }

        topLeft = a;
        bottomRight = b;
        return true;
    }

    private void OnBandReleased(object? sender, PointerReleasedEventArgs e)
    {
        var piece = _dragPiece;
        var dragging = _dragging;
        var preview = _preview;
        var target = _dropTarget;
        var pressedCross = _pressedCross;
        var pendingCaret = _pendingCaret;
        _pressedCross = false;
        _pendingCaret = null;
        ResetDrag();
        e.Pointer.Capture(null);

        if (pressedCross)
        {
            return;
        }

        // A press that stayed a click only moves the insertion point. A drag of six pixels or more
        // is a move of a piece and must not leave the line somewhere the pointer merely passed over.
        if (!dragging && pendingCaret is { } at)
        {
            _viewModel.SetCaret(at);
        }

        // The picture and the result come from the same preview. Looking under the pointer a second
        // time here would let the two disagree about which piece the drop belongs to.
        if (!dragging || piece is null || target is null || !preview.Allowed)
        {
            return;
        }

        _viewModel.MoveOnto(piece, target, preview.After);
    }

    /// <summary>Puts the band back the way it looks when nothing is being dragged.</summary>
    private void ResetDrag()
    {
        _dragPiece = null;
        _dragging = false;
        _dropTarget = null;
        _preview = default;
        DragGhost.IsVisible = false;
        DropLine.IsVisible = false;
        DropRing.IsVisible = false;
        SetHover(null);
        Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
    }

    /// <summary>The innermost piece drawn under this point.</summary>
    private SlotNode? TokenAt(Point point) => ControlAt(point)?.DataContext as SlotNode;

    private Control? ControlAt(Point point)
    {
        for (var walker = Band.InputHitTest(point) as Visual;
             walker is not null;
             walker = walker.GetVisualParent())
        {
            if (walker is Control { DataContext: SlotNode } control)
            {
                return control;
            }
        }

        return null;
    }

    private void OnVariableFilter(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VariableFilter filter })
        {
            _viewModel.Wall.SelectedFilter = filter;
        }
    }

    private void OnFunctionFilter(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExpressionFunctionFilter filter })
        {
            _viewModel.SelectedFunctionGroup = filter;
        }
    }

    private void OnOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExpressionOperator sign })
        {
            return;
        }

        Put(sign.Insert);
    }

    private void OnValue(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ValueToken value })
        {
            Put(value.Insert);
        }
    }

    /// <summary>
    /// A name goes into the formula, or is handed back at once when the cell holds nothing else:
    /// there is no formula to build in that case, so the name is the answer.
    /// </summary>
    private void OnVariablePill(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: VariableChoice choice })
        {
            return;
        }

        if (_viewModel.NamesOnly)
        {
            _viewModel.Choose(choice.Token);
            return;
        }

        _viewModel.Wall.Looking = choice;
        Put(choice.Token);
    }

    private void OnFunctionPill(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExpressionFunctionChoice function })
        {
            return;
        }

        _viewModel.SelectedFunction = function;
        _viewModel.PlaceCall(function.Name);
    }

    /// <summary>
    /// Escape leaves the cell as it was. Enter belongs to the small field and only inserts a value;
    /// neither key closes the window by accident, so leaving is always the confirm button or Esc.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        _viewModel.Cancel();
        e.Handled = true;
    }

    /// <summary>
    /// True when the arrows already belong to whatever has focus: a search box or the small field
    /// moves its own text caret, and a list moves its own selection. Taking the keys there would
    /// break both, and neither is a place a piece of the formula is written.
    /// </summary>
    private static bool KeysOwnedElsewhere(KeyEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return false;
        }

        return source is TextBox || source.GetVisualAncestors().OfType<ListBox>().Any();
    }

    private void OnUndo(object? sender, RoutedEventArgs e) => _viewModel.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => _viewModel.Redo();

    private void OnClear(object? sender, RoutedEventArgs e) => _viewModel.Clear();

    /// <summary>
    /// The two buttons only say what the standing field takes. A number and a piece of text are
    /// written differently, so switching clears what was half typed in the other mode.
    /// </summary>
    private void OnNumber(object? sender, RoutedEventArgs e) => SetTypingMode(number: true);

    private void OnText(object? sender, RoutedEventArgs e) => SetTypingMode(number: false);

    private void SetTypingMode(bool number)
    {
        _viewModel.SetTypingMode(number);
        ValueInput.Focus();
    }

    /// <summary>Writes the value at the insertion point and leaves the field ready for the next one.</summary>
    private void OnInsertTyped(object? sender, RoutedEventArgs e) => InsertTyped();

    /// <summary>
    /// Enter in the small field inserts a value and does nothing else. It never closes the window:
    /// a formula is built a piece at a time and leaving is the confirm button or Esc.
    /// </summary>
    private void OnValueInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        InsertTyped();
        e.Handled = true;
    }

    private void InsertTyped()
    {
        if (_viewModel.TypedToken() is not { } token)
        {
            return;
        }

        Put(token);
        _viewModel.ClearTyped();
        ValueInput.Focus();
    }

    private void OnInsert(object? sender, RoutedEventArgs e) => _viewModel.Confirm();

    private void OnCancel(object? sender, RoutedEventArgs e) => _viewModel.Cancel();
}
