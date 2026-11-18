using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

// What a colour field does with what the magnifier read.
public partial class StepParameterViewModel
{
    /// <summary>
    /// Takes a colour the magnifier just read. A field that holds one colour keeps the new one in
    /// its place; a field that holds a list of them adds it to the list, because a picture cut out
    /// of a screenshot may carry more than one background colour. The value goes in as a shape
    /// rather than as a line on the box: the line is only a picture of the shape, and writing that
    /// alone leaves the step holding exactly what it held before.
    /// </summary>
    internal void TakeColour(PixelColor colour)
    {
        if (!HoldsColours)
        {
            SetWritten(Formula.Text(colour.ToHex()));
            return;
        }

        var items = Items(Written);
        foreach (var item in items)
        {
            if (PixelColor.TryParse(Formula.Plain(item), out var already) && already == colour)
            {
                return;
            }
        }

        items.Add(Formula.Text(colour.ToHex()));
        SetWritten(Formula.List([.. items]));
    }

    /// <summary>
    /// What a field holds, as the pieces a new colour is added beside: the items of a list, the one
    /// value a field that was written as a single colour holds, or nothing at all.
    /// </summary>
    private static List<ExpressionNode> Items(ExpressionNode written)
        => written switch
        {
            ListNode list => [.. list.Items],
            _ when Formula.IsNothing(written) => [],
            TextNode text when text.Text.Trim().Length == 0 => [],
            _ => [written],
        };
}
