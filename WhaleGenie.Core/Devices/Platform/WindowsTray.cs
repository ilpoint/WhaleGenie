using System;
using System.Runtime.InteropServices;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The icon in the notification area, and the shell messages that come from it.
///
/// The program is meant to keep running while its window is out of the way — a macro is triggered
/// by a key or a pixel, not by the window being open — so the icon is where the user finds it again
/// and where the only deliberate "stop" lives. It is also the one place the shell will show a
/// notification from, which is what makes it the machine's wording for a task finishing while
/// nobody is looking at the screen.
///
/// Written against the shell directly rather than through the interface toolkit, because the whole
/// point of it here is the balloon message, which the toolkit's own tray icon cannot send.
/// </summary>
public sealed class WindowsTray : INotificationArea
{
    /// <summary>
    /// The icon this program put there, for everything that has to reach it without holding on to
    /// it — the notification a macro asks for, above all. One program has one of these.
    /// </summary>
    public static WindowsTray? Shared { get; set; }

    /// <summary>The window the shell answers through; private to this icon.</summary>
    private readonly IntPtr _window;

    /// <summary>
    /// Where the icon was made. A balloon is asked for from wherever the macro happens to be
    /// running, and a macro step runs off the user-interface thread, so the call comes back here
    /// first: the window this icon belongs to is pumped by that thread, and the shell likes its
    /// messages answered where it put them.
    /// </summary>
    private readonly SynchronizationContext? _context;

    /// <summary>Kept alive for the lifetime of the window: Windows holds the raw function pointer.</summary>
    private readonly WindowProc _proc;

    private readonly IntPtr _icon;

    /// <summary>Whether the icon is ours to destroy; the one Windows falls back to is not.</summary>
    private readonly bool _ownsIcon;

    private readonly string _tooltip;

    private readonly string _showLabel;

    private readonly string _exitLabel;

    /// <summary>The window class, kept so the same name can be unregistered again.</summary>
    private readonly string _className;

    private bool _disposed;

    /// <summary>
    /// Puts an icon in the notification area. <paramref name="tooltip"/> is what a hover shows;
    /// the two labels are the entries of the menu a right-click opens.
    /// </summary>
    public WindowsTray(string tooltip, string showLabel, string exitLabel)
    {
        _tooltip = tooltip;
        _showLabel = showLabel;
        _exitLabel = exitLabel;
        _context = SynchronizationContext.Current;
        _icon = AppIcon(out _ownsIcon);

        _proc = OnMessage;
        var instance = GetModuleHandle(null);
        _className = $"WhaleGenie.Tray.{Environment.ProcessId}";

        var windowClass = new WindowClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = instance,
            lpszClassName = _className,
        };

        if (RegisterClass(ref windowClass) == 0)
        {
            throw new DeviceUnavailableException("the notification area");
        }

        // A hidden top-level window rather than a message-only one: the menu the shell opens is
        // owned by this window, and it has to be able to come to the front to be dismissed by
        // clicking elsewhere.
        _window = CreateWindowEx(0, _className, _className, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        if (_window == IntPtr.Zero)
        {
            UnregisterClass(_className, instance);
            throw new DeviceUnavailableException("the notification area");
        }

        var data = Data(NifMessage | NifIcon | NifTip);
        data.hIcon = _icon;
        data.szTip = Clip(tooltip, 127);

        if (!ShellNotify(NimAdd, ref data))
        {
            Dispose();
            throw new DeviceUnavailableException("the notification area");
        }

        Shared = this;
    }

    /// <summary>The user asked for the window: a click on the icon, or the menu's first entry.</summary>
    public event Action? Activated;

    /// <summary>The user asked the program to stop: the menu's last entry.</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// Shows a balloon over the icon. The shell times it out on its own, and a machine with its
    /// notifications switched off simply shows nothing — neither is a failure to report back.
    /// An empty title becomes the program's own name, so a macro only has to say what happened.
    /// </summary>
    public void Balloon(string title, string text, NotificationKind kind)
    {
        if (_disposed)
        {
            return;
        }

        if (_context is not null && !ReferenceEquals(SynchronizationContext.Current, _context))
        {
            _context.Post(_ => ShowBalloon(title, text, kind), null);
            return;
        }

        ShowBalloon(title, text, kind);
    }

    /// <summary>Puts the balloon on screen. Called where the icon lives.</summary>
    private void ShowBalloon(string title, string text, NotificationKind kind)
    {
        if (_disposed)
        {
            return;
        }

        var data = Data(NifInfo);
        data.szInfoTitle = Clip(title.Trim().Length == 0 ? _tooltip : title, 63);
        data.szInfo = Clip(text, 255);
        data.dwInfoFlags = kind switch
        {
            NotificationKind.Warning => NiifWarning,
            NotificationKind.Error => NiifError,
            _ => NiifInfo,
        };

        ShellNotify(NimModify, ref data);
    }

    /// <summary>Takes the icon out of the notification area and closes the window behind it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_window != IntPtr.Zero)
        {
            var data = Data(0);
            ShellNotify(NimDelete, ref data);
            DestroyWindow(_window);
            UnregisterClass(_className, GetModuleHandle(null));
        }

        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        if (ReferenceEquals(Shared, this))
        {
            Shared = null;
        }
    }

    /// <summary>
    /// Answers the two mouse messages the shell sends through the icon's own message number: a
    /// click means "show me the window", a right-click means "ask what I want to do".
    /// </summary>
    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam)
    {
        if (message == TrayMessage)
        {
            switch ((int)lparam)
            {
                case WmLButtonUp:
                case WmLButtonDoubleClick:
                    Activated?.Invoke();
                    break;

                case WmRButtonUp:
                    OpenMenu();
                    break;
            }
        }

        return DefWindowProc(window, message, wparam, lparam);
    }

    /// <summary>
    /// Opens the menu where the pointer is and carries out what was picked. The window is brought
    /// to the front first: without that the menu stays up until something else is clicked, which is
    /// the one Windows habit this has to work around.
    /// </summary>
    private void OpenMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, ShowCommand, _showLabel);
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, ExitCommand, _exitLabel);

            if (!GetCursorPos(out var point))
            {
                return;
            }

            SetForegroundWindow(_window);
            var chosen = TrackPopupMenu(
                menu, TpmRightButton | TpmBottomAlign | TpmReturnCommand,
                point.X, point.Y, 0, _window, IntPtr.Zero);

            // Dismissing the menu leaves this behind, and without it the next click anywhere is
            // eaten by the menu that is no longer there.
            PostMessage(_window, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (chosen == ShowCommand)
            {
                Activated?.Invoke();
            }
            else if (chosen == ExitCommand)
            {
                ExitRequested?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>A fresh notification record for the calls that fill in a few fields at a time.</summary>
    private NotifyIconData Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = TrayMessage,
        hIcon = IntPtr.Zero,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>
    /// The icon to put in the tray: the program's own, taken from the executable so the tray, the
    /// taskbar and the window all show the same picture. A copy Windows hands out when that fails
    /// is shared with the rest of the system and is not ours to destroy.
    /// </summary>
    private static IntPtr AppIcon(out bool owned)
    {
        var program = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(program))
        {
            try
            {
                if (ExtractIconEx(program, 0, out var large, out var small, 1) > 0)
                {
                    if (small != IntPtr.Zero)
                    {
                        if (large != IntPtr.Zero)
                        {
                            DestroyIcon(large);
                        }

                        owned = true;
                        return small;
                    }

                    if (large != IntPtr.Zero)
                    {
                        owned = true;
                        return large;
                    }
                }
            }
            catch (Exception)
            {
                // Not a program, no icon in it, or a shell that will not hand one out: the plain
                // system icon below is a better answer than a tray with nothing in it.
            }
        }

        owned = false;
        return LoadIcon(IntPtr.Zero, IdApplication);
    }

    /// <summary>Fits text into one of the record's fixed-size fields.</summary>
    private static string Clip(string text, int limit)
        => text.Length <= limit ? text : text[..limit];

    private static bool ShellNotify(int message, ref NotifyIconData data)
        => Shell_NotifyIcon(message, ref data);

    // The shell's own numbers. They are spelled out because they are the only place these names
    // exist: the notification area has no managed API of its own.
    private const int WmNull = 0x0000;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDoubleClick = 0x0203;

    /// <summary>Where the icon's own messages start; the shell sends the mouse messages below it.</summary>
    private const int TrayMessage = 0x8000 + 1;

    private const int NimAdd = 0;
    private const int NimModify = 1;
    private const int NimDelete = 2;

    private const int NifMessage = 0x01;
    private const int NifIcon = 0x02;
    private const int NifTip = 0x04;
    private const int NifInfo = 0x10;

    private const int NiifInfo = 0x01;
    private const int NiifWarning = 0x02;
    private const int NiifError = 0x03;

    private const int MfString = 0x0000;
    private const int MfSeparator = 0x0800;
    private const int TpmRightButton = 0x0002;
    private const int TpmBottomAlign = 0x0020;
    private const int TpmReturnCommand = 0x0100;

    private const int ShowCommand = 1;
    private const int ExitCommand = 2;

    private static readonly IntPtr IdApplication = new(32512);

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

    /// <summary>What the window class has to say about itself before Windows will make one.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    /// <summary>
    /// The record the shell keeps about one icon: who owns it, what it looks like, and — when a
    /// balloon is being shown — the words and the kind of balloon. Every field is written out in
    /// order, because the order is all the shell knows about it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassW")]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu, int flags, int x, int y, int reserved, IntPtr window, IntPtr area);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern int ExtractIconEx(
        string file, int index, out IntPtr large, out IntPtr small, int count);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? module);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
