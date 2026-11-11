using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The drawer beside the step list: its title, the two pages it switches between, and the save and
/// run buttons under them. What the macro is lives on the general page, what it offers while it
/// runs on the specific one; each is a view of its own so a page can be read without the other.
/// </summary>
public partial class MacroSettingsView : UserControl
{
    public MacroSettingsView() => InitializeComponent();

    // What a click means is a decision about the whole macro — which value a picker fills, which
    // trigger is being edited — so the window that owns this drawer answers it.
    private MacroEditorWindow? Host => this.FindAncestorOfType<MacroEditorWindow>();

    /// <summary>
    /// A control the drawer holds, on its own frame or on one of its two pages. The window reaches
    /// through here because a page keeps its names in a scope of its own.
    /// </summary>
    internal T? Find<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? this.FindControl<MacroSettingsGeneralView>("GeneralSettings")?.FindControl<T>(name)
        ?? this.FindControl<MacroSettingsSpecificView>("SpecificSettings")?.FindControl<T>(name);

    private void OnSettingsPageClicked(object? sender, RoutedEventArgs e) => Host?.OnSettingsPageClicked(sender, e);
}
