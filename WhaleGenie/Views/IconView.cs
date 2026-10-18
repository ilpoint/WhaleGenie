using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WhaleGenie.Views;

/// <summary>
/// Draws one icon geometry on a fixed square grid, so icons keep their relative sizes
/// instead of each stretching to fill the control. The built-in path shape fits a shape's
/// own bounds, which makes an icon with a lot of empty margin (an arrow) grow larger than
/// one that fills the grid (a window); this control scales from the grid instead.
/// </summary>
/// <remarks>
/// The pen is divided by the scale, so <see cref="StrokeThickness"/> stays the width that
/// was asked for at any size: the interface has always drawn 1.4–1.5 px lines, and the
/// icons were designed with that weight in mind.
/// </remarks>
public sealed class IconView : Control
{
    /// <summary>The geometry to draw, on a <see cref="ViewBox"/>-sized grid.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<IconView, Geometry?>(nameof(Data));

    /// <summary>The brush the outline is drawn with.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<IconView, IBrush?>(nameof(Stroke));

    /// <summary>The width of the outline in device pixels, whatever size the icon is.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(StrokeThickness), 1.5);

    /// <summary>The side of the square grid the geometry was drawn on.</summary>
    public static readonly StyledProperty<double> ViewBoxProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(ViewBox), 24);

    /// <summary>
    /// Fit the geometry's own bounds to the control instead of the <see cref="ViewBox"/> grid.
    /// The window chrome is 10–12 px; a 24-grid cross scaled down to that would be about half
    /// the box, so those few places zoom the glyph instead of keeping the grid.
    /// </summary>
    public static readonly StyledProperty<bool> FitToBoundsProperty =
        AvaloniaProperty.Register<IconView, bool>(nameof(FitToBounds));

    static IconView()
    {
        AffectsRender<IconView>(
            DataProperty,
            StrokeProperty,
            StrokeThicknessProperty,
            ViewBoxProperty,
            FitToBoundsProperty);
    }

    public IconView()
    {
        Focusable = false;
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double ViewBox
    {
        get => GetValue(ViewBoxProperty);
        set => SetValue(ViewBoxProperty, value);
    }

    public bool FitToBounds
    {
        get => GetValue(FitToBoundsProperty);
        set => SetValue(FitToBoundsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = ViewBox > 0 ? ViewBox : 24;
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } geometry || Stroke is not { } brush || StrokeThickness <= 0)
        {
            return;
        }

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double scale;
        double dx;
        double dy;

        if (FitToBounds)
        {
            var bounds = geometry.Bounds;
            if (bounds.Width <= 0 && bounds.Height <= 0)
            {
                return;
            }

            // A line has no height (or no width); fit along the one dimension it has and
            // centre it on the other.
            var horizontal = bounds.Width > 0 ? Bounds.Width / bounds.Width : double.PositiveInfinity;
            var vertical = bounds.Height > 0 ? Bounds.Height / bounds.Height : double.PositiveInfinity;
            scale = Math.Min(horizontal, vertical);
            dx = (Bounds.Width - bounds.Width * scale) / 2 - bounds.X * scale;
            dy = (Bounds.Height - bounds.Height * scale) / 2 - bounds.Y * scale;
        }
        else
        {
            var side = Math.Min(Bounds.Width, Bounds.Height);
            var grid = ViewBox > 0 ? ViewBox : 24;
            scale = side / grid;
            dx = (Bounds.Width - side) / 2;
            dy = (Bounds.Height - side) / 2;
        }

        var pen = new Pen
        {
            Brush = brush,
            // The transform below scales the pen too; divide it back out so the line keeps
            // the width the caller asked for.
            Thickness = StrokeThickness / scale,
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
