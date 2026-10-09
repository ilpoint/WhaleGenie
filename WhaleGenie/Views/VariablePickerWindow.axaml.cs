using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The picker a field opens to fill itself in with a variable. It reads and reports; the field
/// that opened it keeps the text and puts it in where the caret is.
/// </summary>
public partial class VariablePickerWindow : Window
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

        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += (_, _) => Close(null);
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Opens the picker over <paramref name="owner"/> with the variables the macro can read, and
    /// reports the text of the one that was picked, or null when it was dismissed.
    /// </summary>
    public static Task<string?> ShowFor(Window owner, IReadOnlyList<VariableChoice> variables)
        => new VariablePickerWindow(new VariablePickerViewModel(variables)).ShowDialogOver<string?>(owner);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape && DataContext is VariablePickerViewModel viewModel)
        {
            viewModel.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
