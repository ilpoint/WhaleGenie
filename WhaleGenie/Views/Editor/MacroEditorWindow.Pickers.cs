using Avalonia.Interactivity;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // The two trigger looks that step the whole program aside: a colour under the pointer and the
    // window a rule should watch. The look itself is in EditorPickers; this window only opens it.

    /// <summary>Opens the magnifier, which follows the pointer and reads the pixel under it.</summary>
    internal async void OnPickColorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MacroEditorViewModel viewModel)
        {
            await _pickers.PickColourAsync(viewModel);
        }
    }

    /// <summary>Captures the window a window trigger should watch.</summary>
    internal async void OnPickTriggerWindow(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MacroEditorViewModel viewModel)
        {
            await _pickers.PickWindowAsync(viewModel);
        }
    }
}
