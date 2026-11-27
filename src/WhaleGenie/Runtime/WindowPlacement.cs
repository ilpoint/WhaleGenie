using System;
using Avalonia;
using Avalonia.Controls;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

/// <summary>Which shell window a remembered size belongs to.</summary>
public enum WindowProfile
{
    Main,
    Workbench,
}

/// <summary>
/// The size a window reopens at. Position is deliberately not restored: a monitor may have gone,
/// and a window must not open off-screen. The web shell and the native main window use the same
/// setting, so switching between them does not lose the size the person chose; the workbench has
/// its own profile because it is a different window with a different useful shape.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Applies the remembered size, limited to the desktop that is here now.</summary>
    public static void Restore(Window window, AppSettings settings, WindowProfile profile = WindowProfile.Main)
    {
        var size = profile == WindowProfile.Workbench
            ? settings.LoadWorkbenchWindowSize()
            : settings.LoadMainWindowSize();
        if (size is { } chosen)
        {
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            var scale = screen is { Scaling: > 0 } ? screen.Scaling : 1;
            var widest = screen is null ? chosen.Width : screen.WorkingArea.Width / scale;
            var tallest = screen is null ? chosen.Height : screen.WorkingArea.Height / scale;

            window.Width = Math.Clamp(chosen.Width, window.MinWidth, Math.Max(window.MinWidth, widest));
            window.Height = Math.Clamp(chosen.Height, window.MinHeight, Math.Max(window.MinHeight, tallest));
        }

        // A maximized window is a choice too. Without this the shell reopened at the last normal
        // size after every launch, which read as the window shrinking by itself.
        var maximized = profile == WindowProfile.Workbench
            ? settings.LoadWorkbenchWindowMaximized()
            : settings.LoadMainWindowMaximized();
        if (maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Remembers the ordinary size; a maximized or minimized size is not a choice.</summary>
    public static void Remember(Window window, AppSettings settings, WindowProfile profile = WindowProfile.Main)
    {
        if (window.WindowState == WindowState.Normal)
        {
            if (profile == WindowProfile.Workbench)
            {
                settings.StoreWorkbenchWindowSize(window.Width, window.Height);
            }
            else
            {
                settings.StoreMainWindowSize(window.Width, window.Height);
            }
        }

        if (profile == WindowProfile.Workbench)
        {
            settings.StoreWorkbenchWindowMaximized(window.WindowState == WindowState.Maximized);
        }
        else
        {
            settings.StoreMainWindowMaximized(window.WindowState == WindowState.Maximized);
        }
    }
}
