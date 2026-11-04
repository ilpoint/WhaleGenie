using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The picker a field opens to fill itself in with a variable. It reads and reports; the field
/// that opened it keeps the text and puts it in where the caret is.
/// </summary>
public partial class VariablePickerWindow : AppWindow
{
    public VariablePickerWindow()
        : this(new VariablePickerViewModel())
    {
    }

    public VariablePickerWindow(VariablePickerViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        Title = viewModel.Header;
        viewModel.CloseRequested += Close;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // Typing is what this window is for, so the caret starts in the box that narrows the list.
        Opened += (_, _) =>
        {
            this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
        };
    }

    private VariablePickerViewModel Picker => (VariablePickerViewModel)DataContext!;

    /// <summary>
    /// Opens the picker over <paramref name="owner"/> with the variables the macro can read, and
    /// reports what was picked, or null when it was dismissed.
    /// </summary>
    /// <param name="lookAt">
    /// What to do with a step somebody asked to be shown from the wall's list of the steps that read
    /// a variable. Null where there is no step list to show it in.
    /// </param>
    /// <param name="offerBuilder">
    /// True for a field that has no expression button of its own, where this picker also offers the
    /// expression editor. A field that has one leaves it unset: two buttons that open the same
    /// window are one button too many.
    /// </param>
    public static Task<VariablePick?> ShowFor(Window owner, IReadOnlyList<VariableChoice> variables,
        Action<MacroStep>? lookAt = null, bool offerBuilder = false)
    {
        var viewModel = new VariablePickerViewModel(variables, offerBuilder);
        if (lookAt is not null)
        {
            viewModel.Jumped += lookAt;
        }

        return new VariablePickerWindow(viewModel).ShowDialogOver<VariablePick?>(owner);
    }

    /// <summary>One variable is what this window hands back, so a click is the whole answer.</summary>
    private void OnChoose(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VariableChoice choice })
        {
            Picker.Wall.ChooseCommand.Execute(choice);
        }
    }

    /// <summary>The pointer is over a variable, so the column beside the list says what it is.</summary>
    private void OnLooking(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: VariableChoice choice })
        {
            Picker.Wall.Looking = choice;
        }
    }

    private void OnUnlooked(object? sender, PointerEventArgs e) => Picker.Wall.Looking = null;

    /// <summary>
    /// Up and down walk the list, Enter takes what is highlighted and Escape puts it back. A picker
    /// that can only be driven by the pointer makes the keyboard user scroll with their eyes.
    /// </summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        var shown = Picker.Wall.Shown;
        if (shown.Count == 0)
        {
            return;
        }

        var at = Picker.Wall.Looking is { } looking ? shown.IndexOf(looking) : -1;
        switch (e.Key)
        {
            case Key.Down:
                Picker.Wall.Looking = shown[Math.Min(at + 1, shown.Count - 1)];
                e.Handled = true;
                break;
            case Key.Up:
                Picker.Wall.Looking = shown[Math.Max(at - 1, 0)];
                e.Handled = true;
                break;
            case Key.Enter:
                Picker.Wall.ChooseCommand.Execute(Picker.Wall.Looking ?? shown[0]);
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape && DataContext is VariablePickerViewModel viewModel)
        {
            viewModel.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
