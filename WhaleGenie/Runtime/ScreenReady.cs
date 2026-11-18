using System;
using System.Runtime.InteropServices;
using System.Threading;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Runtime;

/// <summary>
/// Takes a picture of a screen that is known to be the one on screen now.
/// </summary>
/// <remarks>
/// Hiding a window is one call, but the picture the desktop hands over is not that quick. The
/// duplicator behind <see cref="IScreenDevice.Capture(int, int, int, int)"/> answers with the
/// picture it holds when nothing new has been presented yet, so a shot taken the instant a window
/// went away comes back with that window still in it — which is what put the program's own windows
/// into every region, coordinate and colour picked through them. Waiting for composition is not
/// enough on its own: the compositor may have drawn the desktop again while the duplicator has not
/// been handed the new frame. So the picture is taken repeatedly and kept only once two shots in a
/// row agree, which is the first moment the screen has stopped changing under the picker.
/// </remarks>
internal static class ScreenReady
{
    /// <summary>How many shots to take before settling for the last one, and how far apart.</summary>
    private const int Tries = 6;

    private const int GapMs = 24;

    /// <summary>
    /// Blocks until the desktop has been composed twice more, which is one frame past the one that
    /// still had a window that has just gone.
    /// </summary>
    public static void Wait()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            DwmFlush();
            DwmFlush();
        }
        catch (Exception)
        {
            // A machine that composes some other way has nothing to wait for, and a picture taken
            // without waiting is what it would have been anyway.
        }
    }

    /// <summary>
    /// The rectangle as it stands once the screen has stopped changing under it. A screen that keeps
    /// changing — a video, an animation — is answered with the last shot rather than waited on for
    /// ever: what a picker needs is a picture, not a still life.
    /// </summary>
    public static ImageFrame Capture(IScreenDevice screen, int x, int y, int width, int height)
    {
        Wait();

        var frame = Shot(screen, x, y, width, height);
        if (frame is null)
        {
            return ImageFrame.Empty;
        }

        for (var look = 0; look < Tries; look++)
        {
            Thread.Sleep(GapMs);
            Wait();

            if (Shot(screen, x, y, width, height) is not { } next)
            {
                break;
            }

            var settled = Same(frame, next);
            frame = next;
            if (settled)
            {
                break;
            }
        }

        return frame;
    }

    /// <summary>One shot, or null when the screen will not hand one over at all.</summary>
    private static ImageFrame? Shot(IScreenDevice screen, int x, int y, int width, int height)
    {
        try
        {
            var frame = screen.Capture(x, y, width, height);
            return frame.IsEmpty ? null : frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Same(ImageFrame one, ImageFrame other)
        => one.Width == other.Width
           && one.Height == other.Height
           && one.Bgra.AsSpan().SequenceEqual(other.Bgra);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
