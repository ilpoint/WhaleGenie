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

// The keys the window answers before anything inside it does.
public partial class ValuePickerWindow : AppWindow
{
    /// <summary>
    /// The keys that belong to the window rather than to a field. Undo has to be keyed in front of
    /// everything else: a text box keeps its own history and would swallow the keys before a bubbling
    /// handler ever sees them. The arrows are keyed here for the same reason — the strip they move is
    /// inside a scroll viewer that would rather scroll — and they stand aside whenever a field or a
    /// list is the thing with focus.
    /// </summary>
    private void OnWindowTunnelKey(object? sender, KeyEventArgs e)
    {
        // A field or a list with the focus owns its keys, undo included: the small field keeps its
        // own history, and taking Ctrl+Z there would undo the formula instead of what was typed.
        if (KeysOwnedElsewhere(e))
        {
            return;
        }

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

        if (e.KeyModifiers != KeyModifiers.None)
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

    /// <summary>The box the cross is drawn in, and how far above the piece's top edge it reaches.</summary>
    private const double CrossSize = 16;

    private const double CrossLift = 14;

    /// <summary>The piece last hovered during a drag, and the preview drawn for it.</summary>
    private SlotNode? _dropTarget;
    private ValuePickerViewModel.DropPreview _preview;

    /// <summary>The insertion point a press asked for, applied only if the press stayed a click.</summary>
    private int? _pendingCaret;
}
