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

    private AdvancedSettingsWindow? AdvancedHost => this.FindAncestorOfType<AdvancedSettingsWindow>();

    private void OnCustomizeThemeClicked(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnCustomizeThemeClicked(sender, e);
        }
        else
        {
            AdvancedHost?.OnCustomizeThemeClicked(sender, e);
        }
    }
}
