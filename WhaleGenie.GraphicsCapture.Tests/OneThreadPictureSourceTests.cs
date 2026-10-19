namespace WhaleGenie.GraphicsCapture.Tests;

/// <summary>
/// Windows Runtime capture sessions belong to the thread that opened them. The sessions are read
/// and closed from the engine thread and from the interface thread, so keeping both calls on the
/// capture thread is what lets an OCR step be followed by a clean exit.
/// </summary>
public class OneThreadPictureSourceTests
{
    [Fact]
    public void A_session_is_read_and_closed_on_the_thread_that_owns_it()
    {
        using var thread = new OneThread("capture check");
        var source = new NoticingSource();
        using var bound = new OneThreadPictureSource(source, thread);

        var caller = Environment.CurrentManagedThreadId;
        var picture = bound.Read();
        bound.Dispose();

        Assert.Equal(2, picture.Grab.Width);
        Assert.NotEqual(caller, source.ReadOn);
        Assert.Equal(source.ReadOn, source.ClosedOn);
        Assert.True(source.Closed);
    }

    [Fact]
    public void Closing_twice_lets_go_of_the_session_once()
    {
        using var thread = new OneThread("capture check");
        var source = new NoticingSource();
        var bound = new OneThreadPictureSource(source, thread);

        bound.Dispose();
        bound.Dispose();

        Assert.Equal(1, source.Closes);
    }

    [Fact]
    public void Reading_after_close_is_refused()
    {
        using var thread = new OneThread("capture check");
        var bound = new OneThreadPictureSource(new NoticingSource(), thread);
        bound.Dispose();

        Assert.Throws<ObjectDisposedException>(() => bound.Read());
    }

    private sealed class NoticingSource : IPictureSource
    {
        public int? ReadOn { get; private set; }

        public int? ClosedOn { get; private set; }

        public int Closes { get; private set; }

        public bool Closed => Closes > 0;

        public Picture Read()
        {
            ReadOn = Environment.CurrentManagedThreadId;
            return new Picture(new ScreenGrab(2, 1, new byte[8], CaptureWay.GraphicsCapture), 0, 0);
        }

        public void Dispose()
        {
            ClosedOn = Environment.CurrentManagedThreadId;
            Closes++;
        }
    }
}
