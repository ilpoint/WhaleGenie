using System;
using System.Globalization;
using System.Runtime.InteropServices;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// Reads the current value of a built-in variable, so the Variable Center can show the
/// state of the machine before a macro runs.
/// </summary>
public static class SystemVariableReader
{
    /// <summary>Value of <paramref name="name"/> right now, or empty when it is not known yet.</summary>
    public static string Read(string name, int screenWidth, int screenHeight)
    {
        var now = DateTime.Now;
        var culture = Culture();

        switch (name)
        {
            case "sys.macroLoop":
            case "sys.loopIndex":
                // Only meaningful while a macro is running.
                return "0";
            case "sys.date":
                return now.ToString("yyyy-MM-dd", culture);
            case "sys.dateLong":
                return now.ToString("yyyy-MM-dd dddd", culture);
            case "sys.time":
                return now.ToString("HH:mm:ss", culture);
            case "sys.dateTime":
                return now.ToString("yyyy-MM-dd HH:mm:ss", culture);
            case "sys.year":
                return now.Year.ToString(CultureInfo.InvariantCulture);
            case "sys.month":
                return now.Month.ToString(CultureInfo.InvariantCulture);
            case "sys.day":
                return now.Day.ToString(CultureInfo.InvariantCulture);
            case "sys.weekday":
                return now.ToString("dddd", culture);
            case "sys.hour":
                return now.Hour.ToString(CultureInfo.InvariantCulture);
            case "sys.minute":
                return now.Minute.ToString(CultureInfo.InvariantCulture);
            case "sys.second":
                return now.Second.ToString(CultureInfo.InvariantCulture);
            case "sys.timestamp":
                return DateTimeOffset.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            case "sys.screenWidth":
                return screenWidth > 0 ? screenWidth.ToString(CultureInfo.InvariantCulture) : string.Empty;
            case "sys.screenHeight":
                return screenHeight > 0 ? screenHeight.ToString(CultureInfo.InvariantCulture) : string.Empty;
            case "sys.machineName":
                return Environment.MachineName;
            case "sys.userName":
                return Environment.UserName;
            case "sys.random":
                return Random.Shared.NextDouble().ToString("0.###", CultureInfo.InvariantCulture);
            case "sys.mouseX":
                return TryGetCursor(out var x, out _) ? x.ToString(CultureInfo.InvariantCulture) : string.Empty;
            case "sys.mouseY":
                return TryGetCursor(out _, out var y) ? y.ToString(CultureInfo.InvariantCulture) : string.Empty;
            case "sys.mouseColor":
                return TryGetCursor(out x, out y) ? ReadColour(x, y) : string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>Date text follows the interface language, so the weekday name matches it.</summary>
    private static CultureInfo Culture()
        => Strings.Current.Language == Language.Chinese
            ? CultureInfo.GetCultureInfo("zh-CN")
            : CultureInfo.GetCultureInfo("en-US");

    private static bool TryGetCursor(out int x, out int y)
    {
        x = 0;
        y = 0;

        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var point))
        {
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }

    /// <summary>Colour of the pixel under the cursor, as #RRGGBB.</summary>
    private static string ReadColour(int x, int y)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var pixel = GetPixel(screen, x, y);
            if (pixel == uint.MaxValue)
            {
                return string.Empty;
            }

            // COLORREF stores the channels as 0x00BBGGRR.
            var red = pixel & 0xFF;
            var green = (pixel >> 8) & 0xFF;
            var blue = (pixel >> 16) & 0xFF;
            return $"#{red:X2}{green:X2}{blue:X2}";
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);
}
