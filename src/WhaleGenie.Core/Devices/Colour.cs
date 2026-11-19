using System;
using System.Globalization;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Devices;

// A pixel colour: reading one from text, and how close two of them are.
/// <summary>A colour read from, or compared against, the screen.</summary>
public readonly record struct PixelColor(byte R, byte G, byte B)
{
    /// <summary>
    /// Reads <c>#RRGGBB</c>, <c>RRGGBB</c> or <c>#RGB</c>. Anything else becomes
    /// <paramref name="fallback"/>.
    /// </summary>
    public static PixelColor Parse(string? text, PixelColor fallback = default)
        => TryParse(text, out var colour) ? colour : fallback;

    /// <summary>
    /// The same reading, saying whether the text was a colour at all. A colour that cannot be read
    /// is not the same answer as black, so a step that wrote something else is refused rather than
    /// compared against the wrong pixels.
    /// </summary>
    public static bool TryParse(string? text, out PixelColor colour)
    {
        var value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length == 6
            && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            colour = new PixelColor((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
            return true;
        }

        colour = default;
        return false;
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
        => new ColourReach(this, tolerancePercent).Near(other);

    public override string ToString() => ToHex();
}
