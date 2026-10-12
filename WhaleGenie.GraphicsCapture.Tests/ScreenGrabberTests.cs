namespace WhaleGenie.GraphicsCapture.Tests;

/// <summary>
/// What the grabber decides, as opposed to what the ways themselves do to the screen: which way is
/// reached for, which sessions are kept, and what a picture of a larger thing comes back as. The
/// calls into Windows are the ways' own business and are checked by hand on a real desktop.
/// </summary>
public class ScreenGrabberTests
{
    /// <summary>
    /// The order the automatic choice tries the ways in, best first. What each way finds is worth
    /// against what it costs: reading a window through the compositor finds one another window
    /// covers, asking it to draw itself finds most windows, and reading the pixels on the screen
    /// always answers something.
    /// </summary>
    [Fact]
    public void The_automatic_choice_tries_graphics_capture_first_and_off_the_screen_last()
    {
        Assert.Equal(
            [CaptureWay.GraphicsCapture, CaptureWay.PrintWindow, CaptureWay.Gdi],
            ScreenGrabber.Automatic(window: true, graphicsCapture: true));

        // A machine with no Graphics Capture still reads a window, by asking it to draw itself.
        Assert.Equal([CaptureWay.PrintWindow, CaptureWay.Gdi],
            ScreenGrabber.Automatic(window: true, graphicsCapture: false));
    }

    /// <summary>
    /// The desktop is asked for the graphics card's own picture of itself first, and copied off the
    /// screen when this machine will not hand one over. Graphics Capture is not in the order: it is
    /// the slower of the two ways that read a display on their own, and it is there to be named.
    /// </summary>
    [Fact]
    public void The_desktop_is_duplicated_first_and_copied_off_the_screen_second()
    {
        Assert.Equal([CaptureWay.DesktopDuplication, CaptureWay.Gdi],
            ScreenGrabber.Automatic(window: false, graphicsCapture: true));

        Assert.Equal([CaptureWay.DesktopDuplication, CaptureWay.Gdi],
            ScreenGrabber.Automatic(window: false, graphicsCapture: false));
    }

    /// <summary>
    /// One reading after another of the same window is one session: opening one builds a graphics
    /// device and a pool of pictures, and Windows draws a border around what is being read while one
    /// is open, so opening one per look is both slow and visible.
    /// </summary>
    [Fact]
    public void A_window_read_twice_the_same_way_opens_one_session()
    {
        var sources = new FakeSources { Picture = Solid(4, 4) };
        using var grabber = new ScreenGrabber(sources);

        grabber.Grab(7, 0, 0, 4, 4, CaptureWay.GraphicsCapture);
        grabber.Grab(7, 0, 0, 4, 4, CaptureWay.GraphicsCapture);

        Assert.Equal([7L], sources.Opened);
    }

    /// <summary>
    /// A session that cannot deliver a frame is let go of and opened again once: what it was opened
    /// on has been closed, resized or drawn again from scratch. A second failure is that thing's own
    /// and is reported rather than opened again for ever.
    /// </summary>
    [Fact]
    public void A_session_that_stops_delivering_is_opened_again_once()
    {
        var sources = new FakeSources { Picture = Solid(4, 4) };
        sources.Failures[7] = 1;
        using var grabber = new ScreenGrabber(sources);

        var picture = grabber.Grab(7, 0, 0, 4, 4, CaptureWay.GraphicsCapture);

        Assert.Equal([7L, 7L], sources.Opened);
        Assert.Equal(4, picture.Width);
        Assert.Equal(CaptureWay.GraphicsCapture, picture.Used);
    }

    /// <summary>A session that is done with is let go of, not only forgotten.</summary>
    [Fact]
    public void A_session_that_is_dropped_is_let_go_of()
    {
        var sources = new FakeSources { Picture = Solid(4, 4) };
        sources.Failures[7] = 1;
        using var grabber = new ScreenGrabber(sources);

        grabber.Grab(7, 0, 0, 4, 4, CaptureWay.GraphicsCapture);

        Assert.Equal([7L], sources.Closed);
    }

    /// <summary>
    /// A window Windows will not open a session on is remembered, because it will not open one the
    /// next time either, and a macro looking at it in a loop has no business paying for the same
    /// failed attempt on every look.
    /// </summary>
    [Fact]
    public void A_window_that_will_not_be_read_this_way_is_remembered()
    {
        var sources = new FakeSources();
        sources.Refuses.Add(7);
        using var grabber = new ScreenGrabber(sources);

        Assert.Throws<CaptureUnavailableException>(
            () => grabber.Grab(7, 0, 0, 4, 4, CaptureWay.GraphicsCapture));
        Assert.Throws<CaptureUnavailableException>(
            () => grabber.Grab(7, 0, 0, 4, 4, CaptureWay.GraphicsCapture));

        Assert.Equal([7L], sources.Opened);
    }

    /// <summary>
    /// The picture that comes back is the rectangle that was asked for, wherever on the screen that
    /// rectangle is: what the source has of it is copied to where it belongs, and what the source
    /// has nothing for is left black rather than the picture moving.
    /// </summary>
    [Fact]
    public void The_rectangle_that_was_asked_for_comes_back_where_it_belongs()
    {
        var sources = new FakeSources { Picture = Numbered(4, 4), At = (20, 30) };
        using var grabber = new ScreenGrabber(sources);

        var part = grabber.Grab(7, 22, 31, 2, 2, CaptureWay.GraphicsCapture);

        Assert.Equal([7, 8, 11, 12], Shown(part));
    }

    /// <summary>
    /// A rectangle that hangs over the edge of what is being read comes back whole, with the part
    /// the screen has nothing for left black: a macro aiming at a place that is not there is aiming
    /// somewhere, and the picture it reads is the one it asked for.
    /// </summary>
    [Fact]
    public void A_rectangle_hanging_over_the_edge_comes_back_whole()
    {
        var sources = new FakeSources { Picture = Numbered(4, 4), At = (20, 30) };
        using var grabber = new ScreenGrabber(sources);

        var part = grabber.Grab(7, 19, 29, 2, 2, CaptureWay.GraphicsCapture);

        Assert.Equal(2, part.Width);
        Assert.Equal(2, part.Height);

        // Only the bottom right corner of the request is on what is being read, and it is that one
        // which is filled in; the other three are places the window is not.
        Assert.Equal([0, 0, 0, 1], Shown(part));
    }

    /// <summary>A rectangle with nothing of what is being read in it is a mistake in the asking.</summary>
    [Fact]
    public void A_rectangle_with_nothing_of_the_source_in_it_is_refused()
    {
        var sources = new FakeSources { Picture = Numbered(4, 4), At = (20, 30) };
        using var grabber = new ScreenGrabber(sources);

        Assert.Throws<EmptyRegionException>(
            () => grabber.Grab(7, 0, 0, 4, 4, CaptureWay.GraphicsCapture));
    }

    /// <summary>An empty rectangle is refused before anything is read at all.</summary>
    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-2, 4)]
    public void An_empty_rectangle_is_refused(int width, int height)
    {
        var sources = new FakeSources { Picture = Solid(4, 4) };
        using var grabber = new ScreenGrabber(sources);

        Assert.Throws<EmptyRegionException>(
            () => grabber.Grab(7, 0, 0, width, height, CaptureWay.GraphicsCapture));
        Assert.Empty(sources.Opened);
    }

    /// <summary>A picture whose blue channel counts the pixels, so where each one came from shows.</summary>
    private static ScreenGrab Numbered(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var at = 0; at < width * height; at++)
        {
            // Counting from one, so that a pixel that was filled in never looks like one that was
            // left black.
            pixels[at * 4] = (byte)(at + 1);
            pixels[(at * 4) + 3] = 255;
        }

        return new ScreenGrab(width, height, pixels, CaptureWay.GraphicsCapture);
    }

    private static ScreenGrab Solid(int width, int height)
        => new(width, height, new byte[width * height * 4], CaptureWay.GraphicsCapture);

    /// <summary>What a picture shows, one number per pixel, blue carrying the value.</summary>
    private static int[] Shown(ScreenGrab picture)
    {
        var shown = new int[picture.Width * picture.Height];
        for (var at = 0; at < shown.Length; at++)
        {
            shown[at] = picture.Bgra[at * 4];
        }

        return shown;
    }

    /// <summary>
    /// Somewhere sessions come from that is not this machine: it counts what was asked of it and
    /// answers with pictures of its own, so what the grabber caches and what it reaches for can be
    /// read without a screen being there.
    /// </summary>
    private sealed class FakeSources : IPictureSources
    {
        /// <summary>Every window a session was opened for, in the order they were asked for; 0 is the display.</summary>
        public List<long> Opened { get; } = [];

        /// <summary>Every session that was let go of.</summary>
        public List<long> Closed { get; } = [];

        /// <summary>The windows this machine will not open a session on at all.</summary>
        public HashSet<long> Refuses { get; } = [];

        /// <summary>How many of the next sessions on each window hand no frame over.</summary>
        public Dictionary<long, int> Failures { get; } = [];

        /// <summary>What every session reads: one picture, at one place on the screen.</summary>
        public ScreenGrab Picture { get; set; } = Solid(4, 4);

        /// <summary>Where that picture sits on the screen.</summary>
        public (int X, int Y) At { get; set; }

        public bool Available { get; set; } = true;

        public IPictureSource Open(long window)
        {
            Opened.Add(window);
            if (Refuses.Contains(window))
            {
                throw new CaptureUnavailableException($"{window:X} cannot be read this way");
            }

            var fails = Failures.TryGetValue(window, out var left) && left > 0;
            if (fails)
            {
                Failures[window] = left - 1;
            }

            return new FakeSource(this, window, fails);
        }

        public void Dispose()
        {
        }

        private sealed class FakeSource(FakeSources owner, long window, bool fails) : IPictureSource
        {
            public Picture Read()
            {
                if (fails)
                {
                    throw new CaptureFailedException("no picture came back");
                }

                return new Picture(owner.Picture, owner.At.X, owner.At.Y);
            }

            public void Dispose() => owner.Closed.Add(window);
        }
    }
}
