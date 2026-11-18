using System.Runtime.InteropServices;

namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// The desktop as the screen shows it: rectangles copied straight out of it with GDI, and windows
/// asked to draw themselves. This is the way that needs nothing of the graphics card and the one
/// that always answers something; what it cannot do is read a window another window is covering.
/// </summary>
internal static class WindowsDesktop
{
    private const int CopySource = 0x00CC0020;
    private const int CaptureLayered = 0x40000000;
    private const int DipSection = 0;

    /// <summary>
    /// Asks a window to draw the whole of itself, non-client frame and all, rather than only what
    /// it would draw for a printer. Without it a window that draws through DirectComposition — a
    /// modern game, a browser — answers with a picture of nothing.
    /// </summary>
    private const int RenderFullContent = 0x00000002;

    /// <summary>
    /// Copies the rectangle straight out of the screen. A rectangle that runs off the screen is not
    /// refused: what is there comes back, and what is not there comes back black, which is what a
    /// picture of a place that does not exist is.
    /// </summary>
    public static ScreenGrab Copy(int x, int y, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            throw new CaptureFailedException("the screen has no device context to read");
        }

        var memory = CreateCompatibleDC(screen);
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
            },
        };

        var bitmap = CreateDIBSection(screen, ref info, DipSection, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Discard(memory, bitmap, screen, IntPtr.Zero);
            throw new CaptureFailedException($"{width}x{height} could not be prepared for reading");
        }

        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, CopySource | CaptureLayered))
            {
                throw new CaptureFailedException($"{x},{y} {width}x{height} could not be copied");
            }

            var pixels = new byte[(long)width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new ScreenGrab(width, height, pixels, CaptureWay.Gdi);
        }
        finally
        {
            Discard(memory, bitmap, screen, previous);
        }
    }

    /// <summary>
    /// The window asked to draw itself, the whole of it, title bar and all. A window that draws
    /// nothing to the screen this way cannot answer, which is said rather than answered with
    /// something else: whoever asked for this way asked for the window's own drawing.
    /// </summary>
    public static ScreenGrab Print(long handle, WindowPlaces frame)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            throw new CaptureFailedException("the screen has no device context to read");
        }

        var memory = CreateCompatibleDC(screen);
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = frame.Width,
                Height = -frame.Height,
                Planes = 1,
                BitCount = 32,
            },
        };

        var bitmap = CreateDIBSection(screen, ref info, DipSection, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Discard(memory, bitmap, screen, IntPtr.Zero);
            throw new CaptureFailedException($"{frame.Width}x{frame.Height} could not be prepared");
        }

        var previous = SelectObject(memory, bitmap);
        try
        {
            var window = new IntPtr(handle);
            if (!PrintWindow(window, memory, RenderFullContent) && !PrintWindow(window, memory, 0))
            {
                throw new CaptureFailedException(
                    $"{frame.X},{frame.Y} {frame.Width}x{frame.Height} was not drawn");
            }

            var pixels = new byte[(long)frame.Width * frame.Height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            // A window drawing itself leaves the fourth byte of every pixel at zero, which stands
            // for "see through"; a picture of a window is opaque whatever the window is drawn with.
            for (var at = 3; at < pixels.Length; at += 4)
            {
                pixels[at] = 0xFF;
            }

            return new ScreenGrab(frame.Width, frame.Height, pixels, CaptureWay.PrintWindow);
        }
        finally
        {
            Discard(memory, bitmap, screen, previous);
        }
    }

    /// <summary>Where one window sits, inside and out, in screen pixels.</summary>
    public static WindowGeometry Geometry(long handle)
    {
        var window = new IntPtr(handle);
        if (!GetWindowRect(window, out var frame))
        {
            throw new EmptyRegionException($"{handle:X} is not a window any more");
        }

        var places = new WindowPlaces(frame.Left, frame.Top, frame.Right - frame.Left,
            frame.Bottom - frame.Top);
        if (places.Width <= 0 || places.Height <= 0)
        {
            throw new EmptyRegionException($"{handle:X} {places.Width}x{places.Height}");
        }

        return new WindowGeometry(places, Content(handle));
    }

    /// <summary>
    /// Where the window is seen: what is on the screen, without the invisible border Windows 10 and
    /// 11 leave around a resizable window for the edges to be grabbed by. That, not the rectangle
    /// Windows reports for the window, is what a picture of the window is a picture of — the outer
    /// rectangle runs a few pixels past what is drawn on either side and below.
    /// </summary>
    public static WindowPlaces Visible(long handle)
    {
        var window = new IntPtr(handle);
        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out var seen,
                Marshal.SizeOf<NativeRect>()) < 0)
        {
            return Geometry(handle).Frame;
        }

        return new WindowPlaces(seen.Left, seen.Top, seen.Right - seen.Left, seen.Bottom - seen.Top);
    }

    /// <summary>
    /// The part of a window the window itself draws in, and where that part sits on the screen. The
    /// border and the title bar push it away from the window's outer corner.
    /// </summary>
    public static WindowPlaces Content(long handle)
    {
        var window = new IntPtr(handle);
        if (!GetClientRect(window, out var client) || !ClientToScreen(window, out var origin))
        {
            throw new EmptyRegionException($"{handle:X} has nowhere it draws");
        }

        return new WindowPlaces(origin.X, origin.Y, client.Right - client.Left,
            client.Bottom - client.Top);
    }

    private static void Discard(IntPtr memory, IntPtr bitmap, IntPtr screen, IntPtr previous)
    {
        if (previous != IntPtr.Zero)
        {
            SelectObject(memory, previous);
        }

        if (bitmap != IntPtr.Zero)
        {
            DeleteObject(bitmap);
        }

        if (memory != IntPtr.Zero)
        {
            DeleteDC(memory);
        }

        ReleaseDC(IntPtr.Zero, screen);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    /// <summary>One rectangle of the desktop, in the layout Windows hands it over in.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>The attribute a window's seen bounds are asked for under.</summary>
    private const int ExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value,
        int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapInfo info, int usage,
        out IntPtr bits, IntPtr section, int offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int operation);
}

/// <summary>One rectangle of the screen: where its top left corner is and how big it is.</summary>
internal readonly record struct WindowPlaces(int X, int Y, int Width, int Height);

/// <summary>
/// The window outside, frame and all, and the part of it the window itself draws in. The two travel
/// together because what a way of reading a window hands back is one or the other, and where the
/// picture then sits on the screen is what tells the caller where something it found in it is.
/// </summary>
internal readonly record struct WindowGeometry(WindowPlaces Frame, WindowPlaces Content);
