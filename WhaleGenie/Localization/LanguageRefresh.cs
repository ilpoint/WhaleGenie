using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace WhaleGenie.Localization;

/// <summary>
/// Walks the open windows when the language changes and asks every object they show to re-read
/// its text. Objects that compute a label from the table refresh when their bindings are told
/// about it; this is what tells them, without every window having to subscribe and unsubscribe.
/// </summary>
public static class LanguageRefresh
{
    public static void Refresh()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var window in desktop.Windows)
        {
            if (window.DataContext is ILanguageAware data && seen.Add(data))
            {
                data.RefreshLanguage();
            }

            foreach (var control in window.GetVisualDescendants().OfType<Control>())
            {
                if (control.DataContext is ILanguageAware shown && seen.Add(shown))
                {
                    shown.RefreshLanguage();
                }
            }
        }
    }
}
