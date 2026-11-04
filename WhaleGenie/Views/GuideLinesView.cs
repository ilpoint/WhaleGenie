using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The guide lines of one row, drawn rather than assembled: a column of dashes for every level the
/// row hangs under, and a dash turning into the row where its branch arrives.
///
/// It is a drawing instead of a stack of one pixel rectangles because a dash pattern cannot be
/// assembled out of rectangles that each know nothing about the row's height — the dashes restart at
/// every element, and a line made of odd-sized pieces reads as a dotted smear rather than a line.
/// One pen, one dash pattern, one pass: the pattern is the same on every row however tall the row is.
/// </summary>
public sealed class GuideLinesView : Control
{
    /// <summary>The lines this row carries, worked out by the row itself.</summary>
    public static readonly StyledProperty<IReadOnlyList<GuideLine>?> LinesProperty =
        AvaloniaProperty.Register<GuideLinesView, IReadOnlyList<GuideLine>?>(nameof(Lines));

    /// <summary>The colour of the lines. Faint on purpose: they say where a row belongs, they do not
    /// compete with the icons that say what it does.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<GuideLinesView, IBrush?>(nameof(Stroke));

    /// <summary>How wide the lines are drawn.</summary>
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<GuideLinesView, double>(nameof(Thickness), 2);

    /// <summary>
    /// The dash pattern, in multiples of <see cref="Thickness"/>: four pixels of line and three of
    /// gap at the default width. Equal dashes and gaps read as a dotted rule; a dash longer than the
    /// gap reads as a dashed one, which is what a tree guide is. Coarse enough to still show three
    /// or four dashes on a thirty pixel row.
    /// </summary>
    private static readonly DashStyle Dashes = new([2, 1.5], 0);

    static GuideLinesView()
        => AffectsRender<GuideLinesView>(LinesProperty, StrokeProperty, ThicknessProperty);

    public IReadOnlyList<GuideLine>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Lines is not { Count: > 0 } lines || Stroke is null || Thickness <= 0)
        {
            return;
        }

        var pen = new Pen(Stroke, Thickness) { DashStyle = Dashes };
        var height = Bounds.Height;
        var middle = height / 2;

        // Rows are drawn a little apart, and a line that stops on the row's own edge leaves a gap
        // the height of that spacing: twelve dashes would read as twelve separate lines. Every line
        // reaches a pixel or two past the row so the one above and the one below join up.
        const double Overlap = 2;

        foreach (var line in lines)
        {
            if (line.Tail)
            {
                context.DrawLine(pen, new Point(line.Left, middle),
                    new Point(line.Left, height + Overlap));
                continue;
            }

            // A line that carries on runs the whole height of the row, so it joins the one above
            // and the one below; a line that ends here stops at the middle, where it turns.
            context.DrawLine(pen, new Point(line.Left, -Overlap),
                new Point(line.Left, line.Full ? height + Overlap : middle));

            if (line.Elbow && line.ElbowWidth > 0)
            {
                // The elbow starts on the line it hangs from and ends under the box the row starts
                // with, so the branch arrives at the node rather than stopping in the air.
                context.DrawLine(pen,
                    new Point(line.Left - (Thickness / 2), middle),
                    new Point(line.Left + line.ElbowWidth + (Thickness * 2), middle));
            }
        }
    }
}
