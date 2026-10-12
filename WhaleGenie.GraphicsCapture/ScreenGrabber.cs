namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// Takes pictures of the screen for everything that needs one: a window, or the desktop, read the
/// way the caller says. What each way can and cannot do is the ways' own business; what is decided
/// here is which way is reached for, and which sessions are kept between looks.
/// </summary>
/// <remarks>
/// Sessions are kept because opening one builds a graphics device and a pool of pictures — far more
/// than reading a frame — and because a session opened and let go of for every look makes the
/// capture border Windows draws flicker on and off while a macro reads the same thing over and over.
/// The graphics work happens on threads belonging to the ways themselves, since the calls come from
/// the engine's thread and from the interface's.
/// </remarks>
public sealed class ScreenGrabber : IDisposable
{
    /// <summary>
    /// The display's own picture of itself, taken on first use: a process is allowed to duplicate a
    /// display once, so the whole program shares one.
    /// </summary>
    private readonly Lazy<DesktopDuplicator> _duplicator = new(SharedDesktopDuplicator.Take);

    private readonly IPictureSources _sources;

    /// <summary>One session per window, and one for the display, which is kept under the handle 0.</summary>
    private readonly Dictionary<long, IPictureSource> _open = [];

    /// <summary>
    /// Windows that could not be opened for reading this way at all. Opening one fails every time, so
    /// a macro looking at the same window in a loop would pay for a failed attempt on every look;
    /// what is remembered is the window, not the attempt, because it is the window that cannot be
    /// read this way. Only windows: a display whose duplication cannot be opened now may be free in
    /// a moment, and the way to tell is to ask again.
    /// </summary>
    private readonly HashSet<long> _refused = [];

    /// <summary>A grabber reading this machine's screen.</summary>
    public ScreenGrabber() : this(new WindowsGraphicsCapture())
    {
    }

    /// <summary>
    /// The same, with somewhere else for the sessions to come from, so a check can drive the caching
    /// and the trying of the ways without a screen being there.
    /// </summary>
    internal ScreenGrabber(IPictureSources sources) => _sources = sources;

    /// <summary>
    /// Whether this machine can read a window through Graphics Capture at all. A machine with none
    /// is not going to grow one while a macro runs, which is what makes this worth asking once.
    /// </summary>
    public bool CanUseGraphicsCapture => _sources.Available;

    /// <summary>
    /// The rectangle that was asked for, out of the window that was named or off the desktop, taken
    /// the way that was asked for. Naming no window is reading the desktop.
    /// </summary>
    public ScreenGrab Grab(long window, int x, int y, int width, int height, CaptureWay way)
    {
        if (width <= 0 || height <= 0)
        {
            throw new EmptyRegionException($"{width}x{height}");
        }

        var ways = way == CaptureWay.Auto
            ? Automatic(window != 0, CanUseGraphicsCapture)
            : [way];

        foreach (var candidate in ways[..^1])
        {
            try
            {
                return Take(candidate, window, x, y, width, height);
            }
            catch (Exception error)
                when (error is CaptureUnavailableException or CaptureFailedException)
            {
                // The next way may still answer, and a picture somewhere is worth more than a
                // failure: what could not be done is said only when nothing else works.
            }
        }

        // The last way is tried without a way out of it: reading the pixels off the screen is the one
        // that always answers something, and a failure there is the failure of what was asked for.
        return Take(ways[^1], window, x, y, width, height);
    }

    /// <summary>
    /// The ways the automatic choice tries, best first. Written out as a list rather than folded into
    /// the trying, so that the order — what each way finds against what it costs — can be read and
    /// checked on its own.
    /// </summary>
    internal static CaptureWay[] Automatic(bool window, bool graphicsCapture)
        => window
            ? graphicsCapture
                ? [CaptureWay.GraphicsCapture, CaptureWay.PrintWindow, CaptureWay.Gdi]
                : [CaptureWay.PrintWindow, CaptureWay.Gdi]
            : [CaptureWay.DesktopDuplication, CaptureWay.Gdi];

    private ScreenGrab Take(CaptureWay way, long window, int x, int y, int width, int height)
        => way switch
        {
            CaptureWay.Gdi => WindowsDesktop.Copy(x, y, width, height),
            CaptureWay.DesktopDuplication => Duplicated(x, y, width, height),
            CaptureWay.PrintWindow => Printed(window, x, y, width, height),
            CaptureWay.GraphicsCapture => Grabbed(window, x, y, width, height),
            _ => throw new CaptureUnavailableException($"there is no way of reading the screen: {way}"),
        };

    /// <summary>
    /// The display's own picture of itself, handed over by the graphics card: the cheapest way to
    /// read the whole screen, and the only one that finds a game filling it on its own. A machine
    /// whose card will not hand one over is refused rather than quietly answered some other way,
    /// because this way was asked for by name.
    /// </summary>
    private ScreenGrab Duplicated(int x, int y, int width, int height)
        => _duplicator.Value.Try(x, y, width, height)
            ?? throw new CaptureUnavailableException(
                "the graphics card will not hand over this display");

    /// <summary>
    /// One window asked to draw itself. What it draws is the whole window, so the part that was
    /// asked for is cut out of it.
    /// </summary>
    private static ScreenGrab Printed(long window, int x, int y, int width, int height)
    {
        var places = WindowsDesktop.Geometry(window);
        var whole = WindowsDesktop.Print(window, places.Frame);
        return Placement.Of(whole, places.Frame.X, places.Frame.Y, x, y, width, height);
    }

    /// <summary>
    /// One window, or the display, read the way the screen shows it: what the window shows is read
    /// out of the machinery that composes it, so a game another window is covering still comes out.
    /// </summary>
    private ScreenGrab Grabbed(long window, int x, int y, int width, int height)
    {
        if (_refused.Contains(window))
        {
            throw new CaptureUnavailableException($"{window:X} (refused earlier)");
        }

        var picture = Read(window);
        return Placement.Of(picture.Grab, picture.X, picture.Y, x, y, width, height);
    }

    /// <summary>
    /// One reading out of the session for what is being read, opened again once if it cannot deliver:
    /// what a session was opened on has been closed, resized or drawn again from scratch, and a
    /// second failure is that thing's own and is reported.
    /// </summary>
    private Picture Read(long window)
    {
        try
        {
            return Source(window).Read();
        }
        catch (CaptureFailedException)
        {
            Drop(window);
            return Source(window).Read();
        }
    }

    /// <summary>
    /// The session for one thing, opened the first time it is read and kept while it lives. A window
    /// that will not open is remembered, which is what stops a loop from paying for the same failed
    /// attempt on every look.
    /// </summary>
    private IPictureSource Source(long window)
    {
        if (_open.TryGetValue(window, out var kept))
        {
            return kept;
        }

        try
        {
            return _open[window] = _sources.Open(window);
        }
        catch (CaptureUnavailableException)
        {
            if (window != 0)
            {
                _refused.Add(window);
            }

            throw;
        }
    }

    private void Drop(long window)
    {
        if (_open.Remove(window, out var session))
        {
            session.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var session in _open.Values)
        {
            session.Dispose();
        }

        _open.Clear();

        if (_duplicator.IsValueCreated)
        {
            SharedDesktopDuplicator.Give();
        }

        _sources.Dispose();
    }
}
