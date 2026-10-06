using Avalonia.Media;

namespace Viktor.Models;

/// <summary>
/// The little marks that say something opens and closes. They are drawn rather than typed, so
/// they cannot come out as a missing-glyph box on a machine whose font is not the one they were
/// chosen against.
/// </summary>
public static class Carets
{
    /// <summary>Points down: this is open, or opening it will unfold downwards.</summary>
    public static Geometry Open { get; } = Geometry.Parse("M0,2 L5,7 L10,2");

    /// <summary>Points right: this is folded away, with more of it to the side.</summary>
    public static Geometry Shut { get; } = Geometry.Parse("M2,0 L7,5 L2,10");
}
