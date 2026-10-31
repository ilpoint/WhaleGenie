using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The accent picker. It is shown over the settings window and applies every change to the running
/// program at once; closing it writes the colour that was being previewed.
/// </summary>
public partial class ThemePaletteWindow : AppWindow
{
    private readonly ThemePaletteViewModel _viewModel;

    public ThemePaletteWindow()
        : this(new ThemePaletteViewModel())
    {
    }

    internal ThemePaletteWindow(ThemePaletteViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Title = Strings.Get("Theme.Palette.Title");

        Closed += (_, _) => _viewModel.Flush();
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();


}
