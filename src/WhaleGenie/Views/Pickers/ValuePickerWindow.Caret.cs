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

// Where the caret can sit: the places between the pieces, drawing it, and moving it a row at a time.
public partial class ValuePickerWindow : AppWindow
{
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
            || !OverlayBox().Contains(at))
        {
            SetHover(null);
            return;
        }

        // No "keep the cross while the pointer is on it" here: after an edit that piece is a new
        // object with no control left to sit on, so the honest answer is what stands there now.
        HoverFrom(at);
    }
}
