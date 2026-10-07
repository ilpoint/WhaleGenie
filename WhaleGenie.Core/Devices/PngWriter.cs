using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace WhaleGenie.Core.Devices;

/// <summary>Writes a captured frame out as a PNG.</summary>
/// <remarks>
/// The picture taken of a failed run has to arrive whatever else has gone wrong, so the encoding
/// lives here rather than being borrowed from the vision stack: no native library is loaded, and a
/// machine where OpenCV will not start can still leave a picture behind. Only what a screen capture
/// needs is supported — eight bits to a channel, colour plus alpha, no interlacing.
/// </remarks>
public static class PngWriter
{
    /// <summary>The eight bytes every PNG starts with.</summary>
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>The table every CRC in the file is worked out with.</summary>
    private static readonly uint[] Crc = BuildCrcTable();

    public static byte[] Encode(ImageFrame frame)
    {
        if (frame.IsEmpty)
        {
            throw new ArgumentException("There are no pixels to save.", nameof(frame));
        }

        using var file = new MemoryStream();
        file.Write(Signature);

        var size = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(size, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(size.AsSpan(4), frame.Height);
        size[8] = 8;   // eight bits to a channel
        size[9] = 6;   // colour, with an alpha channel
        size[10] = 0;  // the rows are compressed with deflate
        size[11] = 0;  // the encoder picked the row filters, not the format
        size[12] = 0;  // not interlaced
        Chunk(file, "IHDR", size);

        Chunk(file, "IDAT", Deflate(Rows(frame)));
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>
    /// The pixels with the leading byte every PNG row carries. Filter 0 (none) is what an encoder
    /// picks when filtering would cost more than it saves, and a screen picture is noisy enough
    /// that it usually would.
    /// </summary>
    private static byte[] Rows(ImageFrame frame)
    {
        var stride = frame.Width * 4;
        var rows = new byte[(stride + 1) * frame.Height];

        for (var y = 0; y < frame.Height; y++)
        {
            var at = y * (stride + 1);
            rows[at] = 0;

            for (var x = 0; x < frame.Width; x++)
            {
                var from = (y * frame.Width + x) * 4;
                var to = at + 1 + (x * 4);
                rows[to] = frame.Bgra[from + 2];        // the frame holds blue, green, red, alpha
                rows[to + 1] = frame.Bgra[from + 1];
                rows[to + 2] = frame.Bgra[from];
                // A screen capture leaves the alpha byte zeroed, which would save as a picture
                // nothing can see, so every pixel is written out solid.
                rows[to + 3] = 255;
            }
        }

        return rows;
    }

    private static byte[] Deflate(byte[] rows)
    {
        using var packed = new MemoryStream();
        using (var deflate = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(rows);
        }

        return packed.ToArray();
    }

    /// <summary>
    /// Writes one chunk: how long its body is, what it is called, the body, and the check the
    /// reader uses to tell a whole file from a damaged one.
    /// </summary>
    private static void Chunk(Stream file, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);

        var body = new byte[4 + data.Length];
        for (var index = 0; index < 4; index++)
        {
            body[index] = (byte)type[index];
        }

        data.CopyTo(body.AsSpan(4));
        file.Write(body);

        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, Check(body));
        file.Write(check);
    }

    private static uint Check(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc = Crc[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
