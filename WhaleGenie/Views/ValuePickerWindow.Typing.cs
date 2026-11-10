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

// Typing a value in: which keys belong elsewhere, the mode, and what is inserted.
public partial class ValuePickerWindow : AppWindow
{
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
