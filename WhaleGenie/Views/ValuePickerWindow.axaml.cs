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

        // Every edit hands the band a new tree, so the piece the pointer was over no longer exists.
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ValuePickerViewModel.Recipe))
            {
                Dispatcher.UIThread.Post(RefreshHover, DispatcherPriority.Loaded);
            }
        };

        Opened += (_, _) =>
        {
            if (!viewModel.CanCompute)
            {
                NameSearch.Focus();
                return;
            }

            VariableSearch.Focus();
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
    /// Puts a piece where the person pointed: over the piece chosen in the structure band when one
    /// is, at the end of the line otherwise. The window stays open either way — a formula is built a
    /// piece at a time, and closing on the first piece would make composing one a series of openings.
    /// </summary>
    private void Put(string token) => _viewModel.Place(token);

    /// <summary>
    /// A click on the structure band points at one piece: the caret goes where that piece is written,
    /// so whatever is chosen next from a palette lands there. An empty place is pointed at its own
    /// start, which is where the missing thing would go; a piece already written is pointed after it.
    /// </summary>
    private void OnSlotTapped(object? sender, TappedEventArgs e)
    {
        // The press on the cross already removed the piece. Letting the tap that follows also choose
        // it would leave a stale piece as the place for the next material.
        if (_pressedCross)
        {
            _pressedCross = false;
            e.Handled = true;
            return;
        }

        // A button inside the band does its own job on the way down; the tap that lifted it must not
        // also choose the whole box it sits in — clicking the cross would otherwise leave the call
        // chosen, and the next material would replace the lot.
        for (var walker = e.Source as Visual; walker is not null && !ReferenceEquals(walker, sender);
             walker = walker.GetVisualParent())
        {
            if (walker is Button)
            {
                e.Handled = true;
                return;
            }
        }

        if (sender is not Control { DataContext: SlotNode node })
        {
            return;
        }

        _viewModel.ChooseSlot(node);
        e.Handled = true;
    }

    /// <summary>
    /// Undo and redo, from anywhere in the window. Nothing else is keyed here: with no formula box
    /// there is no typing to key around.
    /// </summary>
    private void OnWindowTunnelKey(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

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
    }

    private SlotNode? _dragPiece;
    private Point _dragFrom;
    private bool _dragging;
    private SlotNode? _hovered;
    private Point _pointer;

    /// <summary>Where the one cross is drawn, in the band's own coordinates, or null when it is away.</summary>
    private Rect? _removeRect;

    /// <summary>True when this press was on the cross, so the tap that follows must not choose a piece.</summary>
    private bool _pressedCross;

    /// <summary>The piece last hovered during a drag, and the preview drawn for it.</summary>
    private SlotNode? _dropTarget;
    private ValuePickerViewModel.DropPreview _preview;

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
                RemoveLayer) is not { } b
            || control.TranslatePoint(default, Band) is not { } bandA
            || control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height),
                Band) is not { } bandB)
        {
            HoverRemove.IsVisible = false;
            _removeRect = null;
            return;
        }

        // The cross sits inside the top-right corner of its piece. Keeping it inside the band is
        // what lets the band's own pointer handlers notice a press on it; the overlay is what keeps
        // it above the piece's content and above every later sibling.
        Canvas.SetLeft(HoverRemove, b.X - 18);
        Canvas.SetTop(HoverRemove, a.Y + 1);
        HoverRemove.DataContext = node;
        HoverRemove.IsVisible = true;
        _removeRect = new Rect(bandB.X - 18, bandA.Y + 1, 16, 16);
    }

    /// <summary>
    /// The outermost visible control standing for one piece. Pre-order matters: the same
    /// DataContext is inherited by everything inside the token, and the outer control is the box a
    /// ring or a cross belongs around, not the text inside it.
    /// </summary>
    private Control? ControlFor(SlotNode node)
        => Band.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(one => one.IsEffectivelyVisible
                && ReferenceEquals(one.DataContext, node));

    /// <summary>
    /// Works out what is under the pointer again from where it last was. The band is rebuilt from
    /// the text after every edit, so the pieces the pointer was over are new objects and no
    /// pointer-moved event arrives to say so.
    /// </summary>
    private void RefreshHover()
    {
        if (_dragging || this.TranslatePoint(_pointer, Band) is not { } at || !Band.Bounds.Contains(at))
        {
            SetHover(null);
            return;
        }

        // The cross sits just outside its piece. Reaching for it must not hand it to the neighbour.
        if (_removeRect is { } cross && cross.Contains(at))
        {
            PlaceRemove();
            return;
        }

        SetHover(TokenAt(at));
    }

    private void OnBandExited(object? sender, PointerEventArgs e) => SetHover(null);

    private void OnBandScrolled(object? sender, ScrollChangedEventArgs e) => RefreshHover();

    private void OnBandCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetDrag();

    /// <summary>
    /// Dragging a piece onto another moves it there; dragging a bracket onto a piece puts a pair of
    /// brackets around it. Pointer input rather than a platform drag session, because this is one
    /// window pointing at itself and a session would leave it while the ghost is still here.
    /// </summary>
    private void OnBandPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Band);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressedCross = false;

        // The cross is drawn in a layer that takes no input, so its press arrives here. It belongs
        // to the piece the cross was drawn for, not to whatever token happens to sit underneath.
        if (_removeRect is { } cross && cross.Contains(point.Position)
            && HoverRemove.DataContext is SlotNode piece && _viewModel.RemoveArgument(piece))
        {
            _pressedCross = true;
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
            if (_removeRect is not { } cross || !cross.Contains(point))
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
        var piece = _dragPiece;
        var control = ControlAt(e.GetPosition(Band));
        _dropTarget = control?.DataContext as SlotNode;
        var node = _dropTarget;

        if (piece is null || control is null || node is null || ReferenceEquals(node, piece))
        {
            _preview = new ValuePickerViewModel.DropPreview(
                node ?? piece ?? _viewModel.Recipe!, false, false, false,
                ValuePickerViewModel.DropKind.None);
            DropLine.IsVisible = false;
            DropRing.IsVisible = false;
            return;
        }

        var rightHalf = e.GetPosition(control).X > control.Bounds.Width / 2;
        var lowerHalf = e.GetPosition(control).Y > control.Bounds.Height / 2;
        var after = ValuePickerViewModel.AfterFor(node, rightHalf, lowerHalf);
        _preview = _viewModel.PreviewDrop(piece, node, after);

        var anchor = ControlFor(_preview.Anchor) ?? control;
        ShowRing(anchor, _preview.Allowed && _preview.Kind != ValuePickerViewModel.DropKind.None);
        ShowLine(anchor, _preview.After,
            _preview.Allowed && !_preview.NoOp
            && _preview.Kind == ValuePickerViewModel.DropKind.Insert);
    }

    /// <summary>The part of the band that is actually on screen, in the layer the cursor is painted in.</summary>
    private Rect VisibleBand()
    {
        var origin = BandScroll.TranslatePoint(default, DragLayer) ?? default;
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

        var visible = VisibleBand();
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
        var visible = VisibleBand();
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
        ResetDrag();
        e.Pointer.Capture(null);

        // The picture and the result come from the same preview. Looking under the pointer a second
        // time here would let the two disagree about which piece the drop belongs to.
        if (!dragging || piece is null || target is null || !preview.Allowed)
        {
            return;
        }

        if (preview.Kind == ValuePickerViewModel.DropKind.Wrap)
        {
            _viewModel.Wrap(target, piece is SlotBracket { Text: "[" } ? '[' : '(');
            return;
        }

        if (preview.Kind == ValuePickerViewModel.DropKind.Insert)
        {
            _viewModel.MoveOnto(piece, target, preview.After);
        }
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

        // A bracket picked while a piece is chosen wraps that piece: that is the click version of
        // dragging a bracket onto it, and it is the only way to put a pair around a whole call.
        if (sign.Token is "(" or "["
            && _viewModel.ChosenSlot is { IsEmpty: false } chosen
            && _viewModel.Wrap(chosen, sign.Token[0]))
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
    /// Escape is the one key this window answers to: it leaves the cell as it was. Every other way
    /// out is a button, so a stray Enter while typing a formula cannot end the work.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        // Escape backs out of what is nearest first: the asking row, then the window.
        if (_viewModel.IsTypingValue)
        {
            _viewModel.EndTyping();
        }
        else
        {
            _viewModel.Cancel();
        }

        e.Handled = true;
    }

    /// <summary>The plus beside the ghost writes one more empty place into the call.</summary>
    private void OnAddSlot(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SlotCall call } && _viewModel.AppendArgument(call))
        {
            e.Handled = true;
        }
    }

    private void OnUndo(object? sender, RoutedEventArgs e) => _viewModel.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => _viewModel.Redo();

    /// <summary>
    /// Asks for a number or for a piece of text in the strip itself. These are the two things the
    /// formula holds that no card can offer, so asking for them here is what keeps the band enough
    /// on its own.
    /// </summary>
    private void OnNumber(object? sender, RoutedEventArgs e) => BeginTyping(number: true);

    private void OnText(object? sender, RoutedEventArgs e) => BeginTyping(number: false);

    private void BeginTyping(bool number)
    {
        _viewModel.BeginTyping(number);

        // The row is laid out on the next pass, so the caret can only be put in it after that.
        Dispatcher.UIThread.Post(() => ValueInput.Focus(), DispatcherPriority.Input);
    }

    private void OnInsertTyped(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.TypedToken() is not { } token)
        {
            return;
        }

        Put(token);
        _viewModel.EndTyping();
    }

    private void OnCancelTyped(object? sender, RoutedEventArgs e) => _viewModel.EndTyping();

    private void OnInsert(object? sender, RoutedEventArgs e) => _viewModel.Confirm();

    private void OnCancel(object? sender, RoutedEventArgs e) => _viewModel.Cancel();
}
