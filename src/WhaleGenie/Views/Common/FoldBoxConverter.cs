using System;
using System.Globalization;
using Avalonia.Data.Converters;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>Turns a tree item's header into "does this row carry a fold box?".</summary>
public sealed class FoldBoxConverter : IValueConverter
{
    public static readonly FoldBoxConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not IFolds fold || fold.CanFold;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
