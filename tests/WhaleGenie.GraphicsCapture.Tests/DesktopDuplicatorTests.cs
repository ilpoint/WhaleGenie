namespace WhaleGenie.GraphicsCapture.Tests;

/// <summary>
/// What a duplicated display hands over, as far as it can be decided without a display being there.
/// A fresh duplication's first picture is often one nothing has been drawn into yet, and the rule
/// that keeps such a picture away from a macro while still reading a screen that is standing still
/// is the one thing here worth pinning down.
/// </summary>
public class DesktopDuplicatorTests
{
    /// <summary>A picture of two by two pixels, which is as much as any of these rules look at.</summary>
    private static byte[] Picture(params byte[] pixels)
    {
        var frame = new byte[16];
        pixels.CopyTo(frame, 0);
        return frame;
    }

    [Fact]
    public void The_first_picture_of_a_duplication_is_served_only_when_it_has_something_in_it()
    {
        // The one a fresh duplication starts with is often blank, and a macro handed one would be
        // reading a picture of nothing; the screen is read another way instead.
        Assert.False(DesktopDuplicator.WorthServing(1, Picture()));

        // A first picture with something in it is the display, so it is what a still screen — the
        // one case where no second picture ever comes — is read by.
        Assert.True(DesktopDuplicator.WorthServing(1, Picture(0, 0, 0, 255, 32, 32, 32, 255)));
    }

    /// <summary>
    /// From the second picture on there is nothing left to be suspicious of: what came back is the
    /// display, and a display that is really dark is still the display.
    /// </summary>
    [Fact]
    public void Every_picture_after_the_first_is_served_however_dark_it_is()
        => Assert.True(DesktopDuplicator.WorthServing(2, Picture()));
}
