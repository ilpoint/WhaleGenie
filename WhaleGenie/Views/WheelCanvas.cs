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
    private IReadOnlyList<WheelPaintSlot> _subActions = [];
    private double _subAngle;

    public WheelPaintStyle Style { get; private set; } = new();

    public void SetStyle(WheelPaintStyle style)
    {
        Style = style;
        InvalidateVisual();
    }

    /// <summary>Sub-actions fanned out beyond the wheel in the direction of their parent.</summary>
    public void SetSubActions(IReadOnlyList<WheelPaintSlot> items, double parentAngle)
    {
        _subActions = items;
        _subAngle = parentAngle;
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

            var (from, to) = RadiiOf(item.Reference.Ring);
            var fill = FillOf(item.Tone, item.Highlighted, surface, field, card, warningSurface);
            var pen = new Pen(item.Highlighted ? warning : border,
                item.Highlighted && Style.HoverBorder ? 2.5 : 0.8);
            var shape = Sector(centre, from, to, item.Reference.Index, count);
            context.DrawGeometry(fill, pen, shape);

            if (item.Highlighted && Style.HoverBorder)
            {
                var glow = new SolidColorBrush(((SolidColorBrush)warning).Color) { Opacity = 0.22 };
                context.DrawGeometry(null, new Pen(glow, 7), shape);
            }
        }

        DrawSubActions(context, centre, warning, card, border, theme);

        if (Style.ShowGuide && _aim is { } pointer)
        {
            DrawGuide(context, new Point(centre, centre), pointer, warning);
        }

        // The centre circle is the cancel region: releasing here runs nothing.
        context.DrawEllipse(card, outline,
            new Point(centre, centre), Metrics.Center, Metrics.Center);
    }

    private void DrawSubActions(DrawingContext context, double centre, IBrush warning,
        IBrush card, IBrush border, bool theme)
    {
        if (_subActions.Count == 0)
        {
            return;
        }

        var inner = Metrics.Edge + 10;
        var outer = Metrics.Edge + 68;
        var step = WheelGeometry.SubSpread / _subActions.Count;
        var text = Brush("TextPrimary", theme, Brushes.White, Brushes.Black);
        for (var index = 0; index < _subActions.Count; index++)
        {
            var from = _subAngle - WheelGeometry.SubSpread / 2 + index * step;
            var to = from + step;
            var item = _subActions[index];
            context.DrawGeometry(
                item.Highlighted ? warning : card,
                new Pen(border, item.Highlighted ? 2 : 1),
                Band(centre, inner, outer, from, to));

            if (item.Label.Trim().Length == 0)
            {
                continue;
            }

            var point = PointAt(centre, (inner + outer) / 2, (from + to) / 2);
            var label = item.Label.Length > 8 ? item.Label[..8] : item.Label;
            var formatted = new FormattedText(label, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, text);
            context.DrawText(formatted,
                new Point(point.X - formatted.Width / 2, point.Y - formatted.Height / 2));
        }
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

    /// <summary>A piece of a band between two angles, used by the fanned-out sub-actions.</summary>
    private static Geometry Band(double centre, double from, double to, double start, double end)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(PointAt(centre, from, start), isFilled: true);
        for (var segment = 1; segment <= Segments; segment++)
        {
            path.LineTo(PointAt(centre, from, start + (end - start) * segment / Segments));
        }

        for (var segment = Segments; segment >= 0; segment--)
        {
            path.LineTo(PointAt(centre, to, start + (end - start) * segment / Segments));
        }

        path.EndFigure(isClosed: true);
        return geometry;
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
