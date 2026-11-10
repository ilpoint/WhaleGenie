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

// What the pointer is over: the piece under it, the remove button, and the anchors a piece is placed against.
public partial class ValuePickerWindow : AppWindow
{
    /// <summary>
    /// Shows the cross on the one piece the pointer is on. Done here rather than with a
    /// <c>:pointerover</c> style because Avalonia raises that pseudo-class on every ancestor of what
    /// the pointer hit — hovering one name would light the cross of each box it sits inside — and a
    /// drag clears it for the whole band the moment the pointer is captured. The cross itself is a
    /// button, so its own hover is its own pseudo-class.
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
            // The node goes with the cross. After an edit the band is a new tree, and a button still
            // holding a node from the old one would cut whatever now sits at those offsets.
            HoverRemove.DataContext = null;
            HoverRemove.IsVisible = false;
            return;
        }

        // Everything here is in the overlay's coordinates. Mixing these with the band's own made the
        // cross drift away from its piece after the strip scrolled. It sits just above the piece's
        // top-right corner rather than on it: a cross drawn over the corner would swallow the click
        // meant to put the insertion point after that piece. Only the layer's own clipping can cut it
        // now that it is a real button, so that box is what the clamp is for — clamping to the content
        // viewport instead would push a first-row cross back down onto its piece.
        var box = OverlayBox();
        var left = Math.Clamp(b.X - CrossSize, box.Left, Math.Max(box.Left, box.Right - CrossSize));
        var top = Math.Clamp(a.Y - CrossLift, box.Top, Math.Max(box.Top, box.Bottom - CrossSize));
        Canvas.SetLeft(HoverRemove, left);
        Canvas.SetTop(HoverRemove, top);
        HoverRemove.DataContext = node;
        HoverRemove.IsVisible = true;
    }

    /// <summary>True when a point in the overlays' coordinates lies on the cross as it is drawn.</summary>
    private bool CrossUnder(Point at)
        => HoverRemove.IsVisible
            && new Rect(Canvas.GetLeft(HoverRemove), Canvas.GetTop(HoverRemove), CrossSize, CrossSize)
                .Contains(at);

    /// <summary>
    /// The piece standing under a point in the overlays' coordinates. Used by the cross, which sits in
    /// a sibling layer: while the pointer is on it the band is told nothing about where the pointer is.
    /// </summary>
    private void HoverFrom(Point at)
        => SetHover(RemoveLayer.TranslatePoint(at, Band) is { } onBand ? TokenAt(onBand) : null);

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
}
