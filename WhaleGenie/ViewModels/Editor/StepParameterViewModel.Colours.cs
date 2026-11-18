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
    /// its place; one that keeps several adds it to the colours already there. The value goes in as
    /// a shape rather than as a line on the box: the line is only a picture of the shape, and
    /// writing that alone leaves the step holding exactly what it held before.
    /// </summary>
    internal void TakeColour(PixelColor colour)
    {
        if (!AppendsColours)
        {
            SetWritten(Formula.Text(colour.ToHex()));
            return;
        }

        var kept = new List<PixelColor>();
        foreach (var token in ColourList.Tokens(CurrentText))
        {
            if (ColourList.Read(token) is { } already && !kept.Contains(already))
            {
                kept.Add(already);
            }
        }

        if (!kept.Contains(colour))
        {
            kept.Add(colour);
        }

        SetWritten(Formula.Text(ColourList.Write(kept)));
    }
}
