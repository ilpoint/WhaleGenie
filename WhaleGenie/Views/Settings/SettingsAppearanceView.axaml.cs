using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The page that says what the program looks like. The theme choice is bound; the button that
/// opens the custom theme asks the window, because the palette is a window of its own.
/// </summary>
public partial class SettingsAppearanceView : UserControl
{
    public SettingsAppearanceView() => InitializeComponent();

    private SettingsWindow? Host => this.FindAncestorOfType<SettingsWindow>();

    private void OnCustomizeThemeClicked(object? sender, RoutedEventArgs e)
        => Host?.OnCustomizeThemeClicked(sender, e);
}
