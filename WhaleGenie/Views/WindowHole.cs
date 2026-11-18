using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace WhaleGenie.Views;

/// <summary>
/// Cuts the middle out of a window, leaving a frame a few pixels thick. Whatever is underneath
/// then belongs to whoever is underneath — both for a hit test and for the pointer — which is what
/// lets a frame sit over a control without taking it.
/// </summary>
internal static class WindowHole
{
    /// <summary>The region mode that subtracts one rectangle from another.</summary>
    private const int Difference = 4;

    /// <summary>
    /// Leaves <paramref name="frame"/> pixels of border on <paramref name="window"/> and takes the
    /// rest away. Nothing happens where there is no window to cut, which is what a test session
    /// with no real desktop gets: the frame is then a plain rectangle, which is enough to be seen.
    /// </summary>
    public static void Cut(Window window, int width, int height, int frame)
    {
        if (!OperatingSystem.IsWindows()
            || window.TryGetPlatformHandle() is not { } platform
            || platform.Handle == IntPtr.Zero)
        {
            return;
        }

        var border = CreateRectRgn(0, 0, width, height);
        var hole = CreateRectRgn(frame, frame, width - frame, height - frame);
        if (border == IntPtr.Zero || hole == IntPtr.Zero)
        {
            return;
        }

        CombineRgn(border, border, hole, Difference);
        DeleteObject(hole);

        // The window takes the region over, so it must not be freed here.
        SetWindowRgn(platform.Handle, border, true);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr target, IntPtr first, IntPtr second, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
}
