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

    private readonly Func<ScreenLayout> _displays;

    /// <summary>
    /// One session per window, and one per display of the desktop: a window is kept under its
    /// handle, and a display under the rectangle it holds.
    /// </summary>
    private readonly Dictionary<SourceKey, IPictureSource> _open = [];

    /// <summary>
    /// Windows that could not be opened for reading this way at all. Opening one fails every time, so
    /// a macro looking at the same window in a loop would pay for a failed attempt on every look;
    /// what is remembered is the window, not the attempt, because it is the window that cannot be
    /// read this way. Only windows: a display whose duplication cannot be opened now may be free in
    /// a moment, and the way to tell is to ask again.
    /// </summary>
    private readonly HashSet<long> _refused = [];

    /// <summary>A grabber reading this machine's screen.</summary>
    public ScreenGrabber() : this(new WindowsGraphicsCapture(), Displays.Read)
    {
    }

    /// <summary>
    /// The same, with somewhere else for the sessions to come from, so a check can drive the caching
    /// and the trying of the ways without a screen being there, and somewhere else for the desktop's
    /// own layout to come from, so a check can read a machine with more than one display.
    /// </summary>
    internal ScreenGrabber(IPictureSources sources, Func<ScreenLayout>? displays = null)
    {
        _sources = sources;
        _displays = displays ?? Displays.Read;
    }

    /// <summary>
    /// How the desktop is laid out right now. Read afresh rather than kept: a display is plugged in,
    /// unplugged or rearranged while the program runs, and a macro that keeps reading the screen
    /// should follow the desk it is on rather than the desk it started on.
    /// </summary>
    public ScreenLayout Desktop() => _displays();

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
    /// The whole desktop takes one session per display, because Windows composes one picture per
    /// display and has no item that covers two of them.
    /// </summary>
    private ScreenGrab Grabbed(long window, int x, int y, int width, int height)
    {
        if (window == 0)
        {
            return GrabbedDesktop(x, y, width, height);
        }

        if (_refused.Contains(window))
        {
            throw new CaptureUnavailableException($"{window:X} (refused earlier)");
        }

        var picture = Read(window);
        return Placement.Of(picture.Grab, picture.X, picture.Y, x, y, width, height);
    }

    /// <summary>
    /// The desktop, a display at a time: every display the rectangle covers is read through its own
    /// session and the part of it that falls inside the rectangle is copied into one picture. The
    /// sessions are kept the same way a window's is, so a macro reading the same corner of the desk
    /// over and over does not open one each look.
    /// </summary>
    private ScreenGrab GrabbedDesktop(int x, int y, int width, int height)
    {
        var pieces = ScreenPlacement.Pieces(Desktop(), x, y, width, height);
        if (pieces.Count == 1)
        {
            var only = pieces[0];
            var picture = Read(SourceKey.OfDisplay(only.Display));
            return Placement.Of(picture.Grab, picture.X, picture.Y, x, y, width, height);
        }

        var pixels = new byte[(long)width * height * 4];
        var way = CaptureWay.GraphicsCapture;
        foreach (var piece in pieces)
        {
            var picture = Read(SourceKey.OfDisplay(piece.Display));
            Placement.Blit(picture.Grab, piece.SourceX, piece.SourceY, pixels, width,
                piece.ToX, piece.ToY, piece.Width, piece.Height);
            way = picture.Grab.Used;
        }

        return new ScreenGrab(width, height, pixels, way);
    }

    /// <summary>
    /// One reading out of the session for what is being read, opened again once if it cannot deliver:
    /// what a session was opened on has been closed, resized or drawn again from scratch, and a
    /// second failure is that thing's own and is reported.
    /// </summary>
    private Picture Read(SourceKey key)
    {
        try
        {
            return Source(key).Read();
        }
        catch (CaptureFailedException)
        {
            Drop(key);
            return Source(key).Read();
        }
    }

    /// <summary>The one reading of a window, kept where the shape a session is named by is a handle.</summary>
    private Picture Read(long window) => Read(SourceKey.OfWindow(window));

    /// <summary>
    /// The session for one thing, opened the first time it is read and kept while it lives. A window
    /// that will not open is remembered, which is what stops a loop from paying for the same failed
    /// attempt on every look.
    /// </summary>
    private IPictureSource Source(SourceKey key)
    {
        if (_open.TryGetValue(key, out var kept))
        {
            return kept;
        }

        try
        {
            return _open[key] = _sources.Open(key);
        }
        catch (CaptureUnavailableException)
        {
            if (!key.IsDisplay)
            {
                _refused.Add(key.Window);
            }

            throw;
        }
    }

    private void Drop(SourceKey key)
    {
        if (_open.Remove(key, out var session))
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
