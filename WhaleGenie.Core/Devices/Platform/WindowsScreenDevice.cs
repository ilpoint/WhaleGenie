using System;
using System.Runtime.InteropServices;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Reads the Windows desktop: how big it is, what colour a pixel is, and what it looks like. The
/// desktop can be copied out of the screen with GDI, read out of the graphics card's own picture of
/// it, or read through Graphics Capture, which is what still finds a game another window is
/// covering; a window can also be asked to draw itself.
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

    /// <summary>
    /// The display's own picture of itself, taken on first use the way graphics capture is: a macro
    /// that never reads the screen never opens a graphics device over it. It is shared, because a
    /// process is allowed to duplicate a display once and a run and the editor's preview read the
    /// screen at the same time.
    /// </summary>
    private readonly Lazy<DesktopDuplicator> _duplicator =
        new(SharedDesktopDuplicator.Take);

    /// <summary>What a waiting step looks on when the display driver cannot say how fast it runs.</summary>
    private const int CommonRefresh = 16;

    private int? _refresh;

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

    /// <summary>
    /// How long one refresh of the screen lasts, from the display driver, in whole milliseconds.
    /// The driver answers in whole hertz and answers 0 or 1 when it does not know, which is taken as
    /// the commonest display; the answer is remembered because what a monitor runs at does not
    /// change while a macro runs.
    /// </summary>
    public int RefreshMilliseconds
    {
        get
        {
            _refresh ??= Refresh();
            return _refresh.Value;
        }
    }

    private static int Refresh()
    {
        if (!OperatingSystem.IsWindows())
        {
            return CommonRefresh;
        }

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return CommonRefresh;
        }

        try
        {
            var hertz = GetDeviceCaps(screen, VerticalRefresh);
            return hertz < 2 ? CommonRefresh : Math.Max(1, (int)Math.Ceiling(1000.0 / hertz));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

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
        return request.Method == CaptureMethod.Auto
            ? Auto(request, window)
            : One(request.Method, request, window);
    }

    /// <summary>
    /// Reads the rectangle the one way that was named, without falling back to another. The ways
    /// that read a window are for the window that was named; the two desktop ways read the pixels
    /// where the window is, which is what "read the screen here" means wherever it is asked for.
    /// </summary>
    private ScreenShot One(CaptureMethod method, ScreenCaptureRequest request, WindowGeometry window)
        => method switch
        {
            CaptureMethod.PrintWindow => PrintWindow(request, window),
            CaptureMethod.GraphicsCapture => Graphics(request, window),
            CaptureMethod.DesktopDuplication => Duplicated(request),
            _ => Gdi(request),
        };

    /// <summary>
    /// A request that does not name a window: the desktop, read by the means asked for. The two
    /// ways that read a window belong to a request that names one, so they are not among the ones
    /// the machine is set up with here, and a request that asks for one of them anyway is read off
    /// the screen the way it always was.
    /// </summary>
    private ScreenShot Horizontal(ScreenCaptureRequest request) => request.Method switch
    {
        CaptureMethod.Gdi or CaptureMethod.PrintWindow => Gdi(request),
        CaptureMethod.DesktopDuplication => Duplicated(request),
        CaptureMethod.GraphicsCapture => ThroughGraphicsCapture(request),
        _ => AutoDesktop(request),
    };

    /// <summary>
    /// What a step that asked for the desktop by no particular means gets: the display's own
    /// picture of what it is being shown, when this machine can hand one over, and the copy taken
    /// out of the screen when it cannot. The first is several times cheaper — a frame out of
    /// graphics memory against copying 14.7 MB out of the display device — and is the only one that
    /// finds a game filling the screen on its own; the second always answers something.
    /// </summary>
    private ScreenShot AutoDesktop(ScreenCaptureRequest request)
    {
        var (left, top, right, bottom) = Bounds(request);
        if (request.X == left && request.Y == top && request.Width == right - left
            && request.Height == bottom - top
            && _duplicator.Value.Try(left, top, right - left, bottom - top) is { } frame)
        {
            return new ScreenShot(frame, new ScreenPoint(left, top),
                CaptureMethod.DesktopDuplication);
        }

        return Gdi(request);
    }

    /// <summary>Copies the rectangle straight out of the screen, the way a macro always has.</summary>
    private static ScreenShot Gdi(ScreenCaptureRequest request)
        => new(Copy(request.X, request.Y, request.Width, request.Height),
            new ScreenPoint(request.X, request.Y), CaptureMethod.Gdi);

    /// <summary>
    /// The rectangle of the desktop that is on the display, in the display's coordinates. What is
    /// asked for past the edge of the screen has nothing to give, which is said here rather than
    /// answered with a picture of a part of it.
    /// </summary>
    private (int Left, int Top, int Right, int Bottom) Bounds(ScreenCaptureRequest request)
    {
        var size = PrimarySize;
        return (Math.Max(0, request.X), Math.Max(0, request.Y),
            Math.Min(size.Width, request.X + request.Width),
            Math.Min(size.Height, request.Y + request.Height));
    }

    /// <summary>
    /// What a step that asked for neither a window nor a means gets: the pixels where the window
    /// is, read through graphics capture when this machine can, so that a window which is not in
    /// front is still read. Asking a window to draw itself is the second try and the desktop the
    /// third, which is the order of how much each one costs and how much it finds.
    /// </summary>
    private ScreenShot Auto(ScreenCaptureRequest request, WindowGeometry window)
    {
        var ways = Automatic(_graphics.Value.Available);
        foreach (var way in ways[..^1])
        {
            try
            {
                return One(way, request, window);
            }
            catch (DeviceActionException)
            {
                // The window may still draw itself, and a picture somewhere is worth more than a
                // failure: what could not be done is said only when nothing else works.
            }
        }

        // The last way is tried without a way out of it: reading the pixels on the screen is the
        // one that always answers something, and a failure there is the failure of the step.
        return One(ways[^1], request, window);
    }

    /// <summary>
    /// The ways the automatic choice tries for a window, best first. Written out as a list rather
    /// than folded into the trying, so that the order — what each way finds against what it costs —
    /// can be read and checked on its own.
    /// </summary>
    internal static CaptureMethod[] Automatic(bool graphicsAvailable)
        => graphicsAvailable
            ? [CaptureMethod.GraphicsCapture, CaptureMethod.PrintWindow, CaptureMethod.Gdi]
            : [CaptureMethod.PrintWindow, CaptureMethod.Gdi];

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
        return Crop(frame, content.Location, request, CaptureMethod.GraphicsCapture);
    }

    /// <summary>
    /// One display copied out of the graphics card's own picture of it, in the display's own
    /// coordinates. The rectangle is cut down to what is on the screen, and a rectangle with
    /// nothing on the screen in it is a failure rather than an empty picture. A machine that
    /// cannot hand a picture over is refused rather than quietly read another way: the way is
    /// written down in the settings, and falling back would hide that this machine is not doing
    /// what it was told to do.
    /// </summary>
    private ScreenShot Duplicated(ScreenCaptureRequest request)
    {
        var (left, top, right, bottom) = Bounds(request);
        if (right <= left || bottom <= top)
        {
            throw new DeviceActionException("Run.EmptyRegion", Written(request));
        }

        var frame = _duplicator.Value.Try(left, top, right - left, bottom - top)
            ?? throw new DeviceActionException("Run.NoGraphicsCapture",
                "this display cannot be duplicated on this machine");
        return new ScreenShot(frame, new ScreenPoint(left, top), CaptureMethod.DesktopDuplication);
    }

    /// <summary>
    /// One display read through Graphics Capture, in the display's own coordinates. This is the way
    /// that reads a display the graphics card will not duplicate, and the one that finds a screen a
    /// full-screen game has taken over.
    /// </summary>
    private ScreenShot ThroughGraphicsCapture(ScreenCaptureRequest request)
    {
        var (left, top, right, bottom) = Bounds(request);
        if (right <= left || bottom <= top)
        {
            throw new DeviceActionException("Run.EmptyRegion", Written(request));
        }

        var frame = _graphics.Value.Desktop(left, top, right - left, bottom - top);
        return new ScreenShot(frame, new ScreenPoint(left, top), CaptureMethod.GraphicsCapture);
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
                whole.Location, request, CaptureMethod.PrintWindow);
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
        ScreenCaptureRequest request, CaptureMethod method)
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
            return new ScreenShot(whole, origin, method);
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
            new ScreenPoint(origin.X + left, origin.Y + top), method);
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

        if (_duplicator.IsValueCreated)
        {
            SharedDesktopDuplicator.Give();
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

    /// <summary>The display driver's refresh rate, in whole hertz, asked of a screen context.</summary>
    private const int VerticalRefresh = 116;

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
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

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
