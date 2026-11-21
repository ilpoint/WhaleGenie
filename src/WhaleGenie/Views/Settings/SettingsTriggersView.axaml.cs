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

    // Two windows can own this page during the settings migration: the old all-in-one window and
    // the advanced window that replaced it. Each handler asks whichever one is above it.
    private SettingsWindow? Host => this.FindAncestorOfType<SettingsWindow>();

    private AdvancedSettingsWindow? AdvancedHost => this.FindAncestorOfType<AdvancedSettingsWindow>();

    private void OnAddMenuRule(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnAddMenuRule(sender, e);
        }
        else
        {
            AdvancedHost?.OnAddMenuRule(sender, e);
        }
    }

    private void OnEditMenuRule(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnEditMenuRule(sender, e);
        }
        else
        {
            AdvancedHost?.OnEditMenuRule(sender, e);
        }
    }

    private void OnRemoveMenuRule(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnRemoveMenuRule(sender, e);
        }
        else
        {
            AdvancedHost?.OnRemoveMenuRule(sender, e);
        }
    }

    private void OnAddMenuRulePage(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnAddMenuRulePage(sender, e);
        }
        else
        {
            AdvancedHost?.OnAddMenuRulePage(sender, e);
        }
    }

    private void OnMoveMenuRuleUp(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnMoveMenuRuleUp(sender, e);
        }
        else
        {
            AdvancedHost?.OnMoveMenuRuleUp(sender, e);
        }
    }

    private void OnMoveMenuRuleDown(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnMoveMenuRuleDown(sender, e);
        }
        else
        {
            AdvancedHost?.OnMoveMenuRuleDown(sender, e);
        }
    }

    private void OnCaptureWheelRepeat(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnCaptureWheelRepeat(sender, e);
        }
        else
        {
            AdvancedHost?.OnCaptureWheelRepeat(sender, e);
        }
    }

    private void OnEnableSystem(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnEnableSystem(sender, e);
        }
        else
        {
            AdvancedHost?.OnEnableSystem(sender, e);
        }
    }

    private void OnPreviewWheel(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
        {
            host.OnPreviewWheel(sender, e);
        }
        else
        {
            AdvancedHost?.OnPreviewWheel(sender, e);
        }
    }
}
