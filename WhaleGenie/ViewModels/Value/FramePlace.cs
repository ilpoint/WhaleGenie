using WhaleGenie.Core.Devices;

namespace WhaleGenie.ViewModels;

/// <summary>Where a frame belongs: the window it stands for, and the rectangle to draw around it.</summary>
public readonly record struct FramePlace(long Handle, ScreenPoint Location, ScreenSize Size)
{
    /// <summary>
    /// Where a window is worth a frame, or null when it has no place on the screen to draw one: a
    /// shrunk window is not lying anywhere, and one that measures to nothing has no rectangle. The
    /// frame is what says "this one", so a frame over nothing would say something untrue.
    /// </summary>
    public static FramePlace? Of(WindowInfo window)
        => window.Minimized || window.Size.Width <= 0 || window.Size.Height <= 0
            ? null
            : new FramePlace(window.Handle, window.Location, window.Size);
}
