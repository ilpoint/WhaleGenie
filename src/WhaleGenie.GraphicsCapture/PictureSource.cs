namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// What one session reads: one window, or one display of the desktop. A display is named by the
/// rectangle it holds rather than by an index, because that is what tells the session where the
/// picture it hands back starts — the desktop is wider than any one display and can begin at a
/// negative corner, so "the display" is not the same as "the screen from 0,0".
/// </summary>
internal readonly record struct SourceKey(long Window, ScreenArea Display)
{
    /// <summary>One window, read as a whole.</summary>
    public static SourceKey OfWindow(long window) => new(window, default);

    /// <summary>One display of the desktop, read as a whole.</summary>
    public static SourceKey OfDisplay(ScreenArea area) => new(0, area);

    /// <summary>True when this is a display rather than a window.</summary>
    public bool IsDisplay => Window == 0;
}

/// <summary>
/// One thing that hands out pictures of what it reads — one window, or one display — at its own size
/// and in its own coordinates. Opened once and kept: opening one builds a graphics device and a pool
/// of pictures, which is far more than reading one frame.
/// </summary>
internal interface IPictureSource : IDisposable
{
    /// <summary>
    /// One picture of the whole of what this reads, and where its top left corner sits on the screen.
    /// Throws <see cref="CaptureFailedException"/> when this frame did not come back.
    /// </summary>
    Picture Read();
}

/// <summary>
/// The whole of something, and where its top left corner sits on the screen. The two travel together
/// because a picture of a window is the window's own drawing and starts where the window draws, not
/// where the caller guessed the window was.
/// </summary>
internal readonly record struct Picture(ScreenGrab Grab, int X, int Y);

/// <summary>
/// Where sessions come from. The real one builds them on Windows' own capture machinery; a check
/// hands in one that counts what it was asked for, so the caching and the trying of the ways can be
/// read through without a screen being there.
/// </summary>
internal interface IPictureSources : IDisposable
{
    /// <summary>Whether this machine has Graphics Capture at all.</summary>
    bool Available { get; }

    /// <summary>
    /// Opens a session on one window or one display. Throws <see cref="CaptureUnavailableException"/>
    /// when this machine will not open one — and will keep saying so for that window for as long as
    /// it lives.
    /// </summary>
    IPictureSource Open(SourceKey key);
}
