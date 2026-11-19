using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WhaleGenie.Converters;

/// <summary>
/// Reads a nullable number the way the theme's number field speaks. Its value is a `double` that
/// says "empty" with `NaN`, while every setting here is a `decimal?` or an `int?` that says it with
/// `null` — and "leave this blank" is a real answer in all of them (an unset hold time is not a
/// hold time of zero). The conversion is therefore not a cast but a translation of that one idea,
/// in both directions, so typing, clearing and reading back all agree.
/// </summary>
public class NumberFieldValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal number => (double)number,
        int number => number,
        double number => number,
        _ => double.NaN,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var number = value is double read ? read : double.NaN;
        if (double.IsNaN(number))
        {
            return null;
        }

        // The field hands back a double whatever it is bound to, so the type to write into comes
        // from the property, not from the value.
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return target == typeof(int) ? (int)Math.Round(number) : (decimal)number;
    }
}
