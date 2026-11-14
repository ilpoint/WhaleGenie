using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Where the search happens: the areas, the rectangles, and the cells a row names.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The places to search: the whole screen unless the step names regions, each of which may be
    /// counted from a window's corner when the step says so. One row of the step's region is either
    /// a rectangle written out — which is what the region picker fills in — or the name of a
    /// variable, which is how a rectangle or a picture taken earlier gets to be searched.
    /// </summary>
    private IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> SearchAreas(ExecutableStep step)
    {
        var rows = step.Rows("region");
        if (rows.Count == 0)
        {
            // With nowhere written down, the whole of whatever the step is about: the window its
            // numbers are counted from when it names one, and the desktop otherwise.
            var anchor = AnchorOf(step);
            if (anchor.Window is { } window)
            {
                var client = _devices.Windows.ClientOrigin(window.Handle);
                var size = _devices.Windows.ClientSize(window.Handle);
                var whole = Shot(client.X, client.Y, size.Width, size.Height, window);
                return [(whole.Frame, whole.Origin)];
            }

            var desk = _devices.Screen.Desktop.Bounds;
            var desktop = Shot(desk.Left, desk.Top, desk.Width, desk.Height);
            return [(desktop.Frame, desktop.Origin)];
        }

        var areas = new List<(ImageFrame, ScreenPoint)>();
        foreach (var row in rows)
        {
            areas.AddRange(Areas(step, row));
        }

        if (areas.Count == 0)
        {
            throw new StepFailure(RunKeys.BadRegion, Written(rows));
        }

        return areas;
    }

    /// <summary>What the regions of a step say, for a failure that has to show what it could not read.</summary>
    private static string Written(IReadOnlyList<IReadOnlyDictionary<string, ExpressionNode>> rows)
        => string.Join("; ", rows.Select(row =>
            string.Join(',', row.Values.Select(value => Formula.Speak(value)))));

    /// <summary>
    /// What one region of a step comes to. A rectangle written out says where it is; a region that
    /// names something looks the name up, which is how a picture an earlier Capture took is
    /// searched — the screen is grabbed once and looked at as often as the macro likes — and how a
    /// rectangle held in a variable gets to be used.
    /// </summary>
    private IEnumerable<(ImageFrame Frame, ScreenPoint Origin)> Areas(ExecutableStep step,
        IReadOnlyDictionary<string, ExpressionNode> row)
    {
        if (!row.TryGetValue("text", out var written))
        {
            yield return Rectangle(step, row);
            yield break;
        }

        // A picture taken earlier is looked up by the name the step says rather than through the
        // variables: what a variable holds is a note about the picture, and it is the picture
        // itself that is searched. The name is the shape's own, so nothing is taken off the front
        // of a word to find it.
        if (Name(written) is { } name && _images.TryGetValue(name, out var picture))
        {
            yield return (picture, new ScreenPoint(0, 0));
            yield break;
        }

        // A record is one rectangle with a name for each of its sides, which is what a hit hands
        // over: naming that record here is how "look again at what you just found" is written
        // without the macro spelling the four numbers out again.
        if (Read(written) is { IsRecord: true } record && Held(step, record) is { } held)
        {
            yield return held;
            yield break;
        }

        // Anything else is a rectangle, written out or held in a variable. It is only read for its
        // text rather than as a whole expression: the commas of a written rectangle would stop an
        // expression at its first number.
        var named = Read(written).AsText().Trim();

        // Several rectangles are separated by a semicolon or a line break, so a variable holding a
        // list of them goes on working the way it always did.
        foreach (var rectangle in named.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Written(step, rectangle);
        }
    }

    /// <summary>
    /// The rectangle a record stands for: its four sides by name, or the one written rectangle an
    /// action that hands its box over as text puts in it. Nothing when the record is neither, so a
    /// record that is not a rectangle is reported as a region nobody could read rather than as a
    /// failure of its own.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin)? Held(ExecutableStep step, Value record)
    {
        if (Sides(record) is { } sides)
        {
            // The record's own numbers are where the rectangle is on screen, the way a hit's are:
            // the step's coordinate origin moves the four numbers it is given as text, not this.
            // Which window the picture is read out of is a different question, and stays the
            // step's own answer.
            var shot = Shot(sides.Left, sides.Top, sides.Width, sides.Height,
                AnchorOf(step).Window);
            return (shot.Frame, shot.Origin);
        }

        var box = record.Field("box")?.AsText().Trim() ?? string.Empty;
        return box.Length > 0 ? Written(step, box) : null;
    }

    /// <summary>One side of a rectangle held in a record, in whole screen pixels.</summary>
    private static int Whole(Value side) => (int)Math.Round(side.AsNumber());

    /// <summary>
    /// The four sides a record says a rectangle has, in whole screen pixels: the corner it starts at
    /// and how far it reaches from there. Every record that carries a rectangle carries all four of
    /// them, which is what lets a hit, a window or a picture taken earlier say where a step has to
    /// look without the macro spelling the numbers out again. Nothing when the record is not a
    /// rectangle, so a record of some other shape is refused by whoever asked for one. A rectangle
    /// with no width or no height is one pixel wide, the way a width nobody wrote is read.
    /// </summary>
    private static (int Left, int Top, int Width, int Height)? Sides(Value record)
    {
        if (record.Field(FieldNames.Left) is not { } left
            || record.Field(FieldNames.Top) is not { } top
            || record.Field(FieldNames.Width) is not { } width
            || record.Field(FieldNames.Height) is not { } height)
        {
            return null;
        }

        return (Whole(left), Whole(top), Math.Max(1, Whole(width)), Math.Max(1, Whole(height)));
    }

    /// <summary>One region written as four numbers, captured where on the screen it says.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Rectangle(ExecutableStep step,
        IReadOnlyDictionary<string, ExpressionNode> row)
    {
        var x = Cell(row, "x");
        var y = Cell(row, "y");
        var width = Cell(row, FieldNames.Width);
        var height = Cell(row, FieldNames.Height);
        if (width <= 0 || height <= 0)
        {
            throw new StepFailure(RunKeys.BadRegion, Written([row]));
        }

        return Capture(step, x, y, width, height);
    }

    /// <summary>What a row of a list parameter says in one column, or nothing when it has none.</summary>
    private static ExpressionNode Column(IReadOnlyDictionary<string, ExpressionNode> row,
        string column)
        => row.GetValueOrDefault(column) ?? Formula.Nothing;

    /// <summary>
    /// The name a shape is, when it is one reference and nothing else. A region that names a
    /// picture an earlier step took says that name rather than what the name holds.
    /// </summary>
    private static string? Name(ExpressionNode written)
        => written is VariableNode variable ? variable.Name : null;

    /// <summary>One number of a row of a list parameter, as its field was filled in.</summary>
    private int Cell(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => (int)Math.Round(Read(Column(row, column)).AsNumber());

    /// <summary>
    /// One number of a row that may be left empty, where empty means the run's own value: a row
    /// only has to spell out the hold or the gap that is not the one everywhere else.
    /// </summary>
    private int Cell(IReadOnlyDictionary<string, ExpressionNode> row, string column, int fallback)
        => Formula.IsNothing(Column(row, column))
            ? fallback
            : (int)Math.Round(Read(Column(row, column)).AsNumber());

    /// <summary>One written rectangle, such as one a variable holds.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Written(ExecutableStep step, string rectangle)
    {
        var parts = rectangle.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            throw new StepFailure(RunKeys.BadRegion, rectangle);
        }

        return Capture(step, x, y, width, height);
    }
}
