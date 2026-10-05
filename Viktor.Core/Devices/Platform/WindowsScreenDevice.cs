using System;
using System.Runtime.InteropServices;

namespace Viktor.Core.Devices.Platform;

/// <summary>Reads the Windows desktop: how big it is, what colour a pixel is, and what it looks like.</summary>
public sealed class WindowsScreenDevice : IScreenDevice
{
    private const int CopySource = 0x00CC0020;
    private const int CaptureLayered = 0x40000000;
    private const int DipSection = 0;

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
    {
        Require();

        if (width <= 0 || height <= 0)
        {
            throw new DeviceActionException("Run.EmptyRegion", $"{width}x{height}");
        }

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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

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
