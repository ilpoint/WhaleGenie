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

// The band around the formula: leaving it, the remove button, and scrolling it.
public partial class ValuePickerWindow : AppWindow
{
    /// <summary>
    /// The pointer left the band. Reaching for the cross leaves this subtree as well — the cross is a
    /// sibling layer, not a child — so the two have to be told apart: a cross hidden the moment the
    /// pointer arrives on it can never be clicked, and the layer would draw it again on the next
    /// move, which is a flicker at every approach. The position is compared rather than the pointer's
    /// over flag because Avalonia raises the exited events of the old chain before the entered events
    /// of the new one.
    /// </summary>
    private void OnBandExited(object? sender, PointerEventArgs e)
    {
        if (!CrossUnder(e.GetPosition(RemoveLayer)))
        {
            SetHover(null);
        }
    }

    /// <summary>
    /// The cross takes its piece away. It carries the piece it was drawn for, so there is no rectangle
    /// to compare the pointer against: what was drawn is what was pressed.
    /// </summary>
    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SlotNode piece })
        {
            return;
        }

        // The cross goes first: the rebuild replaces the tree this node belongs to, and a button left
        // holding the old node would cut whatever now stands at those offsets.
        SetHover(null);
        _viewModel.RemoveArgument(piece);

        // The keyboard follows the formula, the way it does after any other press in the strip.
        Band.Focus();
    }

    /// <summary>
    /// The pointer is on the cross, so the band is not being told where it is. Keeping the last
    /// position current here is what lets the cross be put back on the right piece after an edit
    /// rebuilds the band under it.
    /// </summary>
    private void OnRemoveMoved(object? sender, PointerEventArgs e) => _pointer = e.GetPosition(this);

    /// <summary>
    /// The pointer left the cross. Wherever it went is the new answer: onto another piece, back onto
    /// this one, or off the strip, which <see cref="HoverFrom"/> reads as no piece at all.
    /// </summary>
    private void OnRemoveExited(object? sender, PointerEventArgs e)
        => HoverFrom(e.GetPosition(RemoveLayer));

    private void OnBandScrolled(object? sender, ScrollChangedEventArgs e)
    {
        RefreshHover();
        ShowCaret();
    }

    private void OnBandCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetDrag();
}
