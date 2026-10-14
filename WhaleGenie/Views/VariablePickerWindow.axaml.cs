using System;
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
    /// reports what was picked, or null when it was dismissed.
    /// </summary>
    /// <param name="lookAt">
    /// What to do with a step somebody asked to be shown from the wall's list of the steps that read
    /// a variable. Null where there is no step list to show it in.
    /// </param>
    /// <param name="offerBuilder">
    /// True for a field whose whole value is read at once, where the picker also offers the
    /// expression editor: a name is what this wall has, and a field like that can hold a value
    /// worked out with a function as well.
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
