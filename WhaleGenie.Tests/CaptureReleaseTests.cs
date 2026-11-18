using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// Switching the system off is the point at which no trigger is waiting to read the screen. The
/// capture sessions kept for reuse should go then rather than stay until the program exits.
/// </summary>
public class CaptureReleaseTests
{
    [Fact]
    public void Switching_the_system_off_releases_the_screen_captures()
    {
        var screen = new ReleaseCountingScreen();
        var devices = new ChecksDevices { Screen = screen };
        using var service = new MacroTriggerService(() => [], devices, new NoWindowEvents(),
            NullDeviceLayer.Instance.Hook);

        service.IsEnabled = true;
        Assert.Equal(0, screen.Releases);

        service.IsEnabled = false;

        Assert.Equal(1, screen.Releases);
    }

    private sealed class ReleaseCountingScreen : IScreenDevice
    {
        private readonly IScreenDevice _inner = NullDeviceLayer.Instance.Screen;

        public int Releases { get; private set; }

        public ScreenLayout Desktop => _inner.Desktop;

        public PixelColor PixelAt(int x, int y) => _inner.PixelAt(x, y);

        public ScreenPoint Cursor => _inner.Cursor;

        public ImageFrame Capture(int x, int y, int width, int height)
            => _inner.Capture(x, y, width, height);

        public ScreenShot Capture(ScreenCaptureRequest request) => _inner.Capture(request);

        public void ReleaseCaptures() => Releases++;
    }
}
