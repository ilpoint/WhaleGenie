using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace WhaleGenie.Core.Devices.Platform;

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

    public WindowInfo? Find(string value, WindowMatch match,
        WindowCompare compare = WindowCompare.Contains)
    {
        Require();

        var wanted = (value ?? string.Empty).Trim();
        var windows = EveryWindow();

        // An empty value means "whatever is in front", which is what a macro usually means
        // when it says "the active window".
        if (wanted.Length == 0)
        {
            return windows.Count > 0 ? windows[0] : null;
        }

        foreach (var window in windows)
        {
            if (Matches(window, wanted, match, compare))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether one window answers to the value a step wrote. Title is the cheap one, since the
    /// listing already holds it; a process name costs a lookup per window, so it is only paid for
    /// when a step actually asks to match that way. A pattern the machine cannot read comes back
    /// as <see cref="ArgumentException"/>, which is the run's to report.
    /// </summary>
    private bool Matches(WindowInfo window, string wanted, WindowMatch match, WindowCompare compare)
    {
        var text = match switch
        {
            WindowMatch.Process => ProcessOf(window.Handle),
            WindowMatch.ClassName => ClassOf(window.Handle),
            _ => window.Title,
        };

        return compare switch
        {
            WindowCompare.StartsWith => text.StartsWith(wanted, StringComparison.OrdinalIgnoreCase),
            WindowCompare.Regex => Regex.IsMatch(text, wanted, RegexOptions.IgnoreCase),
            _ => text.Contains(wanted, StringComparison.OrdinalIgnoreCase),
        };
    }

    public string ProcessOf(long handle) => ProcessName(handle);

    /// <summary>
    /// The class a window was registered under, which is the kind of window rather than the
    /// document in it: every Notepad window is "Notepad", however it was renamed.
    /// </summary>
    public string ClassOf(long handle)
    {
        Require();

        var name = new StringBuilder(256);
        var window = new IntPtr(handle);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
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

    /// <summary>
    /// Where the client area starts in screen pixels, through <c>ClientToScreen</c> so the border
    /// and the title bar are left out. A window that closed since it was looked up is reported the
    /// same way as one that was never there, which is what the engine turns into a step failure.
    /// </summary>
    public ScreenPoint ClientOrigin(long handle)
    {
        Require();

        var window = new IntPtr(handle);
        var corner = new NativePoint();
        if (!IsWindow(window) || !ClientToScreen(window, ref corner))
        {
            throw new DeviceActionException("Run.WindowNotFound",
                handle.ToString(CultureInfo.InvariantCulture));
        }

        return new ScreenPoint(corner.X, corner.Y);
    }

    /// <summary>
    /// The program behind a window handle, for the picker that lists what is open. It answers
    /// with an empty string rather than failing: a window can outlive the process lookup, and
    /// the picker only needs it as a label.
    /// </summary>
    public static string ProcessName(long handle)
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            GetWindowThreadProcessId(new IntPtr(handle), out var processId);
            if (processId == 0)
            {
                return string.Empty;
            }

            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return string.Empty;
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder name, int count);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

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
