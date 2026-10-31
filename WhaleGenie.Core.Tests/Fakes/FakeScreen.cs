using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend screen: what it shows, where the pointer is, and every reading of it the engine asked
/// for.
/// </summary>
internal sealed class FakeScreen(FakeDesk desk) : IScreenDevice
{
    /// <summary>
    /// Where the pointer is right now. Read here and moved through the input device, which is the
    /// same split a real machine has: the position is a fact about the desk, not about the way a
    /// click was sent.
    /// </summary>
    public ScreenPoint Cursor { get; set; } = new(7, 9);

    /// <summary>
    /// How the pretend desk is laid out. One display to begin with; a check about several sets this
    /// to a layout with more, in the coordinates Windows would report them in.
    /// </summary>
    public ScreenLayout Desktop { get; set; } = new(new ScreenArea(0, 0, 100, 50),
        [new ScreenDisplay(new ScreenArea(0, 0, 100, 50), true)]);

    /// <summary>
    /// How long one refresh of the pretend screen lasts. A check that wants to see how often a
    /// waiting step looks sets it low, and one that does not care gets a fast screen.
    /// </summary>
    public int RefreshMilliseconds { get; set; } = 1;

    /// <summary>What a pixel reads as, for a screen that is not drawn pixel by pixel.</summary>
    public PixelColor Pixel { get; set; }

    /// <summary>
    /// Every reading of the screen that was asked for, in the order it was asked: which pixels,
    /// from where, and by which means. A check reads this to see how a step's fields were turned
    /// into a request rather than only that something was captured.
    /// </summary>
    public List<ScreenCaptureRequest> Captures { get; } = [];

    /// <summary>What the pretend screen shows: a capture cuts the rectangle asked for out of it.</summary>
    public ImageFrame? Display { get; set; }

    /// <summary>
    /// What the pretend screen shows next, asked after every capture, for the checks that need a
    /// picture that moves: one that moves twice and then holds, or one that never holds at all.
    /// Nothing here leaves the screen as it was.
    /// </summary>
    public Func<ImageFrame>? Next { get; set; }

    /// <summary>
    /// How this machine has been told to read the screen. Automatic unless a check is about the
    /// choice itself, which is what a machine nobody has set up answers.
    /// </summary>
    public CaptureChoice Ways { get; } = new();

    public PixelColor PixelAt(int x, int y)
    {
        desk.Note($"pixel {x} {y}");
        return Display is { } display ? display[x, y] : Pixel;
    }

    public ImageFrame Capture(int x, int y, int width, int height)
        => Capture(new ScreenCaptureRequest(x, y, width, height)).Frame;

    public ScreenShot Capture(ScreenCaptureRequest request)
    {
        Captures.Add(request);
        desk.Note($"capture {request.X} {request.Y} {request.Width} {request.Height}");
        var picture = Display is { } display
            ? ScreenCut(display, request.X, request.Y, request.Width, request.Height)
            : new ImageFrame(request.Width, request.Height, new byte[request.Width * request.Height * 4]);

        // A screen that moves between two captures is what waiting for the picture to settle is
        // about, so a check writes what the screen does next.
        if (Next is { } next)
        {
            Display = next();
        }

        // What a real machine answers an automatic request with: the window itself when a window
        // was named, and the pixels of the desktop when it was not.
        return new ScreenShot(picture, new ScreenPoint(request.X, request.Y),
            request.Method != CaptureWay.Auto
                ? request.Method
                : request.Window == 0 ? CaptureWay.Gdi : CaptureWay.GraphicsCapture);
    }

    /// <summary>
    /// The rectangle of the pretend screen a step asked for, with anything running off the edge
    /// left black the way a real capture leaves it.
    /// </summary>
    private static ImageFrame ScreenCut(ImageFrame display, int x, int y, int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var from = ((y + row) * display.Width + (x + column)) * 4;
                if (from < 0 || from + 4 > display.Bgra.Length)
                {
                    continue;
                }

                Array.Copy(display.Bgra, from, bytes, (row * width + column) * 4, 4);
            }
        }

        return new ImageFrame(width, height, bytes);
    }
}
