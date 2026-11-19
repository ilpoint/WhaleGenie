using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What one reading of the screen reads as a mark: the writing it found, where it found it, how
/// sure it was, and the writing itself as the label. A look built from several readings and a step
/// that only reads them once spell the marks the same way.
/// </summary>
internal static class TextMarks
{
    public static List<(ImageMatch Match, string? Label)> Of(IReadOnlyList<TextSpan> spans,
        ScreenPoint origin)
        => [.. spans.Select(span => (
            new ImageMatch(span.Confidence,
                new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y),
                span.Size),
            (string?)span.Text))];
}
