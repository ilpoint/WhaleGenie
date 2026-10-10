using System;
using System.Runtime.InteropServices;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Reads the Windows desktop: how big it is, what colour a pixel is, and what it looks like. The
/// desktop is copied out of the screen with GDI; a window can also be asked to draw itself, or
/// read through graphics capture, which is what still finds a game another window is covering.
/// </summary>
public sealed class WindowsScreenDevice : IScreenDevice, IDisposable
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
    /// Graphics capture, which is only reached for when a window is the thing being read, and only
    /// built on first use: a macro that never reads a window never starts a graphics device.
    /// </summary>
    private readonly Lazy<GraphicsCapture> _graphics = new(() => new GraphicsCapture());

    public ScreenSize PrimarySize
    {
        get
        {
            Require();
            return new ScreenSize(GetSystemMetrics(SystemMetricWidth), GetSystemMetrics(SystemMetricHeight));
        }
    }

    /// <summary>Where the pointer is, as a plain call so other devices can share it.</summary>
    public static ScreenPoint CursorPosition()
        => OperatingSystem.IsWindows() && GetCursorPos(out var point)
            ? new ScreenPoint(point.X, point.Y)
            : default;

    public PixelColor PixelAt(int x, int y)
    {
        Require();

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            throw new DeviceUnavailableException("the screen");
        }

        try
        {
            var pixel = GetPixel(screen, x, y);
            if (pixel == uint.MaxValue)
            {
                throw new DeviceActionException("Run.PixelOutsideScreen", $"{x},{y}");
            }

            // COLORREF stores the channels as 0x00BBGGRR.
            return new PixelColor((byte)(pixel & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)((pixel >> 16) & 0xFF));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public ImageFrame Capture(int x, int y, int width, int height)
        => Capture(new ScreenCaptureRequest(x, y, width, height)).Frame;

    /// <summary>
    /// Reads a rectangle of the screen, from the place and by the means the request names. What a
    /// window source gives back is what the window itself shows, so a covering window changes
    /// nothing; the rectangle is in screen pixels either way, and where the picture ends up is
    /// reported beside it, because a window read through graphics capture is its own size at its
    /// own corner rather than the rectangle that was asked for.
    /// </summary>
    public ScreenShot Capture(ScreenCaptureRequest request)
    {
        Require();

        if (request.Width <= 0 || request.Height <= 0)
        {
            throw new DeviceActionException("Run.EmptyRegion",
                $"{request.Width}x{request.Height}");
        }

        if (!request.IsWindow)
        {
            return Horizontal(request);
        }

        var window = Geometry(request.Window);
        return request.Method switch
        {
            CaptureMethod.PrintWindow => PrintWindow(request, window),
            CaptureMethod.GraphicsCapture => Graphics(request, window),
            CaptureMethod.GraphicsCaptureDesktop => ThroughTheDisplay(request),
            CaptureMethod.Gdi => new ScreenShot(Copy(request.X, request.Y, request.Width,
                request.Height), new ScreenPoint(request.X, request.Y)),
            _ => Auto(request, window),
        };
    }

    /// <summary>
    /// A request that does not name a window: the desktop, read by the means asked for. Naming a
    /// means that only makes sense for a window is refused rather than quietly read another way,
    /// because a step that says "this window" and does not name one has a mistake in it.
    /// </summary>
    private ScreenShot Horizontal(ScreenCaptureRequest request) => request.Method switch
    {
        CaptureMethod.PrintWindow or CaptureMethod.GraphicsCapture
            => throw new DeviceActionException("Run.NoCaptureWindow"),
        CaptureMethod.GraphicsCaptureDesktop => ThroughTheDisplay(request),
        _ => new ScreenShot(Copy(request.X, request.Y, request.Width, request.Height),
            new ScreenPoint(request.X, request.Y)),
    };

    /// <summary>
    /// What a step that asked for neither a window nor a means gets: the pixels where the window
    /// is, read through graphics capture when this machine can, so that a window which is not in
    /// front is still read. Asking a window to draw itself is the second try and the desktop the
    /// third, which is the order of how much each one costs and how much it finds.
    /// </summary>
    private ScreenShot Auto(ScreenCaptureRequest request, WindowGeometry window)
    {
        if (_graphics.Value.Available)
        {
            try
            {
                return Graphics(request, window);
            }
            catch (DeviceActionException)
            {
                // The window may still draw itself, and a picture somewhere is worth more than a
                // failure: what could not be done is said only when nothing else works.
            }
        }

        try
        {
            return PrintWindow(request, window);
        }
        catch (DeviceActionException)
        {
            // Last of all, the pixels that are on the screen. A window that is covered gives the
            // covering window's picture, which is wrong but is what a person would see.
            return new ScreenShot(Copy(request.X, request.Y, request.Width, request.Height),
                new ScreenPoint(request.X, request.Y));
        }
    }

    /// <summary>
    /// One window read through graphics capture, in its own coordinates. What comes back is the
    /// whole of the window's content, which is what the asking of it is worth: a picture of a
    /// window is composed whole, so the library is not asked for part of one — the part that was
    /// wanted is cut out here, and where it sits is known because the content's corner is.
    /// </summary>
    private ScreenShot Graphics(ScreenCaptureRequest request, WindowGeometry window)
    {
        var content = window.Content;
        var frame = _graphics.Value.Window(request.Window, 0, 0, content.Size.Width,
            content.Size.Height);
        return Crop(frame, content.Location, request);
    }

    /// <summary>One display read through graphics capture, in the display's own coordinates.</summary>
    private ScreenShot ThroughTheDisplay(ScreenCaptureRequest request)
    {
        var size = PrimarySize;
        var left = Math.Max(0, request.X);
        var top = Math.Max(0, request.Y);
        var right = Math.Min(size.Width, request.X + request.Width);
        var bottom = Math.Min(size.Height, request.Y + request.Height);
        if (right <= left || bottom <= top)
        {
            throw new DeviceActionException("Run.EmptyRegion", Written(request));
        }

        var frame = _graphics.Value.Desktop(left, top, right - left, bottom - top);
        return new ScreenShot(frame, new ScreenPoint(left, top));
    }

    /// <summary>
    /// One window asked to draw itself, which answers for a window that draws nothing to the screen
    /// this way, and cannot answer for one that has been shrunk. What it draws is the whole window,
    /// title bar and all, so the part that was asked for is cut out of it.
    /// </summary>
    private ScreenShot PrintWindow(ScreenCaptureRequest request, WindowGeometry window)
    {
        var whole = window.Frame;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            throw new DeviceUnavailableException("the screen");
        }

        var memory = CreateCompatibleDC(screen);
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = whole.Size.Width,
                Height = -whole.Size.Height,
                Planes = 1,
                BitCount = 32,
            },
        };

        var bitmap = CreateDIBSection(screen, ref info, DipSection, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Discard(memory, bitmap, screen, IntPtr.Zero);
            throw new DeviceUnavailableException("the screen");
        }

        var previous = SelectObject(memory, bitmap);
        try
        {
            var handle = new IntPtr(request.Window);
            if (!PrintWindow(handle, memory, RenderFullContent) && !PrintWindow(handle, memory, 0))
            {
                throw new DeviceActionException("Run.CaptureFailed",
                    $"{whole.Location.X},{whole.Location.Y} {whole.Size.Width}x{whole.Size.Height}");
            }

            var pixels = new byte[(long)whole.Size.Width * whole.Size.Height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            // A window drawing itself leaves the fourth byte of every pixel at zero, which stands
            // for "see through"; a picture of a window is opaque whatever the window is drawn with.
            for (var at = 3; at < pixels.Length; at += 4)
            {
                pixels[at] = 0xFF;
            }

            return Crop(new ImageFrame(whole.Size.Width, whole.Size.Height, pixels),
                whole.Location, request);
        }
        finally
        {
            Discard(memory, bitmap, screen, previous);
        }
    }

    /// <summary>
    /// The part of a picture that was taken whole, at the place a request asked for. A rectangle
    /// that runs past the edge of the picture is cut down to what is there, because a window read
    /// this way is exactly the window and has nothing outside it to give; the picture says where
    /// what is left of it sits, so a macro aiming at it still lands in the right place.
    /// </summary>
    private static ScreenShot Crop(ImageFrame whole, ScreenPoint origin,
        ScreenCaptureRequest request)
    {
        var left = Math.Max(0, request.X - origin.X);
        var top = Math.Max(0, request.Y - origin.Y);
        var right = Math.Min(whole.Width, request.X - origin.X + request.Width);
        var bottom = Math.Min(whole.Height, request.Y - origin.Y + request.Height);
        if (right <= left || bottom <= top)
        {
            throw new DeviceActionException("Run.EmptyRegion", Written(request));
        }

        if (left == 0 && top == 0 && right == whole.Width && bottom == whole.Height)
        {
            return new ScreenShot(whole, origin);
        }

        var width = right - left;
        var height = bottom - top;
        var pixels = new byte[(long)width * height * 4];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(whole.Bgra, ((long)(top + row) * whole.Width + left) * 4, pixels,
                (long)row * width * 4, width * 4);
        }

        return new ScreenShot(new ImageFrame(width, height, pixels),
            new ScreenPoint(origin.X + left, origin.Y + top));
    }

    /// <summary>Where one window sits, inside and out, in screen pixels.</summary>
    private static WindowGeometry Geometry(long handle)
    {
        var window = new IntPtr(handle);
        if (!GetWindowRect(window, out var frame))
        {
            throw new DeviceActionException("Run.WindowNotFound", $"{handle:X}");
        }

        var size = new ScreenSize(frame.Right - frame.Left, frame.Bottom - frame.Top);
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new DeviceActionException("Run.EmptyRegion", $"{size.Width}x{size.Height}");
        }

        // The content of a window is counted from where its own drawing starts, which the border
        // and the title bar push away from the window's outer corner.
        if (!GetClientRect(window, out var client) || !ClientToScreen(window, out var origin))
        {
            throw new DeviceActionException("Run.CaptureFailed", $"{handle:X}");
        }

        return new WindowGeometry(
            new WindowPlaces(new ScreenPoint(frame.Left, frame.Top), size),
            new WindowPlaces(new ScreenPoint(origin.X, origin.Y),
                new ScreenSize(client.Right - client.Left, client.Bottom - client.Top)));
    }

    /// <summary>One rectangle of a window: where it is on screen and how big it is.</summary>
    private readonly record struct WindowPlaces(ScreenPoint Location, ScreenSize Size);

    /// <summary>The window outside, frame and all, and the part of it the window itself draws in.</summary>
    private readonly record struct WindowGeometry(WindowPlaces Frame, WindowPlaces Content);

    /// <summary>One rectangle written the way a failure says it, so the reader sees what was asked.</summary>
    private static string Written(ScreenCaptureRequest request)
        => $"{request.X},{request.Y} {request.Width}x{request.Height}";

    /// <summary>Copies a region of the screen into a picture of its own.</summary>
    private static ImageFrame Copy(int x, int y, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            throw new DeviceUnavailableException("the screen");
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
            throw new DeviceUnavailableException("the screen");
        }

        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, CopySource | CaptureLayered))
            {
                throw new DeviceActionException("Run.CaptureFailed", $"{x},{y} {width}x{height}");
            }

            var pixels = new byte[(long)width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new ImageFrame(width, height, pixels);
        }
        finally
        {
            Discard(memory, bitmap, screen, previous);
        }
    }

    /// <summary>
    /// Lets go of the graphics capture this device opened, if it ever opened any. A macro that only
    /// ever read the desktop has nothing here to release.
    /// </summary>
    public void Dispose()
    {
        if (_graphics.IsValueCreated)
        {
            _graphics.Value.Dispose();
        }
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

    private static void Require()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("the screen");
        }
    }

    private const int SystemMetricWidth = 0;
    private const int SystemMetricHeight = 1;

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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

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
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

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
