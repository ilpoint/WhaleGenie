using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WhaleGenie.Views;

/// <summary>
/// What a row says about whether it is folded by a box of its own: only a node that holds other
/// nodes inside it is. A row that names what a block runs is part of that block, not a block itself,
/// and a box on it would claim a fold that does not exist.
/// </summary>
public interface IFolds
{
    /// <summary>True on a node that can be opened and closed on its own.</summary>
    bool CanFold { get; }
}

/// <summary>Turns a tree item's header into "does this row carry a fold box?".</summary>
public sealed class FoldBoxConverter : IValueConverter
{
    public static readonly FoldBoxConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not IFolds fold || fold.CanFold;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
