namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// This machine cannot read the screen this way at all: none of Windows Graphics Capture on it, a
/// window Graphics Capture will not open, or a display the graphics card will not hand over. Which
/// way a step reads with is a setting of the program, so this is what that setting being impossible
/// on this machine sounds like.
/// </summary>
public sealed class CaptureUnavailableException : Exception
{
    public CaptureUnavailableException(string message) : base(message)
    {
    }

    public CaptureUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// The screen can be read this way, but this frame did not come back: something about what is being
/// read has changed under an open session, and the session has to be opened again.
/// </summary>
public sealed class CaptureFailedException : Exception
{
    public CaptureFailedException(string message) : base(message)
    {
    }

    public CaptureFailedException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// The rectangle that was asked for holds nothing that can be read: it is empty, or it lies
/// altogether outside what it was to be read from.
/// </summary>
public sealed class EmptyRegionException : Exception
{
    public EmptyRegionException(string message) : base(message)
    {
    }
}
