using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WhaleGenie.Views;

/// <summary>
/// A box that is as wide as the words in it.
///
/// <para>
/// A value with names in it is edited as a row of pieces, and every run of text between two names
/// is one of these. A box of the usual kind would take the whole row and push the names off it, so
/// this one measures what it holds with the font it is drawn in and asks for exactly that much.
/// </para>
/// </summary>
public class InlineTextBox : TextBox
{
    /// <summary>How wide an empty run is, so there is somewhere to put the caret.</summary>
    private const double EmptyWidth = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        Fit();
        return base.MeasureOverride(availableSize);
    }

    private void Fit()
    {
        var formatted = new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Brushes.Black);

        var wanted = Math.Max(EmptyWidth, Math.Ceiling(formatted.Width) + 2);
        if (double.IsNaN(Width) || Math.Abs(Width - wanted) > 0.5)
        {
            Width = wanted;
        }
    }
}
