using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// Places a step names: picking a point, a region or a drag, and the window it counts from.
public partial class AddActionViewModel
{
    /// <summary>
    /// Notes the parameters that make up a screen position, so either of them can take the
    /// pointer's current place, and the switch that says the whole position comes from the pointer
    /// instead of from numbers. Only actions carrying both halves get either.
    /// </summary>
    private void MarkCoordinates()
    {
        var x = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "x");
        var y = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "y");
        if (x is null || y is null)
        {
            return;
        }

        x.IsCoordinate = true;
        y.IsCoordinate = true;
        x.IsPointAnchor = x.Definition.Picker is not (ActionPickerKind.Region or ActionPickerKind.WindowBounds);

        // The switch stands beside what it belongs to: the button for the actions that click, press
        // or scroll, and the pair of numbers it replaces for the rest.
        if (Parameters.FirstOrDefault(parameter =>
                parameter.Definition.Name == CursorPlaceParameter) is { } place)
        {
            place.IsRowCompanion = true;
        }
    }

    /// <summary>Name of the switch that takes a mouse step's place from the pointer.</summary>
    private const string CursorPlaceParameter = "useCursor";

    /// <summary>
    /// Notes the offset of an action that clicks what it recognized, so the two halves sit on one
    /// line and that line carries the button that picks the point. The point is aimed at on the
    /// picture the step saw, which every action that looks at the screen has — a picture, some
    /// writing or a detected box. An action with an offset but no screen to look at would have
    /// nothing to aim on, and gets no button.
    /// </summary>
    private void MarkOffset()
    {
        var x = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "offsetX");
        var y = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "offsetY");
        if (x is null || y is null || SelectedDefinition is not { LooksAtScreen: true })
        {
            return;
        }

        x.IsRowPair = true;
        y.IsRowPair = true;
        x.IsOffsetAnchor = true;
    }

    /// <summary>
    /// Notes the pattern line of a multi-point colour search, so the line carries the button that
    /// takes its offsets and colours from the screen: working a dozen of them out by eye is the
    /// kind of arithmetic nobody gets right quietly.
    /// </summary>
    /// <summary>
    /// Marks the tolerance line of a multi-point colour search, so the colours around the base colour
    /// are listed under it. That line is the last of the ones about the base colour itself — where it
    /// is, how far off a pixel may be — so the pattern reads as the next question rather than as an
    /// interruption in the middle of those. The pattern has no line of its own: the picker writes it
    /// and this list shows it.
    /// </summary>
    /// <summary>
    /// Writes where a click should land, counted from the middle of the picture the step looks for:
    /// what the point picked on that picture comes to.
    /// </summary>
    public void ApplyOffset(int offsetX, int offsetY)
    {
        Parameter("offsetX")?.SetNumber(offsetX);
        Parameter("offsetY")?.SetNumber(offsetY);
    }

    /// <summary>
    /// Writes the pattern the picker settled on. The picker opens with what the step already holds
    /// and hands the whole list back, so what it shows while it is open is exactly what the step
    /// ends up with; adding, editing and removing all happen there, and there is no second copy in
    /// the form to keep in step by hand.
    /// </summary>
    public void SetColourPoints(IReadOnlyList<string> entries)
    {
        if (Parameter("points") is not { } points)
        {
            return;
        }

        // The picker hands back the whole pattern, so what it returns is what the step keeps: the
        // rows it was given, changed as they were changed there.
        foreach (var row in points.ColourPointRows.ToList())
        {
            points.RemoveColourPoint(row);
        }

        foreach (var entry in entries)
        {
            // 一条偏移点写三段（偏移、偏移、颜色），也可以带上表里的另外两列：不能是、单点容差。
            // 这两列由表格编辑，拾取窗口原样带回来，所以在这里读回它们而不是丢掉。
            var parts = ColourPointText.Split(entry);
            if (parts.Count < 3
                || parts[0].Trim().Length == 0
                || parts[1].Trim().Length == 0
                || parts[2].Trim().Length == 0)
            {
                continue;
            }

            var mustNot = parts.Count > 3 && parts[3].Trim() is "1" or "true";
            var tolerance = parts.Count > 4 ? parts[4].Trim() : string.Empty;
            points.AddColourPoint(
                new ColourPointRowViewModel(parts[0], parts[1], parts[2], tolerance, mustNot));
        }
    }

    /// <summary>
    /// The pattern as the step holds it now, one line each: what the picker starts from. The step
    /// keeps it as rows, so the lines are written from those rather than read back out of a field.
    /// </summary>
    public IReadOnlyList<string> ColourPoints()
        => [.. (Parameter("points")?.ColourPointRows ?? [])
            .Select(row => row.MustNot || row.Tolerance.Trim().Length > 0
                ? string.Join(',', row.Dx.Trim(), row.Dy.Trim(), row.Colour.Trim(),
                    row.MustNot ? "1" : string.Empty, row.Tolerance.Trim())
                : $"{row.Dx.Trim()},{row.Dy.Trim()},{row.Colour.Trim()}")];

    /// <summary>
    /// Notes the parameters that name a rectangle, so one button can take all of them from a drag
    /// on the screen: x, y, width and height; the two places a drag writes; or a single
    /// "x,y,width,height" field.
    /// </summary>
    private void MarkRegion()
    {
        // Which field owns the region button is a fact of the action catalogue: the picker fills
        // the corner and the size together, or the one field that holds the whole rectangle.
        // 一个列表（搜索区域）不给顶上的按钮：它的每一行都有自己的「框选区域」，顶上再放一个
        // 只是把"加一处"说两遍。这里只认"四个数字 + 一个框选"那一种动作。
        var anchor = Parameters.FirstOrDefault(parameter =>
            parameter.Definition.Picker is ActionPickerKind.Region
            && parameter.Definition.Kind is not ActionParameterKind.Region);

        if (anchor is not null)
        {
            anchor.IsRegionAnchor = true;
        }

        // 一个矩形的四个数是一句话：左上角在哪、伸出去多远。原来它们两两一行，四格分两行，
        // 每格底下还各带一段说明，四个数占掉一整屏。压成一行，与"搜索区域"那一行同一个样子。
        if (Parameter("x") is { IsNumber: true } left
            && left.Definition.Picker is ActionPickerKind.Region)
        {
            foreach (var name in new[] { "x", "y", FieldNames.Width, FieldNames.Height })
            {
                if (Parameter(name) is { IsNumber: true } cell)
                {
                    cell.IsRegionQuad = true;
                }
            }
        }

        // How far a rectangle reaches is one thing said in two numbers, so the two read better side
        // by side, the way x and y already do. A drag writes two places rather than a corner and a
        // size, and each place is the same pair of numbers standing the same way.
        PairRow(FieldNames.Width, FieldNames.Height);
        PairRow("startX", "startY");
        PairRow("endX", "endY");

        void PairRow(string first, string second)
        {
            if (Parameter(first) is { } left && Parameter(second) is { } right)
            {
                left.IsRowPair = true;
                right.IsRowPair = true;
            }
        }
    }

    private StepParameterViewModel? Parameter(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Definition.Name == name);

    /// <summary>
    /// The rule a window field shows in the preview. A variable cannot be read before the run, and
    /// a bare line of text is still read as a title.
    /// </summary>
    private static WindowRule RuleOf(StepParameterViewModel? field)
    {
        if (field is null)
        {
            return WindowRule.Empty;
        }

        // A variable that holds a window record cannot be read before the run, so the preview says
        // the name it is waiting for. The shape says which it is; no sign in a line decides it.
        if (field.Written is VariableNode variable)
        {
            return new WindowRule(Title: variable.Name);
        }

        var written = field.CurrentText.Trim();
        return WindowRule.TryParse(written, out var rule)
            ? rule
            : new WindowRule(Title: written);
    }

    /// <summary>The rule a named window field holds right now, or an empty rule when it holds none.</summary>
    public WindowRule WindowRuleFor(string name)
        => Parameter(name) is { } field ? RuleOf(field) : WindowRule.Empty;

    /// <summary>The window this mover names right now, when it is open.</summary>
    public WindowInfo? CurrentWindow()
    {
        if (Windows is null || Parameter("window") is not { } field)
        {
            return null;
        }

        var rule = RuleOf(field);
        if (!rule.HasTarget)
        {
            return null;
        }

        try
        {
            return Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                rule.Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The visible rectangle of the window this mover names right now, when it is open.</summary>
    public (int X, int Y, int Width, int Height)? CurrentWindowBounds()
        => CurrentWindow() is { } window
            ? (window.Location.X, window.Location.Y, window.Size.Width, window.Size.Height)
            : null;

    /// <summary>The window whose client coordinates this action uses, or null for screen pixels.</summary>
    public WindowRule? CoordinateWindow()
    {
        var background = Parameter("inputMode")?.CurrentText.Trim().Equals(
            "background", StringComparison.OrdinalIgnoreCase) == true;
        var windowBasis = Parameter("anchorMode")?.CurrentText.Trim().Equals(
            "window", StringComparison.OrdinalIgnoreCase) == true;
        if ((!background && !windowBasis) || Parameter("window") is not { } field
            || field.Written is VariableNode)
        {
            return null;
        }

        var rule = RuleOf(field);
        return rule.HasTarget ? rule : null;
    }

    /// <summary>True when this action's position fields are counted from a window.</summary>
    public bool NeedsWindowCoordinates
        => Parameter("inputMode")?.CurrentText.Trim().Equals(
               "background", StringComparison.OrdinalIgnoreCase) == true
           || Parameter("anchorMode")?.CurrentText.Trim().Equals(
               "window", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The target's client area on screen, which the region picker highlights.</summary>
    public (int X, int Y, int Width, int Height)? CoordinateWindowBounds()
    {
        if (Windows is null || CoordinateWindow() is not { } rule)
        {
            return null;
        }

        try
        {
            if (Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                    rule.Options) is not { } window)
            {
                return null;
            }

            var origin = Windows.ClientOrigin(window.Handle);
            var size = Windows.ClientSize(window.Handle);
            return (origin.X, origin.Y, size.Width, size.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True when this action works on a rectangle the region picker can fill in.</summary>
    public bool HasRegion => Parameters.Any(parameter => parameter.IsRegionAnchor);

    /// <summary>
    /// Writes a position into the action's x and y, counted from the window the step is anchored
    /// to when it names one, which is what Alt + X does while the dialog is open. Returns false
    /// when the action has no position to fill.
    /// </summary>
    public bool ApplyCursorPosition(int x, int y)
    {
        var origin = AnchorOrigin();
        var filled = false;
        foreach (var parameter in Parameters)
        {
            if (!parameter.IsCoordinate)
            {
                continue;
            }

            parameter.SetNumber(parameter.Definition.Name == "x"
                ? x - (origin?.X ?? 0)
                : y - (origin?.Y ?? 0));
            filled = true;
        }

        // A number just read off the screen is the answer the user wanted; the record field beside
        // it is emptied by the same change, so the form does not keep a place nobody meant.
        return filled;
    }

    /// <summary>
    /// The corner the numbers of this action are counted from, so that a position read off the
    /// screen is stored the way the engine will read it back. Null when the action counts in
    /// screen pixels, and also when it names a window that is not open right now: there is
    /// nothing to subtract then, and the plain numbers are the best that can be done.
    /// </summary>
    private ScreenPoint? AnchorOrigin()
    {
        var mode = Parameter("anchorMode")?.CurrentText.Trim().ToLowerInvariant();
        var background = Parameter("inputMode")?.CurrentText.Trim().Equals(
            "background", StringComparison.OrdinalIgnoreCase) == true;
        if (Windows is null || (mode != "window" && !background))
        {
            return null;
        }

        if (Parameter("window") is not { } text)
        {
            return null;
        }

        var rule = RuleOf(text);
        if (!rule.HasTarget)
        {
            return null;
        }

        try
        {
            // Recognised the way the run will recognise it, so a position taken off the screen is
            // counted from the same corner the step will count from.
            if (Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                    rule.Options) is not { } window)
            {
                return null;
            }

            return Windows.ClientOrigin(window.Handle);
        }
        catch (Exception)
        {
            // A window that is not open right now, or a pattern that will not read, leaves the
            // numbers as screen pixels, which is the best that can be done; the step says what it
            // needed when the macro runs.
            return null;
        }
    }

    /// <summary>
    /// Writes a rectangle that was dragged on the screen into whichever shape this action uses:
    /// x/y/width/height, the two ends of a drag, or one more place to look. A window-anchored
    /// action has the window's corner taken off first, the same way the position shortcut does.
    /// Returns false when the action has nowhere to put it.
    /// </summary>
    public bool ApplyRegion(int x, int y, int width, int height)
    {
        var origin = AnchorOrigin();
        var left = x - (origin?.X ?? 0);
        var top = y - (origin?.Y ?? 0);
        var filled = false;

        foreach (var (name, value) in new (string Name, int Value)[]
                 {
                     ("x", left), ("y", top),
                     (FieldNames.Width, width), (FieldNames.Height, height),
                 })
        {
            if (Parameter(name) is { IsNumber: true } parameter)
            {
                parameter.SetNumber(value);
                filled = true;
            }
        }

        // A drag writes the same rectangle as two places rather than as a corner and a size: the
        // corner the rectangle was dragged from, and the corner it was dragged to.
        foreach (var (name, value) in new (string Name, int Value)[]
                 {
                     ("startX", left), ("startY", top),
                     ("endX", left + width), ("endY", top + height),
                 })
        {
            if (Parameter(name) is { IsNumber: true } parameter)
            {
                parameter.SetNumber(value);
                filled = true;
            }
        }

        if (!filled && Parameter("region") is { } region)
        {
            // A search that looks in several places takes this one as one more place: what the
            // button says is "look here", so that is what it does.
            var row = new RegionRowViewModel();
            row.Put(left, top, width, height);
            region.AddRegion(row);
            filled = true;
        }

        return filled;
    }

    /// <summary>
    /// Writes the two ends of a drag in the order they were dragged, rather than turning them into
    /// a top-left corner and a size.
    /// </summary>
    public bool ApplyDrag(int startX, int startY, int endX, int endY)
    {
        var origin = AnchorOrigin();
        var left = origin?.X ?? 0;
        var top = origin?.Y ?? 0;
        var filled = false;
        foreach (var (name, value) in new (string Name, int Value)[]
                 {
                     ("startX", startX - left), ("startY", startY - top),
                     ("endX", endX - left), ("endY", endY - top),
                 })
        {
            if (Parameter(name) is { IsNumber: true } parameter)
            {
                parameter.SetNumber(value);
                filled = true;
            }
        }

        return filled;
    }
}
