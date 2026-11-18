using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

// What a colour field does with what the magnifier read.
public partial class StepParameterViewModel
{
    /// <summary>
    /// Takes a colour the magnifier just read. A field that holds one colour keeps the new one in
    /// its place; a list of colours adds it as another row, which is how one picture cropped over
    /// two backgrounds is described without typing either of them. The single colour goes in as a
    /// shape rather than as a line on the box: the line is only a picture of the shape, and writing
    /// that alone leaves the step holding exactly what it held before.
    /// </summary>
    internal void TakeColour(PixelColor colour)
    {
        if (!IsColours)
        {
            SetWritten(Formula.Text(colour.ToHex()));
            return;
        }

        foreach (var row in ColourRows)
        {
            if (ColourList.Read(row.Colour.Trim()) == colour)
            {
                return;
            }
        }

        AddColourRow(new ColourRowViewModel(colour.ToHex()));
    }
}
