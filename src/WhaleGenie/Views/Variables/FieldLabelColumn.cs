using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// One width for every field label of a variable form. Labels stay left-aligned so the form has a
/// straight left edge; the column is as wide as the widest label the current language has, so the
/// box behind a short word is not pushed a fixed gutter to the right.
/// </summary>
/// <remarks>
/// Avalonia's Grid has no shared-size group and the field rows are separate grids, so the width is
/// measured from the labels themselves and written to each row's first column. Measuring the words
/// rather than the laid-out block keeps the answer when a row (the step-written names) is hidden
/// and has never been measured.
/// </remarks>
internal static class FieldLabelColumn
{
    /// <summary>The class every field label carries, so the rows can be found without a list.</summary>
    internal const string ClassName = "FieldName";

    /// <summary>
    /// Fits the column when the window opens and again when the language changes. The language
    /// table raises an empty property name for that, which is what a form whose words all come
    /// from bindings announces.
    /// </summary>
    internal static void Follow(Window window, INotifyPropertyChanged model)
    {
        void FitLater() => Dispatcher.UIThread.Post(() => Fit(window), DispatcherPriority.Background);

        model.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                FitLater();
            }
        };
        window.Opened += (_, _) => FitLater();
    }

    /// <summary>Gives every label row the width of the widest label this language holds.</summary>
    internal static void Fit(Window window)
    {
        var labels = Labels(window);
        if (labels.Count == 0)
        {
            return;
        }

        var widest = labels.Max(WidthOf);
        foreach (var label in labels)
        {
            if (label.Parent is Grid grid
                && grid.ColumnDefinitions.Count > 0
                && Math.Abs(grid.ColumnDefinitions[0].Width.Value - widest) > 0.5)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(widest);
            }
        }
    }

    /// <summary>The width the label's own words need, with the window's font and size.</summary>
    internal static double WidthOf(TextBlock label)
    {
        if (string.IsNullOrEmpty(label.Text))
        {
            return 0;
        }

        var typeface = new Typeface(
            label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
        return new TextLayout(label.Text, typeface, label.FontSize, label.Foreground).Width;
    }

    internal static List<TextBlock> Labels(Window window)
        => window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.Classes.Contains(ClassName))
            .ToList();
}
