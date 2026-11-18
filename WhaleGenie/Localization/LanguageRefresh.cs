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

        // The list is walked from a copy: a window that closes itself while its text is being
        // re-read — a picker putting itself away, say — would change the collection under the
        // loop, and that throws in the middle of the switch. Nothing is caught here on purpose: an
        // object that cannot re-read its text is a real fault, and reporting it to the person who
        // asked for the other language is better than half the interface quietly relabelled.
        foreach (var window in desktop.Windows.ToList())
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
