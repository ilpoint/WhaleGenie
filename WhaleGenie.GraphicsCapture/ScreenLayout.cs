using System.Runtime.InteropServices;

namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// One rectangle of the desktop, in the coordinates the desktop is laid out in: what is to the left
/// of the primary display, or above it, is counted in negative numbers, which is the same thing a
/// mouse position and a macro's own coordinates are written in.
/// </summary>
public readonly record struct ScreenArea(int Left, int Top, int Width, int Height)
{
    /// <summary>One past the right-hand edge.</summary>
    public int Right => Left + Width;

    /// <summary>One past the bottom edge.</summary>
    public int Bottom => Top + Height;

    /// <summary>True for a rectangle with nothing in it.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Whether a point is inside.</summary>
    public bool Holds(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>One display, as Windows lays the desktop out.</summary>
public sealed record ScreenDisplay(ScreenArea Bounds, bool Primary);

/// <summary>
/// The desktop as Windows lays it out: one rectangle covering every display, which is what "the
/// whole screen" means, and the displays it is made of. More than one display is a desktop wider or
/// taller than any single one of them, and reading it takes a piece from each.
/// </summary>
public sealed record ScreenLayout(ScreenArea Bounds, IReadOnlyList<ScreenDisplay> Displays)
{
    /// <summary>
    /// The display a point is on, or the primary one when the point is on none of them — which is
    /// what a window dragged off the edge of the desktop lands on, and the answer that keeps a
    /// step reading something rather than nothing.
    /// </summary>
    public ScreenDisplay At(int x, int y)
        => Displays.FirstOrDefault(display => display.Bounds.Holds(x, y))
            ?? Displays.FirstOrDefault(display => display.Primary)
            ?? Displays[0];
}

/// <summary>
/// Asking Windows what the desktop is made of. The desktop's own rectangle covers every display,
/// which is the rectangle GDI and the duplication work in; the displays themselves are what a way
/// that reads one display at a time needs.
/// </summary>
internal static class Displays
{
    /// <summary>How the desktop is laid out right now.</summary>
    public static ScreenLayout Read()
    {
        var found = new List<ScreenDisplay>();

        // A local function rather than a delegate written where it is used: the walker Windows calls
        // hands the monitor's own rectangle over by reference, and a lambda cannot take one.
        bool Walk(IntPtr monitor, IntPtr context, ref NativeRect area, IntPtr data)
        {
            found.Add(Of(monitor, area));
            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Walk, IntPtr.Zero);

        if (found.Count == 0)
        {
            // A machine that will not enumerate its displays still knows how big the desktop is, and
            // one display covering it is what that has always meant.
            return new ScreenLayout(Whole(), [new ScreenDisplay(Whole(), true)]);
        }

        return new ScreenLayout(Whole(), found);
    }

    /// <summary>
    /// The desktop's own rectangle, which covers every display: the leftmost and topmost corners of
    /// the layout are the origin, and they are not the primary display's corner — a display to the
    /// left of it makes both negative.
    /// </summary>
    private static ScreenArea Whole()
    {
        var left = GetSystemMetrics(VirtualScreenLeft);
        var top = GetSystemMetrics(VirtualScreenTop);
        return new ScreenArea(left, top, GetSystemMetrics(VirtualScreenWidth),
            GetSystemMetrics(VirtualScreenHeight));
    }

    /// <summary>
    /// One display out of what Windows said about it. The rectangle the walker hands over is the
    /// same one <c>GetMonitorInfo</c> reports, so a monitor that refuses to say anything more still
    /// comes back with somewhere to read.
    /// </summary>
    private static ScreenDisplay Of(IntPtr monitor, NativeRect area)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return new ScreenDisplay(
                new ScreenArea(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top),
                false);
        }

        return new ScreenDisplay(
            new ScreenArea(info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top),
            (info.Flags & PrimaryFlag) != 0);
    }

    private const int VirtualScreenLeft = 76;
    private const int VirtualScreenTop = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;
    private const int PrimaryFlag = 1;

    private delegate bool MonitorWalker(IntPtr monitor, IntPtr context, ref NativeRect area, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr area,
        MonitorWalker walker, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
