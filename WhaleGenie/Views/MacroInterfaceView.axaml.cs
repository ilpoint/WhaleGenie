using Avalonia.Controls;
using Avalonia.Interactivity;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The form part of a macro's own interface: its parameters and results, and the one tool a
/// default value needs. The dialog only reports text; the row that opened it keeps it.
/// </summary>
public partial class MacroInterfaceView : UserControl
{
    public MacroInterfaceView()
    {
        InitializeComponent();
    }

    private MacroInterfaceViewModel? Model => DataContext as MacroInterfaceViewModel;

    /// <summary>
    /// Opens the value picker on a parameter's default. A default is read as one value at the call
    /// site, so it can use the same functions and variables as any other whole-value field — one
    /// door, with the variables on the same page as the functions.
    /// </summary>
    private async void OnOpenDefaultExpression(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MacroParameterRow row }
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (await ValuePickerWindow.ShowFor(owner, row.Default,
                Model?.ValueVariables ?? [], startOnFormula: true) is { } built)
        {
            row.Default = built;
        }
    }
}
