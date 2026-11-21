using System;
using Avalonia;
using Avalonia.Controls;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

/// <summary>
/// The size a window reopens at. Position is deliberately not restored: a monitor may have gone,
/// and a window must not open off-screen. The web shell and the native main window use the same
/// setting, so switching between them does not lose the size the person chose.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Applies the remembered size, limited to the desktop that is here now.</summary>
    public static void Restore(Window window)
    {
        if (LocalSettings.LoadMainWindowSize() is not { } size)
        {
            return;
        }

        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        var scale = screen is { Scaling: > 0 } ? screen.Scaling : 1;
        var widest = screen is null ? size.Width : screen.WorkingArea.Width / scale;
        var tallest = screen is null ? size.Height : screen.WorkingArea.Height / scale;

        window.Width = Math.Clamp(size.Width, window.MinWidth, Math.Max(window.MinWidth, widest));
        window.Height = Math.Clamp(size.Height, window.MinHeight, Math.Max(window.MinHeight, tallest));
    }

    /// <summary>Remembers the ordinary size; a maximized or minimized size is not a choice.</summary>
    public static void Remember(Window window)
    {
        if (window.WindowState == WindowState.Normal)
        {
            LocalSettings.StoreMainWindowSize(window.Width, window.Height);
        }
    }
}
