using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// What the macro itself is: its name and description, how it is called, the triggers that start
/// it, and the variables it offers to the interface. The window keeps the drawing area and the
/// save buttons; this view is the drawer beside them.
/// </summary>
public partial class MacroSettingsView : UserControl
{
    public MacroSettingsView() => InitializeComponent();

    // What a click means is a decision about the whole macro — which value a picker fills, which
    // trigger is being edited — so the window that owns this drawer answers it.
    private MacroEditorWindow? Host => this.FindAncestorOfType<MacroEditorWindow>();

    private void OnSettingsPageClicked(object? sender, RoutedEventArgs e) => Host?.OnSettingsPageClicked(sender, e);

    private void OnDeleteSubMacro(object? sender, RoutedEventArgs e) => Host?.OnDeleteSubMacro(sender, e);

    private void OnAddTriggerMenu(object? sender, RoutedEventArgs e) => Host?.OnAddTriggerMenu(sender, e);

    private void OnPickColorClicked(object? sender, RoutedEventArgs e) => Host?.OnPickColorClicked(sender, e);

    private void OnPickTriggerWindow(object? sender, RoutedEventArgs e) => Host?.OnPickTriggerWindow(sender, e);

    private void OnSetInterfaceHotkey(object? sender, RoutedEventArgs e) => Host?.OnSetInterfaceHotkey(sender, e);

    private void OnOpenHotstringKeyboard(object? sender, RoutedEventArgs e) => Host?.OnOpenHotstringKeyboard(sender, e);

    private void OnCaptureHotstringEnd(object? sender, RoutedEventArgs e) => Host?.OnCaptureHotstringEnd(sender, e);

    private void OnSetToggleKey(object? sender, RoutedEventArgs e) => Host?.OnSetToggleKey(sender, e);
}
