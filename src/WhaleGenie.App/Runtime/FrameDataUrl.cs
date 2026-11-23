using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Runtime;

/// <summary>
/// A look frame as a data URL the page can put straight into an image. No image library on the
/// bridge thread, and browsers decode BMP; nearest-neighbour is enough for a debugger thumbnail.
/// </summary>
internal static class FrameDataUrl
{
    /// <summary>The longest side a look frame keeps; debugger frames do not need native detail.</summary>
    internal const int LookMaxSide = 320;

    /// <summary>A BMP data URL scaled to fit <see cref="LookMaxSide"/>.</summary>
    internal static string ToDataUrl(ImageFrame frame)
    {
        var sourceWidth = frame.Width;
        var sourceHeight = frame.Height;
        var scale = Math.Min(1.0, LookMaxSide / (double)Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var pixels = frame.Bgra;
        if (width != sourceWidth || height != sourceHeight)
        {
            var scaled = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
                for (var x = 0; x < width; x++)
                {
                    var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
                    Buffer.BlockCopy(pixels, (sourceY * sourceWidth + sourceX) * 4,
                        scaled, (y * width + x) * 4, 4);
                }
            }

            pixels = scaled;
        }

        return EncodeBmp(pixels, width, height);
    }

    private static string EncodeBmp(byte[] pixels, int width, int height)
    {
        var stride = width * 4;
        var size = 54 + stride * height;
        var bytes = new byte[size];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, size);
        WriteInt32(bytes, 10, 54);
        WriteInt32(bytes, 14, 40);
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, height);
        WriteInt16(bytes, 26, 1);
        WriteInt16(bytes, 28, 32);
        WriteInt32(bytes, 34, stride * height);
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(pixels, y * stride, bytes, 54 + (height - 1 - y) * stride, stride);
        }

        return "data:image/bmp;base64," + Convert.ToBase64String(bytes);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }
}
