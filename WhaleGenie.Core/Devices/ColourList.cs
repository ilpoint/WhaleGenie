using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The colours one field holds: written one after another, separated by a semicolon, a comma, a
/// space or a line break, as <c>#FFFFFF; #00FF00</c>. The engine reads them to leave that many
/// colours out of a comparison, and the form writes one more into the field the magnifier fills, so
/// the shape is read and written in one place rather than twice.
/// </summary>
public static class ColourList
{
    /// <summary>The pieces a field wrote, in the order it wrote them, with the empty ones left out.</summary>
    public static IReadOnlyList<string> Tokens(string? written)
        =>
        [
            .. (written ?? string.Empty).Split([';', ',', ' ', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ];

    /// <summary>What one piece stands for, or null when it is not a colour at all.</summary>
    public static PixelColor? Read(string token)
        => PixelColor.TryParse(token, out var colour) ? colour : null;

    /// <summary>The colours written the way a field holds them, in the order they are given.</summary>
    public static string Write(IEnumerable<PixelColor> colours)
        => string.Join("; ", colours.Select(colour => colour.ToHex()));
}
