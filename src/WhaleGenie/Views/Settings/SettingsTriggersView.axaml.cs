using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The pages that say what the program does when something outside it happens: the menu launcher,
/// the window rules, and the wheel with the actions on it. The window keeps the page switch; the
/// rule list and the wheel editor are handed back to it because a drag and a drawn wheel need the
/// window's own state.
/// </summary>
public partial class SettingsTriggersView : UserControl
{
    public SettingsTriggersView() => InitializeComponent();

    private AdvancedSettingsWindow? Host => this.FindAncestorOfType<AdvancedSettingsWindow>();

    private void OnAddMenuRule(object? sender, RoutedEventArgs e) => Host?.OnAddMenuRule(sender, e);

    private void OnEditMenuRule(object? sender, RoutedEventArgs e) => Host?.OnEditMenuRule(sender, e);

    private void OnRemoveMenuRule(object? sender, RoutedEventArgs e) => Host?.OnRemoveMenuRule(sender, e);

    private void OnAddMenuRulePage(object? sender, RoutedEventArgs e) => Host?.OnAddMenuRulePage(sender, e);

    private void OnMoveMenuRuleUp(object? sender, RoutedEventArgs e) => Host?.OnMoveMenuRuleUp(sender, e);

    private void OnMoveMenuRuleDown(object? sender, RoutedEventArgs e) => Host?.OnMoveMenuRuleDown(sender, e);

    private void OnCaptureWheelRepeat(object? sender, RoutedEventArgs e) => Host?.OnCaptureWheelRepeat(sender, e);

    private void OnEnableSystem(object? sender, RoutedEventArgs e) => Host?.OnEnableSystem(sender, e);

    private void OnPreviewWheel(object? sender, RoutedEventArgs e) => Host?.OnPreviewWheel(sender, e);
}
