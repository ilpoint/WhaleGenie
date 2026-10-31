using System;
using System.Collections;
using System.Reflection;
using FluentAvalonia.UI.Windowing;

namespace WhaleGenie.Views;

/// <summary>
/// Every window in WhaleGenie is one of these: FluentAvalonia's <see cref="FAAppWindow"/>, so the
/// title bar, the caption buttons and the rounded frame come from the library rather than from XAML
/// we maintain ourselves.
/// </summary>
public class AppWindow : FAAppWindow
{
    public AppWindow()
    {
        // The bar is the only place a window can be dragged by, so it is taller than the library's
        // 32: at that height the strip reads as an edge rather than as something to grab.
        TitleBar.Height = 40;
        ForgetHandlelessWindow();
    }

    /// <summary>
    /// Lets go of the library's window-manager entry when this window has no native handle.
    /// </summary>
    /// <remarks>
    /// <see cref="FAAppWindow"/> keeps a static table of window managers keyed by `HWND`. A headless
    /// session hands out `0` for every window, so the first window takes the only slot and the second
    /// one throws "an item with the same key has already been added" from its constructor — which is
    /// why the checks could never open two of these. A handle of zero means there is no window
    /// underneath at all, and nothing in that table can be reached, so it is emptied and the next
    /// window gets its turn. A real `HWND` is never zero, so on a real machine this never runs and
    /// the library keeps the entry it needs for the title-bar menu.
    ///
    /// The field is reached by name because the type is internal; if a later version renames it,
    /// nothing happens here, which leaves the same "no handle, no table" case this check is in.
    /// </remarks>
    private void ForgetHandlelessWindow()
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle != IntPtr.Zero)
        {
            return;
        }

        var manager = typeof(FAAppWindow).Assembly
            .GetType("FluentAvalonia.UI.Windowing.Win32WindowManager");
        var registry = manager
            ?.GetField("_appWindowRegistry", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null);

        (registry as IDictionary)?.Clear();
    }
}
