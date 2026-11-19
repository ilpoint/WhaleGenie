using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// What the macro is called, what it does and how it starts: the name, description and group, the
/// triggers it answers to, and the settings of the trigger picked in the list. The window owns the
/// trigger entries; this page only says what they look like.
/// </summary>
public partial class MacroSettingsGeneralView : UserControl
{
    public MacroSettingsGeneralView() => InitializeComponent();

    private MacroEditorWindow? Host => this.FindAncestorOfType<MacroEditorWindow>();

    private void OnDeleteSubMacro(object? sender, RoutedEventArgs e) => Host?.OnDeleteSubMacro(sender, e);

    private void OnAddTriggerMenu(object? sender, RoutedEventArgs e) => Host?.OnAddTriggerMenu(sender, e);

    private void OnPickColorClicked(object? sender, RoutedEventArgs e) => Host?.OnPickColorClicked(sender, e);

    private void OnPickTriggerWindow(object? sender, RoutedEventArgs e) => Host?.OnPickTriggerWindow(sender, e);

    private void OnOpenHotstringKeyboard(object? sender, RoutedEventArgs e) => Host?.OnOpenHotstringKeyboard(sender, e);

    private void OnCaptureHotstringEnd(object? sender, RoutedEventArgs e) => Host?.OnCaptureHotstringEnd(sender, e);

    private void OnSetInterfaceHotkey(object? sender, RoutedEventArgs e) => Host?.OnSetInterfaceHotkey(sender, e);
}
