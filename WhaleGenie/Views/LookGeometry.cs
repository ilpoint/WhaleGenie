using System;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Views;

/// <summary>A rectangle on the canvas the look is drawn on, in the units the canvas uses.</summary>
internal readonly record struct LookRect(double X, double Y, double Width, double Height);

/// <summary>
/// Where the marks of a look land on the picture it was taken from. Kept apart from the window so
/// that the arithmetic — picture pixels, screen pixels and the zoom between them — can be checked
/// without a window.
/// </summary>
internal static class LookGeometry
{
    /// <summary>How far in and out the view can be taken.</summary>
    public const double SmallestZoom = 0.1;
    public const double LargestZoom = 8;

    /// <summary>
    /// The zoom that shows the whole picture inside the room there is for it. A picture smaller
    /// than the room is left at its own size rather than blown up: enlarging it would only make
    /// the marks look more certain than they are.
    /// </summary>
    public static double Fit(int pictureWidth, int pictureHeight, double roomWidth, double roomHeight)
    {
        if (pictureWidth <= 0 || pictureHeight <= 0 || roomWidth <= 0 || roomHeight <= 0)
        {
            return 1;
        }

        var zoom = Math.Min(roomWidth / pictureWidth, roomHeight / pictureHeight);
        return Math.Clamp(Math.Min(1, zoom), SmallestZoom, LargestZoom);
    }

    /// <summary>Zooming one step in or out, kept inside what the view allows.</summary>
    public static double Step(double zoom, bool closer) => Math.Clamp(
        closer ? zoom * 1.25 : zoom / 1.25, SmallestZoom, LargestZoom);

    /// <summary>
    /// Where a point on the drawn picture falls in the picture's own pixels, kept inside the
    /// picture: a click past the edge is a click at the edge, not a position that is not there.
    /// </summary>
    public static ScreenPoint Inside(int width, int height, double zoom, double x, double y)
        => new(
            Math.Clamp((int)Math.Round(x / zoom), 0, Math.Max(0, width - 1)),
            Math.Clamp((int)Math.Round(y / zoom), 0, Math.Max(0, height - 1)));

    /// <summary>
    /// The offset a point picked on a picture stands for: how far it is from the middle, which is
    /// where a click lands when the step asks for no offset at all.
    /// </summary>
    public static ScreenPoint Offset(int width, int height, ScreenPoint point)
        => new(point.X - (width / 2), point.Y - (height / 2));

    /// <summary>
    /// Where a mark sits on the canvas. The mark is in screen pixels and the picture has a corner
    /// of its own on the screen, so the two have to be subtracted before the zoom goes on.
    /// </summary>
    public static LookRect Place(ScreenPoint origin, ScreenPoint point, ScreenSize size, double zoom)
        => new((point.X - origin.X) * zoom, (point.Y - origin.Y) * zoom,
            size.Width * zoom, size.Height * zoom);

    /// <summary>
    /// The rectangle as it is drawn: never smaller than something that can be seen, and never
    /// moved by being made bigger — a hit found by a colour is one pixel, and a one pixel mark on
    /// a picture shrunk to fit is nothing at all. The middle stays where it was.
    /// </summary>
    public static LookRect Visible(LookRect rect, double smallest)
    {
        var width = Math.Max(rect.Width, smallest);
        var height = Math.Max(rect.Height, smallest);
        return new LookRect(
            rect.X - ((width - rect.Width) / 2),
            rect.Y - ((height - rect.Height) / 2),
            width,
            height);
    }
}
