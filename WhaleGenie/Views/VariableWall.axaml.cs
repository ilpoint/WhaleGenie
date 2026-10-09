using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The wall of variable buttons, used wherever a variable has to be chosen: on its own in the
/// picker, and inside the expression builder where it drops the name in at the caret. It holds no
/// state of its own — which variables there are and which are on show is the view model's business —
/// so the same wall can stand in both places without either knowing about the other.
/// </summary>
public partial class VariableWall : UserControl
{
    public VariableWall()
    {
        InitializeComponent();
    }

    private void OnChoose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VariableWallViewModel wall
            && sender is Control { DataContext: VariableChoice choice })
        {
            wall.ChooseCommand.Execute(choice);
        }
    }

    /// <summary>The pointer is over a variable, so the line under the buttons says what it is.</summary>
    private void OnLooking(object? sender, PointerEventArgs e)
    {
        if (DataContext is VariableWallViewModel wall
            && sender is Control { DataContext: VariableChoice choice })
        {
            wall.Looking = choice;
        }
    }

    private void OnUnlooked(object? sender, PointerEventArgs e)
    {
        if (DataContext is VariableWallViewModel wall)
        {
            wall.Looking = null;
        }
    }
}
