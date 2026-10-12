namespace WhaleGenie.GraphicsCapture;

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
    /// Opens a session on one window, or on the primary display when <paramref name="window"/> is 0.
    /// Throws <see cref="CaptureUnavailableException"/> when this machine will not open one — and
    /// will keep saying so for that window for as long as it lives.
    /// </summary>
    IPictureSource Open(long window);
}
