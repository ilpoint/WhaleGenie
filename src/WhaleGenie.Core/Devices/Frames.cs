using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;


namespace WhaleGenie.Core.Devices;

// A frame of the screen, and the choices about how frames are taken.
/// <summary>A block of pixels, top row first, four bytes per pixel: blue, green, red, alpha.</summary>
public sealed record ImageFrame(int Width, int Height, byte[] Bgra)
{
    /// <summary>A frame with nothing in it.</summary>
    public static ImageFrame Empty { get; } = new(0, 0, []);

    public bool IsEmpty => Width <= 0 || Height <= 0 || Bgra.Length < (long)Width * Height * 4;

    /// <summary>The colour of one pixel. Points outside the frame read as black.</summary>
    public PixelColor this[int x, int y]
    {
        get
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || IsEmpty)
            {
                return default;
            }

            var offset = ((long)y * Width + x) * 4;
            return new PixelColor(Bgra[offset + 2], Bgra[offset + 1], Bgra[offset]);
        }
    }

    /// <summary>
    /// How much of the picture moved between two frames of the same area: the share of pixels
    /// whose colour is more than <paramref name="tolerancePercent"/> away from what it was, from 0
    /// for a picture that did not move at all to 1 for one that changed everywhere. Two frames of
    /// different sizes are nothing like each other, so that is 1.
    /// </summary>
    /// <remarks>
    /// The whole point of a tolerance is that a cursor blinking or a video playing quietly is not
    /// what a macro means by "the screen moved", so a count of every pixel that differs at all
    /// would answer the wrong question. What is counted is pixels that differ by more than the
    /// caller is willing to call the same colour.
    /// </remarks>
    public static double ChangedShare(ImageFrame before, ImageFrame after, double tolerancePercent)
    {
        if (before.Width != after.Width || before.Height != after.Height)
        {
            return 1;
        }

        var pixels = (long)before.Width * before.Height;
        if (pixels <= 0)
        {
            return 0;
        }

        // Compared squared, which is the same question without the square root: how far apart the
        // colour channels are, against how far the caller allows them to be.
        var left = before.Bgra;
        var right = after.Bgra;
        var limit = Math.Max(0, tolerancePercent) / 100 * 255;
        var allowed = 3 * limit * limit;

        long changed = 0;
        for (long offset = 0; offset + 3 < left.Length && offset + 3 < right.Length; offset += 4)
        {
            double blue = left[offset] - right[offset];
            double green = left[offset + 1] - right[offset + 1];
            double red = left[offset + 2] - right[offset + 2];
            if ((blue * blue) + (green * green) + (red * red) > allowed)
            {
                changed++;
            }
        }

        return (double)changed / pixels;
    }
}

/// <summary>
/// How this machine reads a picture of the screen: the way a window is taken, and the way a
/// rectangle of the desktop is. A macro says what it is looking at and how fresh the picture has to
/// be; how the pixels come off this machine is the machine's business, because the answers —
/// whether it has a graphics card that will duplicate the display, whether it can read a window
/// another window covers — are the same for every step of every macro that runs here.
/// </summary>
public interface ICaptureChoice
{
    /// <summary>The way a picture of a window is taken.</summary>
    CaptureWay ForWindow();

    /// <summary>The way a picture of the desktop is taken.</summary>
    CaptureWay ForScreen();
}

/// <summary>
/// The choice the program is running with. The engine cannot read the settings file — where it
/// lives is the interface's business — so the two answers are handed over here: once when the
/// program starts and again whenever the settings change. What the test devices answer with is their
/// own, so a check can put any of the ways behind a step.
/// </summary>
public sealed class CaptureChoice : ICaptureChoice
{
    /// <summary>The choice this machine's screen is read with.</summary>
    public static CaptureChoice Current { get; } = new();

    /// <summary>The way a picture of a window is taken, automatic by default.</summary>
    public CaptureWay Window { get; set; } = CaptureWay.Auto;

    /// <summary>The way a picture of the desktop is taken, automatic by default.</summary>
    public CaptureWay Screen { get; set; } = CaptureWay.Auto;

    public CaptureWay ForWindow() => Window;

    public CaptureWay ForScreen() => Screen;
}

/// <summary>
/// The pictures this machine hands out: how often it will hand one over, how long one already
/// handed over stays worth reading again, and the last picture it took of each thing it reads.
/// Every run shares it, because there is one screen — two macros recognising pictures at the same
/// moment are looking at the same picture, and a ceiling on what the machine hands out is not one
/// each run gets for itself.
/// </summary>
/// <remarks>
/// Like <see cref="CaptureChoice"/> it lives here rather than in the settings file, which the engine
/// cannot read: the interface hands the answers over once at start-up and again whenever they
/// change, and a run picks a change up at its next look.
/// </remarks>
public sealed class FrameShelf
{
    /// <summary>The pictures this machine is running with.</summary>
    public static FrameShelf Current { get; } = new();

    /// <summary>
    /// How many things are kept pictures of at once. Reuse pays only when a look comes back to
    /// something it has just read, and a macro walking through more windows than this inside one
    /// keep gains nothing from a picture held for it — while each one is a whole screen's worth of
    /// memory, held for a second at most.
    /// </summary>
    private const int Kept = 4;

    private readonly Lock _gate = new();

    /// <summary>The last picture of each source, under the way it was read.</summary>
    private readonly Dictionary<(long Source, CaptureWay Method), (ScreenShot Shot, long Taken)>
        _kept = [];

    /// <summary>When the last look was allowed, which is what the next one is measured from.</summary>
    private long _turn;

    /// <summary>Whether <see cref="_turn"/> means anything yet.</summary>
    private bool _turned;

    private int _cache;

    /// <summary>
    /// How long a picture that was taken stays worth reading again, in milliseconds. Zero means
    /// every look takes a fresh one. Anything longer is a deliberate trade: the frame is read again
    /// instead of taken again, so a run spends less on capturing and matching, and a step can be
    /// handed a picture of the screen as it was that many milliseconds ago.
    /// </summary>
    public int CacheMilliseconds
    {
        get => _cache;
        set
        {
            _cache = Math.Max(0, value);
            if (_cache > 0)
            {
                return;
            }

            // Nothing in hand may be read any more, and a picture of a whole screen is worth letting
            // go of now rather than holding until some later look happens to replace it.
            lock (_gate)
            {
                _kept.Clear();
            }
        }
    }

    /// <summary>
    /// The most pictures a second the machine hands out. Zero, or anything at or above the display's
    /// own rate, means the display's rate: asking for more only reads the same frame twice.
    /// </summary>
    public int MaxFramesPerSecond { get; set; }

    /// <summary>
    /// How long a look waits for its turn, in milliseconds, and the booking that keeps two runs from
    /// taking the same turn: a look that asks while another is waiting is given the turn after it.
    /// That is what makes the ceiling the machine's — several macros recognising pictures at once
    /// take one look each in turn rather than each taking a ceiling's worth.
    /// </summary>
    public int Turn(int gap)
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            if (!_turned || now >= _turn)
            {
                _turned = true;
                _turn = now + Ticks(gap);
                return 0;
            }

            var left = _turn - now;
            _turn += Ticks(gap);
            return Math.Max(1, (int)Math.Ceiling(Milliseconds(left)));
        }
    }

    /// <summary>
    /// The picture in hand of one source, when it was taken no longer ago than
    /// <paramref name="age"/>. A picture older than that is dropped rather than kept for a look
    /// that could not use it.
    /// </summary>
    public bool Recall(long source, CaptureWay method, int age, out ScreenShot kept)
    {
        var key = (source, method);
        lock (_gate)
        {
            kept = default;
            if (!_kept.TryGetValue(key, out var picture))
            {
                return false;
            }

            if (Stopwatch.GetElapsedTime(picture.Taken).TotalMilliseconds > age)
            {
                _kept.Remove(key);
                return false;
            }

            kept = picture.Shot;
            return true;
        }
    }

    /// <summary>
    /// Keeps one picture for the look after this one. The oldest is let go when the shelf is full,
    /// so a macro reading one thing after another cannot grow it without a ceiling.
    /// </summary>
    public void Keep(long source, CaptureWay method, ScreenShot shot)
    {
        lock (_gate)
        {
            _kept[(source, method)] = (shot, Stopwatch.GetTimestamp());
            while (_kept.Count > Kept)
            {
                _kept.Remove(_kept.MinBy(picture => picture.Value.Taken).Key);
            }
        }
    }

    /// <summary>One gap of milliseconds in the clock's own ticks.</summary>
    private static long Ticks(int milliseconds)
        => milliseconds <= 0 ? 0 : (long)milliseconds * Stopwatch.Frequency / 1000;

    /// <summary>The same gap read back, for a caller that waits in milliseconds.</summary>
    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

/// <summary>
/// A picture that was taken, and where its top left corner sits on the screen. The two travel
/// together because the picture is not always the rectangle that was asked for — a window read
/// through graphics capture is its own size, at its own corner — and everything the run does with
/// the picture afterwards is in screen pixels. Which way it was taken is beside them, because
/// "automatic" is a question rather than an answer, and a step that came out wrong is worth being
/// able to ask what it was really read with.
/// </summary>
public readonly record struct ScreenShot(ImageFrame Frame, ScreenPoint Origin, CaptureWay Method);

/// <summary>
/// One region to read, and where its pixels should come from. The rectangle is in screen pixels,
/// the way every other coordinate a macro writes is; naming a window only says where to reach for
/// those pixels, and turning the two into each other is the device's business rather than the
/// macro's.
/// </summary>
public sealed record ScreenCaptureRequest(
    int X,
    int Y,
    int Width,
    int Height,
    CaptureWay Method = CaptureWay.Auto,
    long Window = 0)
{
    /// <summary>True when the pixels are to come from a window rather than from the desktop.</summary>
    public bool IsWindow => Window != 0;

    /// <summary>The same rectangle, moved, which is how a search area is placed inside a picture.</summary>
    public ScreenCaptureRequest At(int x, int y) => this with { X = x, Y = y };
}
