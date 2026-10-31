using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace WhaleGenie.Runtime;

/// <summary>
/// Puts a window of this program in front of whatever is on screen, on behalf of a launch that has
/// already ended.
/// </summary>
/// <remarks>
/// Windows does not let a background program take the foreground. A window that asks for it while
/// the person is working in another program is refused, and its taskbar button flashes instead —
/// which is exactly what a second launch used to look like: it flashed, and nothing came up. There
/// are two ways round that, and both are used here because either can be unavailable. The launch
/// that asked hands its own foreground rights over before it ends
/// (<see cref="SingleInstance.AskToComeBack"/>), and the window joins the thread that holds the
/// foreground for as long as it takes to be let in. Raising it through the always-on-top flag is
/// the last resort, because it works even when both of those are refused.
/// </remarks>
internal static class WindowFront
{
    private const int ShowRestore = 9;

    /// <summary>Brings the window back from wherever it is and puts it in front.</summary>
    internal static void Bring(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { } platform)
        {
            return;
        }

        var handle = platform.Handle;
        if (handle == IntPtr.Zero || InFront(handle))
        {
            return;
        }

        ShowWindow(handle, ShowRestore);
        SetForegroundWindow(handle);
        if (InFront(handle))
        {
            return;
        }

        // While attached, this window's request comes from the same input queue as the window the
        // person is working in, so it is no longer a background program asking.
        JoinForeground(handle);
        if (InFront(handle))
        {
            return;
        }

        // The system raises an always-on-top window itself. The flag comes off again at once, so
        // the window is not left above everything else.
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
    }

    private static bool InFront(IntPtr handle) => GetForegroundWindow() == handle;

    private static void JoinForeground(IntPtr handle)
    {
        var foreground = GetForegroundWindow();
        var owner = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        var here = GetCurrentThreadId();

        if (owner == 0 || owner == here || !AttachThreadInput(here, owner, true))
        {
            SetForegroundWindow(handle);
            return;
        }

        try
        {
            SetForegroundWindow(handle);
            BringWindowToTop(handle);
        }
        finally
        {
            AttachThreadInput(here, owner, false);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
