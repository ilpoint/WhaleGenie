using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

// What a colour field does with what the magnifier read.
public partial class StepParameterViewModel
{
    /// <summary>
    /// Takes a colour the magnifier just read. A field that holds one colour keeps the new one in
    /// its place; a field that holds a list of them adds a row, because a picture cut out of a
    /// screenshot may carry more than one background colour. The single colour goes in as a shape
    /// rather than as a line on the box: the line is only a picture of the shape, and writing that
    /// alone leaves the step holding exactly what it held before.
    /// </summary>
    internal void TakeColour(PixelColor colour)
    {
        if (!IsColourRows)
        {
            SetWritten(Formula.Text(colour.ToHex()));
            return;
        }

        foreach (var row in ColourRows)
        {
            if (PixelColor.TryParse(row.Colour.Trim(), out var already) && already == colour)
            {
                return;
            }
        }

        AddColourRow(new ColourRowViewModel(colour.ToHex()));
    }
}
