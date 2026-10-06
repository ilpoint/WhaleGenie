using System;

namespace Viktor.Core.Devices;

/// <summary>
/// Reads and writes the device-independent bitmap the Windows clipboard carries a picture in.
/// It is kept away from the clipboard device so the byte layout can be checked on any machine:
/// a DIB is a header, sometimes a colour table, and the pixels — and getting the row order or the
/// stride wrong shows up as a picture that is upside down or striped rather than as an error.
/// </summary>
public static class ClipboardImage
{
    /// <summary>The size of the plain header, which is what this writer produces.</summary>
    private const int HeaderSize = 40;

    /// <summary>How many bytes the masks of a bit-field header take up after the plain one.</summary>
    private const int BitFieldMasks = 12;

    /// <summary>
    /// The picture a device-independent bitmap holds, or null when the bytes are not one this
    /// reader knows. Only uncompressed pictures are read: that is what copying from a screen or a
    /// drawing program produces, and anything else is better reported as "no picture" than turned
    /// into something that only looks right.
    /// </summary>
    public static ImageFrame? FromDib(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            return null;
        }

        var header = ReadInt(bytes, 0);
        var width = ReadInt(bytes, 4);
        var height = ReadInt(bytes, 8);
        var bitCount = ReadShort(bytes, 14);
        var compression = ReadInt(bytes, 16);

        // A height of zero is nothing to draw and a negative one means the rows run the other way,
        // which is the one variation worth following.
        if (header < HeaderSize || width <= 0 || height == 0
            || bitCount is not (24 or 32) || compression is not (0 or 3))
        {
            return null;
        }

        var rows = Math.Abs(height);
        var stride = (width * bitCount + 31) / 32 * 4;
        var table = bitCount <= 8 ? (1 << bitCount) * 4 : 0;
        var masks = compression == 3 && header == HeaderSize ? BitFieldMasks : 0;
        var start = header + table + masks;

        if (bytes.Length < start + (long)stride * rows)
        {
            return null;
        }

        var pixels = new byte[(long)width * rows * 4];
        for (var row = 0; row < rows; row++)
        {
            // A plain header writes the rows bottom-up; a negative height is the same file saying
            // they are the right way up already.
            var source = start + (height > 0 ? rows - 1 - row : row) * stride;
            var target = row * width * 4;

            for (var column = 0; column < width; column++)
            {
                var from = source + column * (bitCount / 8);
                var to = target + column * 4;
                pixels[to] = bytes[from];
                pixels[to + 1] = bytes[from + 1];
                pixels[to + 2] = bytes[from + 2];
                pixels[to + 3] = bitCount == 32 ? bytes[from + 3] : (byte)255;
            }
        }

        return new ImageFrame(width, rows, pixels);
    }

    /// <summary>
    /// The device-independent bitmap for a picture. It is written bottom-up with a positive
    /// height, which is the shape everything that reads a DIB understands without being asked.
    /// </summary>
    public static byte[] ToDib(ImageFrame image)
    {
        var stride = image.Width * 4;
        var bytes = new byte[HeaderSize + stride * image.Height];

        WriteInt(bytes, 0, HeaderSize);
        WriteInt(bytes, 4, image.Width);
        WriteInt(bytes, 8, image.Height);
        WriteShort(bytes, 12, 1);
        WriteShort(bytes, 14, 32);
        WriteInt(bytes, 16, 0);
        WriteInt(bytes, 20, stride * image.Height);

        // The rest of the header is the resolution and the palette, and every reader takes zeroes
        // there for an answer.
        for (var row = 0; row < image.Height; row++)
        {
            var source = row * stride;
            var target = HeaderSize + (image.Height - 1 - row) * stride;
            image.Bgra.AsSpan(source, stride).CopyTo(bytes.AsSpan(target, stride));
        }

        return bytes;
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes, int at)
        => bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24;

    private static int ReadShort(ReadOnlySpan<byte> bytes, int at) => bytes[at] | bytes[at + 1] << 8;

    private static void WriteInt(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
        bytes[at + 2] = (byte)(value >> 16);
        bytes[at + 3] = (byte)(value >> 24);
    }

    private static void WriteShort(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
    }
}
