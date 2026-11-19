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

// Dragging a piece: the ghost, the line it would land on, and the drop that decides where it goes.
public partial class ValuePickerWindow : AppWindow
{
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

        _pendingCaret = CaretFor(point.Position);

        // A press in the strip is a press into the formula, so the keyboard follows it there. Without
        // this the search box keeps the focus, and the arrow keys keep going to the search box.
        Band.Focus();

        // A press on a real button belongs to that button: capturing the band here would start a
        // drag of the piece it was about to work on. The cross is one of those — it sits in its own
        // layer and takes its click itself — and a token may grow one later.
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
        _viewModel.Select(_dragPiece);
        SetHover(_dragPiece);
    }

    private void OnBandMoved(object? sender, PointerEventArgs e)
    {
        _pointer = e.GetPosition(this);
        var point = e.GetPosition(Band);

        if (!_dragging)
        {
            SetHover(TokenAt(point));

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
        DragLabel.Text = _viewModel.PieceLabel(piece).Trim();
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
    /// The area the two overlays are clipped to. They sit in the same grid cell as the strip, so a
    /// layer's own box is the strip's box — the same rectangle <c>ClipToBounds</c> cuts them at.
    /// </summary>
    private Rect OverlayBox() => new(RemoveLayer.Bounds.Size);

    /// <summary>
    /// The part of the band that is really on screen, in the coordinates both overlay layers share.
    /// It is the overlap of the band's own box with the layer's: the band sits inside the strip's
    /// padding and grows past the viewport once a formula is long, and the layer is what clips the two
    /// overlays. Reading the scroll viewer's viewport instead is off by that padding at both ends.
    /// </summary>
    private Rect VisibleOverlay()
    {
        if (Band.TranslatePoint(default, RemoveLayer) is not { } a
            || Band.TranslatePoint(new Point(Band.Bounds.Width, Band.Bounds.Height), RemoveLayer)
                is not { } b)
        {
            return default;
        }

        return new Rect(a, b).Intersect(OverlayBox());
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
        var pendingCaret = _pendingCaret;
        _pendingCaret = null;
        ResetDrag();
        e.Pointer.Capture(null);

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
}
