using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices;

// The screen: pixels, regions, colours and the pictures taken off it.
/// <summary>The screen: its size, its pixels, and pictures of it.</summary>
public interface IScreenDevice
{
    /// <summary>
    /// How the desk is laid out right now: one rectangle covering every display, which is what "the
    /// whole screen" means, and the displays it is made of. Read afresh each time because a display
    /// is plugged in, unplugged or rearranged while the program runs.
    /// </summary>
    ScreenLayout Desktop { get; }

    /// <summary>The colour of one pixel.</summary>
    PixelColor PixelAt(int x, int y);

    /// <summary>
    /// Where the pointer is right now, in screen pixels. It is a fact about the screen rather than
    /// about the way input is sent, so it is asked here and nowhere else: the three ways of sending
    /// input and the windows whose pickers follow the pointer all mean the same position, and one
    /// place to read it is what keeps them from drifting apart.
    /// </summary>
    ScreenPoint Cursor { get; }

    /// <summary>Copies a region of the desktop, the way this machine reads it by itself.</summary>
    ImageFrame Capture(int x, int y, int width, int height);

    /// <summary>
    /// Copies a region of the screen, taking the pixels from the place and by the means the
    /// request names, and saying where the picture it hands back sits. A request that names a
    /// window is answered with what the window shows — which a covering window does not change —
    /// and one that does not is answered with the desktop.
    /// </summary>
    ScreenShot Capture(ScreenCaptureRequest request);

    /// <summary>
    /// Lets go of the capture sessions kept for reuse, without making the screen unusable: a later
    /// capture opens what it needs again. Switching the macros off asks for this because nothing is
    /// then waiting to look at the screen. A device with nothing to keep answers by doing nothing.
    /// </summary>
    void ReleaseCaptures()
    {
    }

    /// <summary>
    /// How long one refresh of the screen lasts, in milliseconds. It is the shortest wait between
    /// two looks at the screen that can see something the look before it did not, because a picture
    /// that has not been drawn again cannot differ from the one already read; a step that waits for
    /// something to turn up looks again on this beat unless it was told another. A device that
    /// cannot say answers with 16, one refresh of a 60 Hz display.
    /// </summary>
    int RefreshMilliseconds => 16;

    /// <summary>
    /// The same rate said the way a display is sold and the settings window shows it: whole
    /// refreshes per second, 60 on a 60 Hz display. Milliseconds are what the engine waits in, but
    /// nobody sets a limit in them, so the one place that knows both is here rather than a divide
    /// at every call site. A device that cannot say answers with the rate its milliseconds come to.
    /// </summary>
    int RefreshHertz => Math.Max(1, (int)Math.Round(1000.0 / Math.Max(1, RefreshMilliseconds)));
}
