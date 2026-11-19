using System;

namespace WhaleGenie.Storage;

/// <summary>
/// The size a picture file says it has, read from its own header. The MCP image tool reports the
/// width and height it just stored, and decoding the whole picture for two numbers would drag the
/// interface toolkit into a server that does not otherwise need one. The four formats the tool
/// accepts are the four headers read here; anything else answers zeroes and the caller says so.
/// </summary>
public static class ImageSize
{
    /// <summary>The width and height written in the file's header, or zeroes when it is not read.</summary>
    public static (int Width, int Height) Of(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            // PNG: the IHDR chunk always comes first, and its width/height are big-endian.
            return (BigEndian(bytes[16..20]), BigEndian(bytes[20..24]));
        }

        if (bytes.Length >= 10 && bytes[..3].SequenceEqual("GIF"u8))
        {
            return (LittleEndian16(bytes[6..8]), LittleEndian16(bytes[8..10]));
        }

        if (bytes.Length >= 26 && bytes[..2].SequenceEqual("BM"u8))
        {
            // BMP: a bottom-up bitmap stores a negative height, which is still the height.
            var width = Math.Abs(BitConverter.ToInt32(bytes[18..22]));
            var height = Math.Abs(BitConverter.ToInt32(bytes[22..26]));
            return (width, height);
        }

        return Jpeg(bytes);
    }

    private static (int Width, int Height) Jpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return (0, 0);
        }

        var at = 2;
        while (at + 3 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                at++;
                continue;
            }

            var marker = bytes[at + 1];
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                at += 2;
                continue;
            }

            var length = (bytes[at + 2] << 8) | bytes[at + 3];
            if (length < 2 || at + 2 + length > bytes.Length)
            {
                return (0, 0);
            }

            // SOF0..SOF15 carry the size; C4 (Huffman), C8 (JPEG extensions) and CC (arithmetic)
            // share the range but are not frame headers.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (at + 9 > bytes.Length)
                {
                    return (0, 0);
                }

                var height = (bytes[at + 5] << 8) | bytes[at + 6];
                var width = (bytes[at + 7] << 8) | bytes[at + 8];
                return (width, height);
            }

            at += 2 + length;
        }

        return (0, 0);
    }

    private static int BigEndian(ReadOnlySpan<byte> bytes)
        => (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static int LittleEndian16(ReadOnlySpan<byte> bytes)
        => bytes[0] | (bytes[1] << 8);
}
