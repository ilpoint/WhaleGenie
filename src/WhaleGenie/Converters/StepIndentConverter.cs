using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Converters;

/// <summary>
/// Turns a tree level into the left margin of that level's row. The tree indents with this and the
/// guide lines are placed from the same number, so the two can never drift apart.
/// </summary>
public sealed class StepIndentConverter : IValueConverter
{
    public static readonly StepIndentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new Thickness(TreeIndent.Of(value), 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>One step of the step tree's indentation, in pixels.</summary>
public static class TreeIndent
{
    /// <summary>
    /// How far one level sits to the right of the one above it. Sixteen is the theme's default and
    /// reads as a single column once three levels are open; twenty-six is wider than a glyph and
    /// narrower than a row, which is what makes "this row belongs to the one above" visible without
    /// counting the guide lines.
    /// </summary>
    public const double Step = StepRow.IndentWidth;

    /// <summary>The left margin of a row at <paramref name="level"/>.</summary>
    public static double Of(object? level)
        => (level is int depth ? depth : 0) * Step;
}
