using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WhaleGenie.Converters;

/// <summary>Parses a hex colour string so the colour preview swatch reflects the entered value.</summary>
public class HexColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text && Color.TryParse(text.Trim(), out var color)
            ? new SolidColorBrush(color)
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
