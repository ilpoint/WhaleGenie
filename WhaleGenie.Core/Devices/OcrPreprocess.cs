using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// Cleaning a picture up before the OCR is asked to read it: small or low-contrast writing on a
/// busy background is where a little work on the picture pays off more than anything else.
/// </summary>
/// <remarks>
/// This is plain pixel work with no screen and no engine behind it, so a frame goes in and a frame
/// comes out and the whole thing can be tested on pictures a test built itself.
/// </remarks>
public static class OcrPreprocess
{
    /// <summary>What a step can ask for, in the order the interface offers it.</summary>
    public static IReadOnlyList<string> Recipes { get; } =
    [
        "none",
        "greyscale",
        "binarize",
        "upscale",
        "upscaleBinarize",
    ];

    /// <summary>
    /// The picture as the recipe wants it, together with how much bigger it was made. Everything
    /// the engine reads out of a bigger picture comes back that much further apart, so the caller
    /// needs the number to put the positions back where they belong.
    /// </summary>
    public static (ImageFrame Frame, double Scale) Apply(ImageFrame frame, string recipe) => recipe
        .Trim().ToLowerInvariant() switch
    {
        "greyscale" => (Greyscale(frame), 1),
        "binarize" => (Binarize(Greyscale(frame)), 1),
        "upscale" => (Upscale(frame, 2), 2),
        "upscalebinarize" => (Binarize(Greyscale(Upscale(frame, 2))), 2),
        _ => (frame, 1),
    };

    /// <summary>The picture in shades of grey, so the reading does not depend on the colours.</summary>
    public static ImageFrame Greyscale(ImageFrame frame)
    {
        if (frame.IsEmpty)
        {
            return frame;
        }

        var pixels = new byte[frame.Width * frame.Height * 4];
        for (var at = 0; at + 3 < frame.Bgra.Length && at + 3 < pixels.Length; at += 4)
        {
            var grey = Luminance(frame.Bgra[at], frame.Bgra[at + 1], frame.Bgra[at + 2]);
            pixels[at] = grey;
            pixels[at + 1] = grey;
            pixels[at + 2] = grey;
            pixels[at + 3] = frame.Bgra[at + 3];
        }

        return new ImageFrame(frame.Width, frame.Height, pixels);
    }

    /// <summary>
    /// The picture in black and white, cut at its own average brightness. Reading the average off
    /// the picture rather than fixing a number means a dark screen and a light one are both handled
    /// the same way, which a fixed cut in the middle cannot do.
    /// </summary>
    public static ImageFrame Binarize(ImageFrame frame)
    {
        if (frame.IsEmpty)
        {
            return frame;
        }

        var total = 0L;
        var counted = 0;
        for (var at = 0; at + 3 < frame.Bgra.Length && at + 3 < frame.Width * frame.Height * 4;
             at += 4)
        {
            total += frame.Bgra[at];
            counted++;
        }

        if (counted == 0)
        {
            return frame;
        }

        var cut = (byte)Math.Clamp(total / counted, 0, 255);
        var pixels = new byte[frame.Width * frame.Height * 4];
        for (var at = 0; at + 3 < frame.Bgra.Length && at + 3 < pixels.Length; at += 4)
        {
            var value = frame.Bgra[at] >= cut ? (byte)255 : (byte)0;
            pixels[at] = value;
            pixels[at + 1] = value;
            pixels[at + 2] = value;
            pixels[at + 3] = frame.Bgra[at + 3];
        }

        return new ImageFrame(frame.Width, frame.Height, pixels);
    }

    /// <summary>
    /// The picture made bigger, so writing that is only a few pixels high has something to be read
    /// from. The pixels are copied as they are: the engine does its own smoothing, and a copy of
    /// each pixel keeps the edges where the writing actually is.
    /// </summary>
    public static ImageFrame Upscale(ImageFrame frame, int times)
    {
        if (frame.IsEmpty || times < 2)
        {
            return frame;
        }

        var width = frame.Width * times;
        var height = frame.Height * times;
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var from = (y * frame.Width + x) * 4;
                for (var row = 0; row < times; row++)
                {
                    for (var column = 0; column < times; column++)
                    {
                        var to = ((y * times + row) * width + x * times + column) * 4;
                        pixels[to] = frame.Bgra[from];
                        pixels[to + 1] = frame.Bgra[from + 1];
                        pixels[to + 2] = frame.Bgra[from + 2];
                        pixels[to + 3] = frame.Bgra[from + 3];
                    }
                }
            }
        }

        return new ImageFrame(width, height, pixels);
    }

    /// <summary>How bright a pixel looks to the eye, from its blue, green and red parts.</summary>
    private static byte Luminance(byte blue, byte green, byte red)
        => (byte)Math.Clamp((int)Math.Round(0.114 * blue + 0.587 * green + 0.299 * red), 0, 255);
}
