using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>The action browser that stands on the editor's left: a category dock, a search box, and the actions of the chosen category.</summary>
public partial class ActionPaletteView : UserControl
{
    public ActionPaletteView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when a card was clicked, so the editor can show the form for it.</summary>
    internal event Action? ActionChosen;

    private void OnPickCategory(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionCategoryChoice choice }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.SelectedCategory = choice;
        }
    }
    private void OnToggleGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionGroupViewModel group }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.ToggleGroup(group);
        }
    }

    private void OnPickAction(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionCardViewModel card }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.SelectAction(card.Key);
            ActionChosen?.Invoke();
        }
    }
}
