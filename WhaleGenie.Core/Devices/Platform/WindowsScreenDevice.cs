using System;
using System.Runtime.InteropServices;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Reads the Windows desktop: how big it is, what colour a pixel is, and what it looks like. Taking
/// a picture of it is not done here — that belongs to the capture library, which knows the ways
/// Windows hands pixels over — so what this does is turn the engine's request into one of its own
/// and its answer back into the engine's shape.
/// </summary>
public sealed class WindowsScreenDevice : IScreenDevice, IDisposable
{
    /// <summary>What a waiting step looks on when the display driver cannot say how fast it runs.</summary>
    private const int CommonRefresh = 16;

    private readonly ScreenGrabber _grabber = new();

    private int? _refresh;

    /// <summary>
    /// How the desk is laid out, read from the display itself rather than from a setting: plugging a
    /// second display in changes what "the whole screen" means, and a macro reading the screen should
    /// follow the desk it is on.
    /// </summary>
    public ScreenLayout Desktop
    {
        get
        {
            Require();
            return _grabber.Desktop();
        }
    }

    /// <summary>Where the pointer is: the one place in the engine that asks Windows this.</summary>
    public ScreenPoint Cursor => CursorPosition();

    /// <summary>The call itself, so every device that hands the pointer on reads the same thing.</summary>
    private static ScreenPoint CursorPosition()
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
                throw new DeviceActionException(RunKeys.PixelOutsideScreen, $"{x},{y}");
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
    /// Reads a rectangle of the screen, from the place and by the way the request names. A request
    /// that names a window reads that window — what it hands back is what the window itself shows,
    /// so a covering window changes nothing — and one that names none reads the desktop. Where the
    /// picture ends up is reported beside it, because it is always the rectangle that was asked for.
    /// </summary>
    public ScreenShot Capture(ScreenCaptureRequest request)
    {
        Require();

        try
        {
            var grab = _grabber.Grab(request.Window, request.X, request.Y, request.Width,
                request.Height, request.Method);
            return new ScreenShot(new ImageFrame(grab.Width, grab.Height, grab.Bgra),
                new ScreenPoint(request.X, request.Y), grab.Used);
        }
        catch (CaptureUnavailableException error)
        {
            throw new DeviceActionException(RunKeys.NoGraphicsCapture, error.Message);
        }
        catch (CaptureFailedException error)
        {
            throw new DeviceActionException(RunKeys.CaptureFailed, error.Message);
        }
        catch (EmptyRegionException error)
        {
            throw new DeviceActionException(RunKeys.EmptyRegion, error.Message);
        }
    }

    /// <summary>
    /// Lets go of the sessions the capture library is holding. A macro that only ever read the
    /// desktop has nothing much here to release, but a window that was being read has a graphics
    /// device and a pool of pictures behind it.
    /// </summary>
    public void Dispose() => _grabber.Dispose();

    private static void Require()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("the screen");
        }
    }

    /// <summary>The display driver's refresh rate, in whole hertz, asked of a screen context.</summary>
    private const int VerticalRefresh = 116;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);
}
