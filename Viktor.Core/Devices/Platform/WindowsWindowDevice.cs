using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// The windows on the desktop, through the Win32 calls. Only the top-level windows a person
/// can actually see are listed, so a macro talks about window titles rather than handles,
/// and never trips over the invisible helper windows programs keep to themselves.
/// </summary>
public sealed class WindowsWindowDevice : IWindowDevice
{
    private const int ShowMinimized = 6;
    private const int ShowMaximized = 3;
    private const int ShowRestore = 9;
    private const uint CloseMessage = 0x0010;

    public IReadOnlyList<WindowInfo> List()
    {
        Require();
        return EveryWindow();
    }

    public WindowInfo? Find(string title)
    {
        Require();

        var wanted = (title ?? string.Empty).Trim();
        var windows = EveryWindow();

        // An empty title means "whatever is in front", which is what a macro usually means
        // when it says "the active window".
        if (wanted.Length == 0)
        {
            return windows.Count > 0 ? windows[0] : null;
        }

        foreach (var window in windows)
        {
            if (window.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return null;
    }

    public bool Activate(long handle)
    {
        Require();

        var window = new IntPtr(handle);
        if (!IsWindow(window))
        {
            return false;
        }

        if (IsIconic(window))
        {
            ShowWindow(window, ShowRestore);
        }

        // Windows only lets the program that already has focus hand it on, so a refusal here
        // is not treated as a failure: the window is still raised as far as the rules allow.
        SetForegroundWindow(window);
        BringWindowToTop(window);
        return true;
    }

    public bool Minimize(long handle) => Show(handle, ShowMinimized);

    public bool Maximize(long handle) => Show(handle, ShowMaximized);

    public bool Restore(long handle) => Show(handle, ShowRestore);

    public bool Close(long handle)
    {
        Require();

        var window = new IntPtr(handle);
        return IsWindow(window) && PostMessage(window, CloseMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public bool Move(long handle, int x, int y, int width, int height)
    {
        Require();

        var window = new IntPtr(handle);
        return IsWindow(window)
               && MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
    }

    private static bool Show(long handle, int command)
    {
        Require();

        var window = new IntPtr(handle);
        return IsWindow(window) && ShowWindow(window, command);
    }

    /// <summary>Every visible window that has a title, frontmost first.</summary>
    private static List<WindowInfo> EveryWindow()
    {
        var windows = new List<WindowInfo>();
        var shell = GetShellWindow();

        EnumWindows((handle, _) =>
        {
            if (handle == shell || !IsWindowVisible(handle))
            {
                return true;
            }

            var title = TitleOf(handle);
            if (title.Length == 0 || !GetWindowRect(handle, out var bounds))
            {
                return true;
            }

            windows.Add(new WindowInfo(
                handle.ToInt64(),
                title,
                new ScreenPoint(bounds.Left, bounds.Top),
                new ScreenSize(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
                IsIconic(handle),
                IsZoomed(handle)));

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static string TitleOf(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder(length + 1);
        return GetWindowText(handle, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
    }

    private static void Require()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("windows");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height,
        bool repaint);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
