using System.Linq;
using Avalonia.Controls;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Finding a control in the settings window when the pages it sits on have been split into views
/// of their own. The window's own name scope stops at the page views, so a control named there is
/// looked up where it actually lives rather than where the reader expects the window to hold it.
/// </summary>
internal static class SettingsView
{
    public static T? Find<T>(SettingsWindow window, string name) where T : Control =>
        Pages(window).Select(page => page.FindControl<T>(name)).FirstOrDefault(found => found is not null);

    private static Control[] Pages(SettingsWindow window) =>
    [
        window.FindControl<SettingsSystemView>("SystemPages")!,
        window.FindControl<SettingsTriggersView>("TriggerPages")!,
        window.FindControl<SettingsAppearanceView>("AppearancePages")!,
    ];
}
