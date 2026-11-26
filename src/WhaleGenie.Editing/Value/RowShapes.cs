using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// A row's value as a shape rather than as the line the dialog draws. The form and the step both use
/// these two translations, so a mutant in one of them is a mutant in the round trip itself — not in
/// a label, a placeholder or a grid cell. Nothing here reads a control.
/// </summary>
internal static class RowShapes
{
    /// <summary>The four numbers, or the one variable, as a step stores one region row.</summary>
    public static StepParameterRow RegionRow(ExpressionNode x, ExpressionNode y,
        ExpressionNode width, ExpressionNode height, string variable)
    {
        var row = new StepParameterRow();
        var named = variable.Trim();
        if (named.Length > 0)
        {
            row.Set("text", RegionText(named));
            return row;
        }

        Keep(row, "x", x);
        Keep(row, "y", y);
        Keep(row, FieldNames.Width, width);
        Keep(row, FieldNames.Height, height);
        return row;
    }

    /// <summary>The same row read back: a name is the whole answer, otherwise it is the four cells.</summary>
    public static RegionReading ReadRegion(StepParameterRow row)
    {
        var named = row.Text("text").Trim();
        if (named.Length > 0)
        {
            return new RegionReading(named, Formula.Nothing, Formula.Nothing,
                Formula.Nothing, Formula.Nothing);
        }

        return new RegionReading(string.Empty, row.Cell("x"), row.Cell("y"),
            row.Cell(FieldNames.Width), row.Cell(FieldNames.Height));
    }

    /// <summary>
    /// What a row written as one thing says: a rectangle written out is text, and anything else is
    /// the name of a variable. The box is the only place a person names a variable here, so a word
    /// that is not a rectangle is a name rather than text that happens to look like one.
    /// </summary>
    public static ExpressionNode RegionText(string text)
    {
        var written = text.Trim();
        return LooksLikeRectangle(written) || !VariableName.IsName(written)
            ? Formula.Text(written)
            : Formula.Name(written);
    }

    /// <summary>
    /// True when the line is one or more rectangles written out: four numbers, one rectangle a line.
    /// A name never has four comma-separated numbers in it, so the two uses of the cell do not have
    /// to be guessed apart.
    /// </summary>
    public static bool LooksLikeRectangle(string text)
    {
        var places = text.Split([';', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return places.Length > 0 && places.All(place =>
        {
            var cells = place.Split(',', StringSplitOptions.TrimEntries);
            return cells.Length == 4 && cells.All(cell =>
                double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        });
    }

    /// <summary>A call argument row, or nothing when the call leaves the argument out.</summary>
    public static StepParameterRow? CallRow(string name, ExpressionNode shape, string value, bool given)
    {
        var text = value.Trim();
        if (text.Length == 0 && !given)
        {
            return null;
        }

        var row = new StepParameterRow
        {
            Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
            {
                [FieldNames.Name] = Formula.Text(name),
            },
        };

        if (!Formula.IsNothing(shape))
        {
            row.Set(FieldNames.Value, shape);
        }

        if (given)
        {
            row.Set(FieldNames.Given, Formula.Constant("true"));
        }

        return row;
    }

    private static void Keep(StepParameterRow row, string column, ExpressionNode shape)
    {
        if (!Formula.IsNothing(shape))
        {
            row.Set(column, shape);
        }
    }
}

/// <summary>One region row after reading: either a name or four shapes, never both.</summary>
internal readonly record struct RegionReading(string Variable, ExpressionNode X,
    ExpressionNode Y, ExpressionNode Width, ExpressionNode Height);
