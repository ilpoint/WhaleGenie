using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WhaleGenie.Models;

namespace WhaleGenie.Views;

/// <summary>What a drawn position means, so the editor and the live wheel share one painter.</summary>
public enum WheelPaintTone
{
    Empty,
    Global,
    Overridden,
    Extended,
}

/// <summary>One position the painter draws. The label is already localised by the caller.</summary>
public sealed record WheelPaintSlot(
    WheelSlotRef Reference,
    string Label,
    WheelPaintTone Tone,
    bool Highlighted);

/// <summary>The 2.x look: theme colours, a sheen, a hover rim and the guide line.</summary>
public sealed record WheelPaintStyle(
    bool FollowTheme = true,
    bool Gloss = true,
    bool HoverBorder = true,
    bool ShowGuide = true);

/// <summary>
/// Draws the wheel's sectors. The sectors are polylines rather than arc segments: the shape is
/// identical at any size and does not depend on the platform's arc implementation, and a sector
/// with a hole in the middle is just one closed path.
/// </summary>
public sealed class WheelCanvas : Control
{
    private const int Segments = 14;

    public IReadOnlyList<WheelPaintSlot> Items { get; private set; } = [];

    public double Diameter { get; private set; } = 420;

    /// <summary>
    /// The side of the transparent square the wheel sits in. It is larger than the wheel on the
    /// live window so the guide line can follow the pointer outside the visible circles; an empty
    /// square must still be exactly the wheel's size for the editor and the preview.
    /// </summary>
    public double Size { get; private set; } = 420;

    public WheelMetrics Metrics { get; private set; } = WheelGeometry.For(420, false);

    /// <summary>Whether the invisible extended circle is drawn as a band, which only the editor asks for.</summary>
    public bool ShowExtendedBand { get; private set; }

    private Point? _aim;
    private bool _aimExtended;

    /// <summary>
    /// Sector shapes by position and count. A wheel redraws on every change of direction, and the
    /// shape of a sector only changes when the wheel's size or its rings do; building forty-eight
    /// closed paths per frame was most of what a fast pointer cost.
    /// </summary>
    private readonly Dictionary<(WheelRing Ring, int Index, int Count), Geometry> _sectors = [];
    private WheelMetrics _sectorMetrics = WheelGeometry.For(1, false);
    private double _sectorDiameter;
    private double _sectorSize;

    public WheelPaintStyle Style { get; private set; } = new();

    public void SetStyle(WheelPaintStyle style)
    {
        Style = style;
        InvalidateVisual();
    }

    public void Draw(double diameter, bool wideOuter, IReadOnlyList<WheelPaintSlot> items,
        bool showExtendedBand, double canvasSize = 0, int thirdCapacity = 0, int rings = 0)
    {
        Diameter = diameter;
        Size = canvasSize > 0 ? canvasSize : diameter;
        Metrics = WheelGeometry.For(diameter, wideOuter, thirdCapacity, rings);
        Items = items;
        ShowExtendedBand = showExtendedBand;

        // The cached sectors are drawn in this canvas' own coordinates, so they only survive while
        // the wheel keeps the same size and the same rings.
        if (Metrics != _sectorMetrics || Diameter != _sectorDiameter || Size != _sectorSize)
        {
            _sectors.Clear();
            _sectorMetrics = Metrics;
            _sectorDiameter = Diameter;
            _sectorSize = Size;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Points the guide line at the pointer, in this canvas' own coordinates. The pointer can be
    /// far outside the square window; the line then runs to the edge to show the direction.
    /// </summary>
    public void AimAt(Point point, bool extended)
    {
        _aim = point;
        _aimExtended = extended;
        InvalidateVisual();
    }

    public void ClearAim()
    {
        _aim = null;
        InvalidateVisual();
    }

    /// <summary>Where a line from the centre meets the square window's edge.</summary>
    internal static Point ClampToSquare(Point from, Point to, double size)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx == 0 && dy == 0)
        {
            return to;
        }

        var fraction = 1d;
        if (dx > 0)
        {
            fraction = Math.Min(fraction, (size - from.X) / dx);
        }
        else if (dx < 0)
        {
            fraction = Math.Min(fraction, -from.X / dx);
        }

        if (dy > 0)
        {
            fraction = Math.Min(fraction, (size - from.Y) / dy);
        }
        else if (dy < 0)
        {
            fraction = Math.Min(fraction, -from.Y / dy);
        }

        fraction = Math.Clamp(fraction, 0, 1);
        return new Point(from.X + dx * fraction, from.Y + dy * fraction);
    }

    private static readonly IBrush LightSurface = Solid("#FFF7F8FA");
    private static readonly IBrush LightField = Solid("#FFECEFF4");
    private static readonly IBrush LightCard = Solid("#FFFFFFFF");
    private static readonly IBrush LightBorder = Solid("#FFC9D0DA");
    private static readonly IBrush DarkSurface = Solid("#FF1C1C1C");
    private static readonly IBrush DarkField = Solid("#FF2A2A2A");
    private static readonly IBrush DarkCard = Solid("#FF343434");
    private static readonly IBrush DarkBorder = Solid("#FF4A4A4A");
    private static readonly IBrush Accent = Solid("#FFFF8A3D");

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var theme = Style.FollowTheme;
        var centre = Size / 2;
        var surface = Brush("SurfaceWindow", theme, DarkSurface, LightSurface);
        var field = Brush("SurfaceField", theme, DarkField, LightField);
        var card = Brush("SurfaceCard", theme, DarkCard, LightCard);
        var border = Brush("BorderControl", theme, DarkBorder, LightBorder);
        var warning = Brush("Warning", theme, Accent, Accent);
        var warningSurface = Brush("WarningSurface", theme, DarkCard, LightField);
        var outline = new Pen(border, 1);
        var sectorPen = new Pen(border, 0.8);
        var hoverPen = new Pen(warning, Style.HoverBorder ? 2.5 : 0.8);
        var glowPen = Style.HoverBorder && warning is SolidColorBrush solid
            ? new Pen(new SolidColorBrush(solid.Color) { Opacity = 0.22 }, 7)
            : null;

        context.DrawEllipse(surface, outline,
            new Point(centre, centre), Metrics.Edge, Metrics.Edge);

        if (Style.Gloss)
        {
            var sheen = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.FromArgb(38, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55),
                    new GradientStop(Color.FromArgb(20, 0, 0, 0), 1),
                ],
            };
            context.DrawEllipse(sheen, null,
                new Point(centre, centre), Metrics.Edge, Metrics.Edge);
        }

        foreach (var item in Items)
        {
            var count = CountOf(item.Reference.Ring);
            if (item.Reference.Ring == WheelRing.Extended && !ShowExtendedBand)
            {
                continue;
            }

            if (item.Reference.Ring == WheelRing.Outer && !Metrics.HasOuter
                || item.Reference.Ring == WheelRing.Third && !Metrics.HasThird)
            {
                continue;
            }

            if (item.Reference.Index >= count)
            {
                continue;
            }

            var fill = FillOf(item.Tone, item.Highlighted, surface, field, card, warningSurface);
            var pen = item.Highlighted ? hoverPen : sectorPen;
            var shape = SectorFor(item.Reference, count);
            context.DrawGeometry(fill, pen, shape);

            if (item.Highlighted && glowPen is not null)
            {
                context.DrawGeometry(null, glowPen, shape);
            }
        }

        if (Style.ShowGuide && _aim is { } pointer)
        {
            DrawGuide(context, new Point(centre, centre), pointer, warning);
        }

        // The centre circle is the cancel region: releasing here runs nothing.
        context.DrawEllipse(card, outline,
            new Point(centre, centre), Metrics.Center, Metrics.Center);
    }

    /// <summary>
    /// The line from the centre to the pointer. A solid line means a visible circle; a dashed one
    /// means the pointer has left the wheel and is aiming at the invisible extended circle.
    /// </summary>
    private void DrawGuide(DrawingContext context, Point centre, Point pointer, IBrush warning)
    {
        var end = ClampToSquare(centre, pointer, Size);
        var pen = new Pen(warning, _aimExtended ? 2.5 : 2)
        {
            DashStyle = _aimExtended ? new DashStyle([6, 5], 0) : null,
        };
        context.DrawLine(pen, centre, end);
        var dot = _aimExtended ? 5 : 3.5;
        context.DrawEllipse(warning, null, end, dot, dot);
    }

    private int CountOf(WheelRing ring) => ring switch
    {
        WheelRing.Inner => WheelShape.InnerCount,
        WheelRing.Outer => Metrics.OuterCount,
        WheelRing.Third => Metrics.ThirdCount,
        _ => WheelShape.ExtendedCount,
    };

    private (double From, double To) RadiiOf(WheelRing ring) => ring switch
    {
        WheelRing.Inner => (Metrics.Center, Metrics.Inner),
        WheelRing.Outer => (Metrics.Inner, Metrics.Outer),
        WheelRing.Third => (Metrics.Outer, Metrics.Third),
        _ => (Metrics.Edge, Diameter / 2),
    };

    private static IBrush Brush(string key, bool theme, IBrush dark, IBrush light)
        => theme ? ThemeBrush.Of(key) ?? dark : light;

    private static SolidColorBrush Solid(string colour) => new(Color.Parse(colour));

    private static IBrush FillOf(WheelPaintTone tone, bool highlighted, IBrush surface,
        IBrush field, IBrush card, IBrush warningSurface)
    {
        if (highlighted)
        {
            return warningSurface;
        }

        return tone switch
        {
            WheelPaintTone.Global => field,
            WheelPaintTone.Overridden => warningSurface,
            WheelPaintTone.Extended => card,
            _ => surface,
        };
    }

    /// <summary>The shape of one position, built once for the size and rings it is drawn at.</summary>
    private Geometry SectorFor(WheelSlotRef reference, int count)
    {
        var key = (reference.Ring, reference.Index, count);
        if (_sectors.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var (from, to) = RadiiOf(reference.Ring);
        var shape = Sector(Size / 2, from, to, reference.Index, count);
        _sectors[key] = shape;
        return shape;
    }

    private static Geometry Sector(double centre, double from, double to, int index, int count)
    {
        var step = Math.PI * 2 / count;
        var start = index * step - step / 2;
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(PointAt(centre, from, start), isFilled: true);
        for (var segment = 1; segment <= Segments; segment++)
        {
            path.LineTo(PointAt(centre, from, start + step * segment / Segments));
        }

        for (var segment = Segments; segment >= 0; segment--)
        {
            path.LineTo(PointAt(centre, to, start + step * segment / Segments));
        }

        path.EndFigure(isClosed: true);
        return geometry;
    }

    private static Point PointAt(double centre, double radius, double angle)
        => new(centre + radius * Math.Sin(angle), centre - radius * Math.Cos(angle));
}
