using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The form part of a macro's own interface: its parameters and results, and the two tools a
/// default value needs. The dialogs only report text; the row that opened one keeps it.
/// </summary>
public partial class MacroInterfaceView : UserControl
{
    public MacroInterfaceView()
    {
        InitializeComponent();
    }

    private MacroInterfaceViewModel? Model => DataContext as MacroInterfaceViewModel;

    /// <summary>
    /// Opens the expression builder on a parameter's default. A default is read as one value at the
    /// call site, so it can use the same functions and variables as any other whole-value field.
    /// </summary>
    private async void OnOpenDefaultExpression(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MacroParameterRow row }
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (await ExpressionBuilderWindow.ShowFor(owner, row.Default,
                Model?.ValueVariables ?? []) is { } built)
        {
            row.Default = built;
        }
    }

    /// <summary>
    /// Inserts a variable into a parameter's default at the caret. The default is evaluated in the
    /// caller's scope, so the list is the global and system values every caller has; a caller's own
    /// local name can still be typed by hand.
    /// </summary>
    private async void OnPickDefaultVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MacroParameterRow row } button
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (await VariablePickerWindow.ShowFor(owner, Model?.ValueVariables ?? [])
                is not { Token.Length: > 0 } picked)
        {
            return;
        }

        var text = row.Default;
        var caret = button.FindAncestorOfType<Grid>(true)?.GetVisualDescendants()
            .OfType<TextBox>().FirstOrDefault()?.CaretIndex;
        var at = Math.Clamp(caret ?? text.Length, 0, text.Length);
        row.Default = text[..at] + picked.Token + text[at..];
    }
}
