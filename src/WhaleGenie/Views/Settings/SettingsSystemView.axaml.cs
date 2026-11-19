using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The pages that name a machine setting: general, hotkeys, screen capture, input and devices.
/// Which page is open is decided by the window; these panels only say what each one holds, so a
/// page can be read without the list and the chrome around it.
/// </summary>
public partial class SettingsSystemView : UserControl
{
    public SettingsSystemView() => InitializeComponent();

    // The window wires these up itself, and decides what each button does.
    private SettingsWindow? Host => this.FindAncestorOfType<SettingsWindow>();

    private void OnOpenStorage(object? sender, RoutedEventArgs e) => Host?.OnOpenStorage(sender, e);

    private void OnInstallDriverClicked(object? sender, RoutedEventArgs e) => Host?.OnInstallDriverClicked(sender, e);

    private void OnRestartElevatedClicked(object? sender, RoutedEventArgs e) => Host?.OnRestartElevatedClicked(sender, e);
}
