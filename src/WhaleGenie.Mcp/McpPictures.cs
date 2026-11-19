using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Mcp;

/// <summary>
/// A captured picture at a size an AI client will take: a whole 4K desktop as PNG is tens of
/// megabytes of base64, which most clients refuse outright, and the interesting part of a
/// debugging screenshot is usually readable at a twentieth of the size. The scaling averages
/// whole boxes of pixels rather than picking one out of each, so a line of text stays a line of
/// text instead of breaking up.
/// </summary>
internal static class McpPictures
{
    /// <summary>The same picture with its longest side at most <paramref name="maxSide"/> pixels.</summary>
    public static ImageFrame Scale(ImageFrame frame, int maxSide)
    {
        if (maxSide <= 0 || frame.IsEmpty)
        {
            return frame;
        }

        var longest = Math.Max(frame.Width, frame.Height);
        if (longest <= maxSide)
        {
            return frame;
        }

        var width = Math.Max(1, (int)Math.Round(frame.Width * (double)maxSide / longest));
        var height = Math.Max(1, (int)Math.Round(frame.Height * (double)maxSide / longest));
        if (width >= frame.Width || height >= frame.Height)
        {
            return frame;
        }

        var pixels = new byte[(long)width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var top = (int)((long)y * frame.Height / height);
            var bottom = Math.Min(frame.Height, Math.Max(top + 1, (int)((long)(y + 1) * frame.Height / height)));
            for (var x = 0; x < width; x++)
            {
                var left = (int)((long)x * frame.Width / width);
                var right = Math.Min(frame.Width, Math.Max(left + 1, (int)((long)(x + 1) * frame.Width / width)));

                long blue = 0;
                long green = 0;
                long red = 0;
                long count = 0;
                for (var row = top; row < bottom; row++)
                {
                    var from = (long)row * frame.Width;
                    for (var column = left; column < right; column++)
                    {
                        var at = (from + column) * 4;
                        blue += frame.Bgra[at];
                        green += frame.Bgra[at + 1];
                        red += frame.Bgra[at + 2];
                        count++;
                    }
                }

                var to = ((long)y * width + x) * 4;
                pixels[to] = (byte)(count == 0 ? 0 : blue / count);
                pixels[to + 1] = (byte)(count == 0 ? 0 : green / count);
                pixels[to + 2] = (byte)(count == 0 ? 0 : red / count);
                pixels[to + 3] = 255;
            }
        }

        return new ImageFrame(width, height, pixels);
    }
}
