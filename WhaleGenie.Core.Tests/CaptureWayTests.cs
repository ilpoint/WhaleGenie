using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The ways a window's picture can be taken, and the order the automatic choice tries them in. The
/// trying itself needs a real window to fall back through, so what is checked here is the order:
/// which way is reached for first, and what is left of it on a machine without graphics capture.
/// </summary>
public class CaptureWayTests
{
    [Fact]
    public void A_window_is_read_by_graphics_capture_first_and_off_the_screen_last()
    {
        CaptureMethod[] ways =
            [CaptureMethod.GraphicsCapture, CaptureMethod.PrintWindow, CaptureMethod.Gdi];

        Assert.Equal(ways, WindowsScreenDevice.Automatic(graphicsAvailable: true));
    }

    /// <summary>
    /// A machine that cannot do graphics capture — an old Windows, a remote session, no graphics
    /// driver — still reads a window, by asking the window to draw itself.
    /// </summary>
    [Fact]
    public void Without_graphics_capture_the_window_is_asked_to_draw_itself()
    {
        CaptureMethod[] ways = [CaptureMethod.PrintWindow, CaptureMethod.Gdi];

        Assert.Equal(ways, WindowsScreenDevice.Automatic(graphicsAvailable: false));
    }
}
