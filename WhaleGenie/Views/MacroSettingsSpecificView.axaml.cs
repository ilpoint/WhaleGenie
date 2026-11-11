using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// What the macro offers once it has started: the interface it shows, and the key that switches it
/// on and off while it runs.
/// </summary>
public partial class MacroSettingsSpecificView : UserControl
{
    public MacroSettingsSpecificView() => InitializeComponent();

    private MacroEditorWindow? Host => this.FindAncestorOfType<MacroEditorWindow>();

    private void OnSetToggleKey(object? sender, RoutedEventArgs e) => Host?.OnSetToggleKey(sender, e);
}
