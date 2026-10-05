using System;
using System.Globalization;

namespace Viktor.Core.Devices;

/// <summary>A point on the desktop, in pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A width and height, in pixels.</summary>
public readonly record struct ScreenSize(int Width, int Height);

/// <summary>A colour read from, or compared against, the screen.</summary>
public readonly record struct PixelColor(byte R, byte G, byte B)
{
    /// <summary>
    /// Reads <c>#RRGGBB</c>, <c>RRGGBB</c> or <c>#RGB</c>. Anything else becomes
    /// <paramref name="fallback"/>.
    /// </summary>
    public static PixelColor Parse(string? text, PixelColor fallback = default)
    {
        var value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        return value.Length == 6
               && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)
            ? new PixelColor((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed)
            : fallback;
    }

    /// <summary>The colour as <c>#RRGGBB</c>.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>How far apart two colours are: 0 when they are the same, 1 at the extremes.</summary>
    public double DistanceTo(PixelColor other)
    {
        double dr = R - other.R;
        double dg = G - other.G;
        double db = B - other.B;
        return Math.Sqrt((dr * dr + dg * dg + db * db) / (3.0 * 255 * 255));
    }

    /// <summary>True when this colour is within <paramref name="tolerancePercent"/> of another.</summary>
    public bool Matches(PixelColor other, double tolerancePercent)
        => DistanceTo(other) * 100 <= Math.Max(0, tolerancePercent);

    public override string ToString() => ToHex();
}

/// <summary>A block of pixels, top row first, four bytes per pixel: blue, green, red, alpha.</summary>
public sealed record ImageFrame(int Width, int Height, byte[] Bgra)
{
    /// <summary>A frame with nothing in it.</summary>
    public static ImageFrame Empty { get; } = new(0, 0, []);

    public bool IsEmpty => Width <= 0 || Height <= 0 || Bgra.Length < (long)Width * Height * 4;

    /// <summary>The colour of one pixel. Points outside the frame read as black.</summary>
    public PixelColor this[int x, int y]
    {
        get
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || IsEmpty)
            {
                return default;
            }

            var offset = ((long)y * Width + x) * 4;
            return new PixelColor(Bgra[offset + 2], Bgra[offset + 1], Bgra[offset]);
        }
    }
}

/// <summary>Where a reference image was found, and how well it matched.</summary>
public sealed record ImageMatch(double Score, ScreenPoint Location, ScreenSize Size)
{
    /// <summary>The middle of the match, which is what a click aims at.</summary>
    public ScreenPoint Center => new(Location.X + (Size.Width / 2), Location.Y + (Size.Height / 2));
}

/// <summary>One piece of text found on screen.</summary>
public sealed record TextSpan(string Text, ScreenPoint Location, ScreenSize Size, double Confidence)
{
    public ScreenPoint Center => new(Location.X + (Size.Width / 2), Location.Y + (Size.Height / 2));
}

/// <summary>What to look for through UI Automation. Every part is optional.</summary>
public sealed record UiQuery(
    string? Name = null,
    string? AutomationId = null,
    string? ControlType = null,
    string? ClassName = null,
    string? WindowTitle = null)
{
    /// <summary>True when the query would match anything, which is never what was meant.</summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Name)
           && string.IsNullOrWhiteSpace(AutomationId)
           && string.IsNullOrWhiteSpace(ControlType)
           && string.IsNullOrWhiteSpace(ClassName)
           && string.IsNullOrWhiteSpace(WindowTitle);
}

/// <summary>What a command line gave back once it finished.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// An open top-level window: what it is called, where it sits, and whether it is shrunk or
/// filling the screen. The handle is what the window actions are carried out on.
/// </summary>
public sealed record WindowInfo(
    long Handle,
    string Title,
    ScreenPoint Location,
    ScreenSize Size,
    bool Minimized,
    bool Maximized);
