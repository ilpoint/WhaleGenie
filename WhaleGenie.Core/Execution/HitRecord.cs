using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One hit as the record a macro reads: where it was, the four sides of its rectangle, the middle a
/// click aims at, how sure the reading was, and the kind or writing that came with it where there
/// was any. Every family that finds something spells it the same way.
/// </summary>
internal static class HitRecord
{
    /// <summary>One hit, with the fields every kind of hit carries.</summary>
    public static Value Of(ScreenPoint corner, ScreenSize size, double score, string text = "",
        string label = "", int classId = -1)
    {
        var centre = ScreenMath.Middle(corner, size);
        return Value.FromRecord(
        [
            (FieldNames.X, Value.FromNumber(centre.X)),
            (FieldNames.Y, Value.FromNumber(centre.Y)),
            (FieldNames.Left, Value.FromNumber(corner.X)),
            (FieldNames.Top, Value.FromNumber(corner.Y)),
            (FieldNames.Width, Value.FromNumber(size.Width)),
            (FieldNames.Height, Value.FromNumber(size.Height)),
            (FieldNames.Score, Value.FromNumber(score)),
            (FieldNames.Box, Value.FromText($"{corner.X},{corner.Y},{size.Width},{size.Height}")),
            (FieldNames.Text, Value.FromText(text)),
            (FieldNames.Class, Value.FromNumber(classId)),
            (FieldNames.Label, Value.FromText(label)),
        ]);
    }
}
