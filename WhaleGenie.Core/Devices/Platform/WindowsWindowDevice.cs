using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    /// <summary>
    /// The rectangle a window is seen in rather than the one Windows resizes it by: the border kept
    /// for resizing is not drawn, so the two differ by a few pixels on every side of an ordinary
    /// window. It is the one a picture of the window starts at, and the one a frame around it
    /// belongs on.
    /// </summary>
    private const int SeenByTheEye = 9;

    public IReadOnlyList<WindowInfo> List()
    {
        Require();
        return EveryWindow();
    }

    /// <summary>
    /// The top-level window that currently has the foreground, with the rectangle it is seen at.
    /// The picker asks this only after the person has clicked the window they mean.
    /// </summary>
    public WindowInfo? Foreground()
    {
        Require();
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || !Seen(handle, out var bounds))
        {
            return null;
        }

        var title = TitleOf(handle);
        return new WindowInfo(handle.ToInt64(), title, new ScreenPoint(bounds.Left, bounds.Top),
            new ScreenSize(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
            IsIconic(handle), IsZoomed(handle));
    }

    public WindowInfo? Find(string value, WindowMatch match,
        string compare = TextCompare.Contains, TextCompareOptions options = default)
        => Filter(value, match, compare, options: options).FirstOrDefault();

    public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match,
        string compare = TextCompare.Contains, bool everyWhenEmpty = false,
        TextCompareOptions options = default)
    {
        Require();

        var wanted = (value ?? string.Empty).Trim();
        var windows = EveryWindow();

        // An empty value means "whatever is in front", which is what a macro usually means when it
        // says "the active window" — the one window a step that names none acts on. A step that
        // lists windows means every window by it instead, and says so.
        if (wanted.Length == 0)
        {
            return everyWhenEmpty || windows.Count == 0 ? windows : [windows[0]];
        }

        return [.. windows.Where(window => Matches(window, wanted, match, compare, options))];
    }

    /// <summary>
    /// Whether one window answers to the value a step wrote. Title is the cheap one, since the
    /// listing already holds it; a process name costs a lookup per window, so it is only paid for
    /// when a step actually asks to match that way. How the two are held up against each other is
    /// the same question for every device that recognises a window, so it lives in one place. A
    /// pattern the machine cannot read comes back as <see cref="ArgumentException"/>, which is the
    /// run's to report.
    /// </summary>
    private bool Matches(WindowInfo window, string wanted, WindowMatch match, string compare,
        TextCompareOptions options)
    {
        var text = match switch
        {
            WindowMatch.Process => ProcessOf(window.Handle),
            WindowMatch.ClassName => ClassOf(window.Handle),
            _ => window.Title,
        };

        return WindowHow.Matches(text, wanted, compare, options);
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
        if (!IsWindow(window))
        {
            return false;
        }

        // A window is moved and sized by its own rectangle, which is a few pixels wider and taller
        // than what is seen of it. A macro asking for a window's left edge to be here means the edge
        // it can see — the one window.info hands back — so the difference is added back on: moving a
        // window to the rectangle that was just read puts it where the rectangle said.
        var grown = 0;
        var taller = 0;
        var left = 0;
        var top = 0;
        // A shrunk window is not lying anywhere to have an edge, and its icon's rectangle is nothing
        // to measure against, so it is moved the way it always was.
        if (!IsIconic(window) && GetWindowRect(window, out var own) && Seen(window, out var seen))
        {
            left = own.Left - seen.Left;
            top = own.Top - seen.Top;
            grown = (own.Right - own.Left) - (seen.Right - seen.Left);
            taller = (own.Bottom - own.Top) - (seen.Bottom - seen.Top);
        }

        return MoveWindow(window, x - left, y - top, Math.Max(0, width + grown),
            Math.Max(0, height + taller), true);
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
            throw new DeviceActionException(RunKeys.WindowNotFound,
                handle.ToString(CultureInfo.InvariantCulture));
        }

        return new ScreenPoint(corner.X, corner.Y);
    }

    /// <summary>
    /// How big the part of a window inside its border is, through <c>GetClientRect</c>: the frame
    /// and the title bar are left out of it, so a search that names this window looks at the same
    /// pixels the window itself draws in. A window that closed since it was looked up is reported
    /// the same way as one that was never there.
    /// </summary>
    public ScreenSize ClientSize(long handle)
    {
        Require();

        var window = new IntPtr(handle);
        if (!IsWindow(window) || !GetClientRect(window, out var client))
        {
            throw new DeviceActionException(RunKeys.WindowNotFound,
                handle.ToString(CultureInfo.InvariantCulture));
        }

        return new ScreenSize(client.Right - client.Left, client.Bottom - client.Top);
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
            if (title.Length == 0 || !Seen(handle, out var bounds))
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

    /// <summary>
    /// Where a window is seen on the screen, which is the rectangle the eye has rather than the one
    /// Windows keeps for resizing it. A window that DWM has no answer for — it is not one DWM draws,
    /// or the answer does not apply — falls back to its own rectangle, which is the same thing on a
    /// window with no border to hide.
    /// </summary>
    private static bool Seen(IntPtr handle, out NativeRect bounds)
        => DwmGetWindowAttribute(handle, SeenByTheEye, out bounds, Marshal.SizeOf<NativeRect>()) == 0
           || GetWindowRect(handle, out bounds);

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
    private static extern IntPtr GetForegroundWindow();

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute,
        out NativeRect value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect bounds);

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
