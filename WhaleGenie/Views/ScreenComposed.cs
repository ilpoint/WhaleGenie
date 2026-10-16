using System;
using System.Runtime.InteropServices;

namespace WhaleGenie.Views;

/// <summary>
/// Waits until the desktop has been drawn again.
/// </summary>
/// <remarks>
/// A picture of the screen taken the moment a window left is taken too early: the screen is asked
/// what it is showing, and the answer is the last frame it drew — the one that still has that
/// window in it. Waiting for composition is what keeps such a window out of the picture.
/// </remarks>
internal static class ScreenComposed
{
    /// <summary>
    /// Blocks until the desktop has been composed twice more, which is one frame past the one that
    /// still had the window in it.
    /// </summary>
    public static void Wait()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            DwmFlush();
            DwmFlush();
        }
        catch (Exception)
        {
            // A machine that composes some other way has nothing to wait for, and a picture taken
            // without waiting is what it would have been anyway.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
