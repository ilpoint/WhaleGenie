using System.Collections.Generic;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The picture side of the screen: where a step's pixel is, what colour it shows, the one picture
/// taken of a rectangle, and what a look at a pixel or a capture leaves for whoever is watching.
/// Reading text and finding a picture both come through here, so "what is on screen" is answered
/// the same way whatever a step means to do with it.
/// </summary>
internal interface IScreenShots
{
    /// <summary>The pixel a step watches, with the window its numbers were counted from.</summary>
    (ScreenPoint Corner, WindowInfo? Window) Spot(ExecutableStep step);

    /// <summary>One picture of a rectangle, taken the way this machine takes pictures.</summary>
    ScreenShot Take(int x, int y, int width, int height, WindowInfo? window = null);

    /// <summary>The colour one pixel shows, read out of the window it belongs to when it has one.</summary>
    PixelColor At(ExecutableStep step, ScreenPoint point, WindowInfo? window);

    /// <summary>
    /// A look at a single pixel: a square of screen around it with the pixel itself marked, which
    /// is what a step watching for a colour has to show for itself.
    /// </summary>
    void SawPixel(ExecutableStep step, ScreenPoint point, string looking, WindowInfo? window);

    /// <summary>Hands a whole picture and its marks to whoever is watching, when anybody is.</summary>
    void Show(ExecutableStep step, LookKind kind, ImageFrame frame, ScreenPoint origin,
        IReadOnlyList<LookBox> boxes, int chosen, string looking = "");

    /// <summary>
    /// Hands over which window a captured picture came from and which way it was taken: not "where
    /// is it", but "was it looking at the right thing at all".
    /// </summary>
    void GiveTaken(ExecutableStep step);
}
