using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Taking the picture to search, reusing one already taken, and the gap between frames.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The picture taken of one rectangle. A window-anchored rectangle is counted from that
    /// window's corner, and the origin handed back is the one on screen, so the positions read out
    /// of the picture are pixels the rest of the macro can click on.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin) Capture(ExecutableStep step, int x, int y,
        int width, int height)
    {
        var anchor = AnchorOf(step);
        var corner = Placed(anchor.Corner, x, y);
        var shot = Shot(corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// One picture of the screen, taken the way this machine is set up to take one: whether the
    /// pixels come off the screen or out of the window itself is an answer about the machine rather
    /// than about the step, and so is how long a picture already taken stays worth reading again.
    /// The window is the one the caller's coordinates are counted from, or the one it names
    /// outright.
    /// </summary>
    private ScreenShot Shot(int x, int y, int width, int height, WindowInfo? window = null)
    {
        // Which window this is about to be read out of is asked for before anything is read, so a
        // debugger can put a frame there while the reading is going on rather than after it.
        Reading(window);

        var method = window is null
            ? _devices.Capture.ForScreen()
            : _devices.Capture.ForWindow();

        var source = window?.Handle ?? 0;
        var age = Math.Max(0, _devices.Frames.CacheMilliseconds);
        if (age == 0)
        {
            // Nothing already taken is old enough to do, so the rectangle asked for is the rectangle
            // read: this is the reading a macro has always done, at the cost it has always had.
            var exact = _devices.Screen.Capture(new ScreenCaptureRequest(x, y, width, height,
                method, source));
            Taken(window, exact.Method);
            return exact;
        }

        var key = Reused(source, method);
        var now = Stopwatch.GetTimestamp();
        if (_frames.TryGetValue(key, out var kept)
            && Stopwatch.GetElapsedTime(kept.TakenAt, now).TotalMilliseconds <= age
            && Covers(kept.Frame, kept.Origin, x, y, width, height))
        {
            Taken(window, kept.Method);
            return Cut(kept.Frame, kept.Origin, x, y, width, height, kept.Method);
        }

        // The whole of what the step is reading, so that the next step wanting another part of it
        // is served without reading the screen again: asking for one rectangle costs more than that
        // rectangle, and asking for the whole source is what makes the next look free.
        var whole = Whole(method, window);
        _frames[key] = (whole.Frame, whole.Origin, whole.Method, now);
        Taken(window, whole.Method);
        return Cut(whole.Frame, whole.Origin, x, y, width, height, whole.Method);
    }

    /// <summary>
    /// Which pictures are worth keeping for a second look: one to each source, one to each way of
    /// reading it. Two steps reading the same window the same way see the same picture, and a step
    /// reading it another way gets its own — a window copied off the screen and a window read as the
    /// window itself shows it are not the same pixels. What is keyed on is the way that was asked
    /// for rather than the one that came back, because the next step has to look its picture up
    /// before anything is read: the automatic choice is a question, and this machine answers it the
    /// same way twice in a run.
    /// </summary>
    private static (long Source, CaptureWay Method) Reused(long source, CaptureWay method)
        => (source, method);

    /// <summary>The whole of what a step reads from: the window, or the screen.</summary>
    private ScreenShot Whole(CaptureWay method, WindowInfo? window)
    {
        if (window is null)
        {
            var desk = _devices.Screen.Desktop.Bounds;
            return _devices.Screen.Capture(
                new ScreenCaptureRequest(desk.Left, desk.Top, desk.Width, desk.Height, method));
        }

        return _devices.Screen.Capture(new ScreenCaptureRequest(window.Location.X, window.Location.Y,
            window.Size.Width, window.Size.Height, method, window.Handle));
    }

    /// <summary>True when a picture taken whole covers the rectangle a step is asking for.</summary>
    private static bool Covers(ImageFrame frame, ScreenPoint origin, int x, int y, int width,
        int height)
        => x >= origin.X && y >= origin.Y
           && x + width <= origin.X + frame.Width && y + height <= origin.Y + frame.Height;

    /// <summary>
    /// The part of a picture taken whole that a step asked for. A rectangle reaching past the
    /// picture is cut down to what is there, the way a window read as itself is, and one altogether
    /// outside the picture is nothing to look at.
    /// </summary>
    private static ScreenShot Cut(ImageFrame whole, ScreenPoint origin, int x, int y, int width,
        int height, CaptureWay method)
    {
        if (origin.X == x && origin.Y == y && whole.Width == width && whole.Height == height)
        {
            return new ScreenShot(whole, origin, method);
        }

        var left = Math.Max(0, x - origin.X);
        var top = Math.Max(0, y - origin.Y);
        var right = Math.Min(whole.Width, x - origin.X + width);
        var bottom = Math.Min(whole.Height, y - origin.Y + height);
        if (right <= left || bottom <= top)
        {
            throw new StepFailure(RunKeys.EmptyRegion, $"{x},{y} {width}x{height}");
        }

        var kept = new byte[(long)(right - left) * (bottom - top) * 4];
        for (var row = 0; row < bottom - top; row++)
        {
            Array.Copy(whole.Bgra, ((long)(top + row) * whole.Width + left) * 4, kept,
                (long)row * (right - left) * 4, (right - left) * 4);
        }

        return new ScreenShot(new ImageFrame(right - left, bottom - top, kept),
            new ScreenPoint(origin.X + left, origin.Y + top), method);
    }

    /// <summary>
    /// Notes what the picture a step is about to be given was taken of, and when. Every look passes
    /// through here, whether it read the screen or reused a picture, so this is also the run's own
    /// clock: the next look is measured from it, and a step that waits its beat out keeps the same
    /// clock as one that looks once.
    /// </summary>
    private void Taken(WindowInfo? window, CaptureWay method)
    {
        _takenWindow = window?.Title ?? string.Empty;
        _takenMethod = method;
        _lastFrame = Stopwatch.GetTimestamp();
    }

    /// <summary>Tells whoever is watching which window the next picture comes out of.</summary>
    private void Reading(WindowInfo? window) => _looks?.Reading(window);

    /// <summary>
    /// Waits out however much of the run's frame gap is left and notes the look. Every step that
    /// reads the screen comes through here, so the beat is the run's rather than a step's: a step is
    /// held to it whichever way it reads, and ten steps in one loop share it instead of each taking
    /// a picture of their own.
    /// </summary>
    private async Task Gate(CancellationToken token)
    {
        var left = Until();
        if (left > 0)
        {
            await Pause(left, token);
        }
    }

    /// <summary>
    /// How long the run still has to wait before it may take another picture: nothing when it has
    /// never taken one, or when the policy leaves no gap at all.
    /// </summary>
    private int Until()
    {
        if (_lastFrame == 0)
        {
            return 0;
        }

        return Math.Max(0, FrameGap() - (int)Stopwatch.GetElapsedTime(_lastFrame).TotalMilliseconds);
    }

    /// <summary>
    /// The shortest wait the run keeps between two pictures, from what the settings window stores:
    /// the cap the person set, or the display's own rate when that cap is absent or higher. A
    /// picture that has not been drawn again cannot differ from the one already read, so anything
    /// above the display's rate would only read the same frame twice at the cost of reading it.
    /// </summary>
    private int FrameGap()
    {
        var hertz = Math.Max(1, _devices.Screen.RefreshHertz);
        var asked = _devices.Frames.MaxFramesPerSecond;
        if (asked <= 0 || asked >= hertz)
        {
            return Math.Max(1, _devices.Screen.RefreshMilliseconds);
        }

        return Math.Max(1, (int)Math.Ceiling(1000.0 / asked));
    }
}
