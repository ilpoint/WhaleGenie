using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The PNG a failed run leaves behind. The file is taken apart the way a reader would: the
/// signature, the header, the pixels after the row filter byte, and the two check values the
/// format fixes to a known number.
/// </summary>
public class PngWriterTests
{
    /// <summary>One row of two pixels, red then green, in the order the screen device reports them.</summary>
    private static ImageFrame Frame() => new(2, 1, [0, 0, 255, 0, 0, 255, 0, 0]);

    [Fact]
    public void The_file_starts_with_the_eight_bytes_a_png_starts_with()
    {
        var png = PngWriter.Encode(Frame());

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
    }

    [Fact]
    public void The_header_names_the_size_and_the_kind_of_picture()
    {
        var png = PngWriter.Encode(Frame());

        Assert.Equal("IHDR", Text(png, 12));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(8, png[24]);   // eight bits to a channel
        Assert.Equal(6, png[25]);   // colour, with an alpha channel
        Assert.Equal(0, png[26]);   // the rows are compressed with deflate
        Assert.Equal(0, png[27]);   // the encoder picked the filters, not the format
        Assert.Equal(0, png[28]);   // not interlaced
    }

    [Fact]
    public void The_header_carries_the_check_the_format_works_out()
    {
        // Worked out elsewhere, over "IHDR" and the thirteen header bytes of a 2 by 1 picture,
        // so a fingerprint of this code would not pass as the answer.
        var png = PngWriter.Encode(Frame());

        Assert.Equal(0xF4227F8Au, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(29)));
    }

    [Fact]
    public void The_pixels_come_back_in_the_order_the_format_wants()
    {
        var rows = Inflate(PngWriter.Encode(Frame()));

        // A byte of row filter — zero, meaning the row stands as it is — then red, green, blue
        // and a solid alpha for each pixel. The frame said alpha zero and it comes out solid,
        // because a screen capture leaves that byte behind.
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255, 0, 255, 0, 255 }, rows);
    }

    [Fact]
    public void The_file_ends_with_the_chunk_that_says_it_is_over()
    {
        var png = PngWriter.Encode(Frame());

        Assert.Equal("IEND", Text(png, png.Length - 8));
        Assert.Equal(0xAE426082u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(png.Length - 4)));
    }

    /// <summary>The four letters a chunk is called by.</summary>
    private static string Text(byte[] png, int at)
        => System.Text.Encoding.ASCII.GetString(png, at, 4);

    /// <summary>Reads the pixels back the way a reader does: the body of the data chunk, unpacked.</summary>
    private static byte[] Inflate(byte[] png)
    {
        // The header chunk is the same size whatever the picture is: the eight bytes of signature,
        // then thirteen bytes of body, then four of length and four of name before it.
        var at = 8 + 12 + 13;
        var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
        var packed = png[(at + 8)..(at + 8 + length)];

        using var rows = new MemoryStream();
        using (var inflate = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress))
        {
            inflate.CopyTo(rows);
        }

        return rows.ToArray();
    }
}
