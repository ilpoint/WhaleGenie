using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WhaleGenie.Converters;

/// <summary>
/// The colour a group of expression functions is written in. The palette lives here rather than in
/// the markup because the same hue has to come out for everything that names a group — the card's
/// fill, the line around it and the writing on it are one hue at three weights — and because a table
/// is the only shape that can be read back and checked against the groups the engine has.
/// </summary>
public class FunctionGroupBrushConverter : IValueConverter
{
    /// <summary>One hue to a group of functions, named the way the engine names the group.</summary>
    private static readonly Dictionary<string, Color> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = Color.Parse("#5B9BD5"),
        ["maths"] = Color.Parse("#9B7EDE"),
        ["lists"] = Color.Parse("#4FBF9F"),
        ["dates"] = Color.Parse("#D8A657"),
        ["records"] = Color.Parse("#E06C9F"),
        ["other"] = Color.Parse("#8A99A8"),
    };

    /// <summary>
    /// The hue a group is written in. A group the table does not know is read as "other" rather
    /// than as nothing at all: a function added to the language before its colour is chosen has to
    /// stay readable on the wall rather than come out as an invisible card.
    /// </summary>
    public static Color Hue(string? group)
        => group is not null && Palette.TryGetValue(group, out var hue) ? hue : Palette["other"];

    /// <summary>
    /// The hue at one of the three weights a card needs: <c>fill</c> for what is under the writing,
    /// <c>line</c> for the edge around it, <c>ink</c> for the writing itself. Anything else is read
    /// as the fill, so a missing parameter still paints a card.
    /// </summary>
    public static Color Shade(Color hue, string? weight) => weight switch
    {
        "line" => Color.FromArgb(0xA6, hue.R, hue.G, hue.B),
        "ink" => Toward(hue, Colors.White, 0.45),
        _ => Color.FromArgb(0x2E, hue.R, hue.G, hue.B),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(Shade(Hue(value as string), parameter as string));

    /// <summary>
    /// One colour moved part of the way to another, which is how the writing on a card stays
    /// readable: it is the card's own hue, lifted far enough off the tint under it.
    /// </summary>
    private static Color Toward(Color from, Color to, double part) => Color.FromArgb(
        from.A,
        (byte)(from.R + ((to.R - from.R) * part)),
        (byte)(from.G + ((to.G - from.G) * part)),
        (byte)(from.B + ((to.B - from.B) * part)));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
