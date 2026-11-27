using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace WhaleGenie.Web;

/// <summary>
/// The part of an overlay that only Windows can answer: where the pointer is, which band the window
/// belongs to, and the styles and region that make it transparent, click-through or unable to take
/// the keyboard. Kept beside the navigation half so neither file has to hold both readings.
/// </summary>
internal sealed partial class WebOverlayWindow
{
    private bool _noActivate;
    private bool _clickThrough;

    /// <summary>
    /// Puts the window directly above the target, in the target's own band: a program dragged over
    /// the target covers the overlay as well, which is what "the same layer" means for an interface
    /// panel. The target's predecessor is used when it sits in the same band; otherwise the band's
    /// own top is asked for, because inserting after a topmost window would make an ordinary overlay
    /// topmost, and inserting after nothing would send it to the very bottom of every window.
    /// </summary>
    internal void PlaceAbove(long targetHandle)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || targetHandle == 0)
        {
            return;
        }

        var target = new IntPtr(targetHandle);
        var topmost = IsTopmost(target);
        var above = GetWindow(target, GwHwndPrev);
        var after = above != IntPtr.Zero && IsTopmost(above) == topmost
            ? above
            : topmost ? HwndTopmost : HwndNotTopmost;
        SetWindowPos(handle, after, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    private static bool IsTopmost(IntPtr window)
        => window != IntPtr.Zero
            && (GetWindowLongPtr(window, GwlExStyle).ToInt64() & WsExTopmost) != 0;

    /// <summary>
    /// Starts a system caption drag. A page cannot move a window itself: pointer events stop at the
    /// window edge, so dragging a small panel across the screen needs the OS move loop.
    /// </summary>
    internal void BeginWindowDrag()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
    }

    /// <summary>Where the pointer is, or null when there is no desktop to ask.</summary>
    private static PixelPoint? TryCursor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A menu or panel must not take the keyboard until its search is opened.</summary>
    internal void SetNoActivate(bool value)
    {
        _noActivate = value;
        ApplyStyles();
    }

    /// <summary>The wheel is driven by the global hook; the pointer belongs to whatever is behind it.</summary>
    internal void SetClickThrough(bool value)
    {
        _clickThrough = value;
        ApplyStyles();
    }

    /// <summary>
    /// Puts the window at the top of the topmost band. A window that is hidden and shown again
    /// keeps the place it had in the band, so anything that became topmost while it was away —
    /// the game the wheel is opened over, a pinned window, a panel — stays above it. A no-activate
    /// window is never raised by a click either, which leaves a re-shown overlay looking like it
    /// opened underneath everything. Every host that shows its window again asks for the band.
    /// </summary>
    internal void EnsureTopmost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    /// <summary>Whether a screen point (physical pixels) lies inside this window.</summary>
    internal bool ContainsScreen(int x, int y)
    {
        var scale = RenderScaling;
        var position = Position;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        return x >= position.X && y >= position.Y
            && x < position.X + width && y < position.Y + height;
    }

    /// <summary>
    /// Clips the window to the card it draws: the exact height after Windows refuses a very short
    /// one, and the card's rounded corners. Without the round region the window is a rectangle and
    /// the WebView's background fills the corners the page left transparent.
    /// </summary>
    internal void ClipToBody(int width, int height, int cornerRadius = 0)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        var scale = RenderScaling;
        var right = (int)Math.Round(width * scale) + 1;
        var bottom = (int)Math.Round(height * scale) + 1;
        var corner = Math.Max(0, (int)Math.Round(cornerRadius * scale));
        var region = corner > 0
            ? CreateRoundRectRgn(0, 0, right, bottom, corner, corner)
            : CreateRectRgn(0, 0, right, bottom);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void ApplyStyles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style = _noActivate
            ? style | WsExNoActivate | WsExToolWindow
            : style & ~(long)WsExNoActivate;
        style = _clickThrough ? style | WsExTransparent : style & ~(long)WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int GwHwndPrev = 3;
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoActivate = 0x0010;
    private const int SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx,
        int cy, int flags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    // A window region is made with GDI and handed to the window manager: CreateRectRgn and
    // DeleteObject are gdi32, SetWindowRgn is user32. Asking user32 for the first two throws
    // EntryPointNotFoundException, which used to end Show() before the panel was placed.
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
        int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
