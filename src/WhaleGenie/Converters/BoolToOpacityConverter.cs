using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WhaleGenie.Converters;

/// <summary>
/// Maps a toggle state to an opacity, so enabled options render in full colour
/// while disabled options appear greyed out.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    /// <summary>Opacity applied while the bound value is <c>true</c>.</summary>
    public double On { get; set; } = 1.0;

    /// <summary>Opacity applied while the bound value is <c>false</c>.</summary>
    public double Off { get; set; } = 0.3;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? On : Off;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
