using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;


namespace WhaleGenie.Core.Tests;

// Pictures taken off the screen or the clipboard, and the pixel a step watched.
public partial class RunLookTests
{
    [Fact]
    public async Task A_picture_that_was_taken_is_handed_over_as_the_picture_it_took()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Capture(1, 2, 5, 6, Hit("image", "shot"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Capture, look.Kind);
        Assert.Equal(new ScreenPoint(1, 2), look.Origin);
        Assert.Equal(5, look.Frame.Width);
        Assert.Equal(6, look.Frame.Height);
        Assert.Equal(new ScreenPoint(1, 2), Assert.Single(look.Boxes).Match.Location);
        Assert.Equal("shot", look.Looking);
    }

    [Fact]
    public async Task A_picture_taken_from_the_clipboard_is_handed_over_as_the_picture_it_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Picture = new ImageFrame(4, 3, new byte[48]);
        var looks = new Watched();
        var step = Step("clipboard.readImage", Returns("image=shot", "width=w", "height=h"));

        await Run(step, devices, looks);

        // 这张图不是从屏幕上看的，所以没有原点、没有标记、没有阈值，也没有在找什么。
        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Clipboard, look.Kind);
        Assert.Equal(4, look.Frame.Width);
        Assert.Equal(3, look.Frame.Height);
        Assert.Equal(new ScreenPoint(0, 0), look.Origin);
        Assert.Empty(look.Boxes);
        Assert.Equal(0, look.ChosenIndex);
        Assert.Null(look.Minimum);
        Assert.Equal(string.Empty, look.Looking);
    }

    [Fact]
    public async Task A_watched_pixel_is_handed_over_with_a_square_of_screen_around_it()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("vision.getPixel", Param("x", "50"), Param("y", "25"),
            Returns("color=color"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Pixel, look.Kind);
        Assert.Equal(2, look.Boxes.Count);
        Assert.Equal(LookRole.Target, look.Boxes[1].Role);
        Assert.Equal(new ScreenPoint(50, 25), look.Boxes[1].Match.Location);
        Assert.Equal(2, look.ChosenIndex);
        Assert.False(look.Frame.IsEmpty);
    }
}
