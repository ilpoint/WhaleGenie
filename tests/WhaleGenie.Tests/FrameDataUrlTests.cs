using System;
using WhaleGenie.Core.Devices;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The debugger look is a BMP data URL: the prefix, header and length are what the browser reads,
/// and a frame larger than the thumbnail is scaled to fit without changing its shape.
/// </summary>
public class FrameDataUrlTests
{
    private const string Prefix = "data:image/bmp;base64,";

    [Fact]
    public void A_frame_is_written_as_a_bmp_data_url()
    {
        var frame = new ImageFrame(4, 3, new byte[4 * 3 * 4]);

        var bytes = Decode(FrameDataUrl.ToDataUrl(frame));

        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        Assert.Equal(bytes.Length, BitConverter.ToInt32(bytes, 2));
        Assert.Equal(54, BitConverter.ToInt32(bytes, 10));
        Assert.Equal(40, BitConverter.ToInt32(bytes, 14));
        Assert.Equal(frame.Width, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(frame.Height, BitConverter.ToInt32(bytes, 22));
        Assert.Equal((short)1, BitConverter.ToInt16(bytes, 26));
        Assert.Equal((short)32, BitConverter.ToInt16(bytes, 28));
        Assert.Equal(frame.Width * frame.Height * 4, BitConverter.ToInt32(bytes, 34));
        Assert.Equal(54 + (frame.Width * frame.Height * 4), bytes.Length);
    }

    [Fact]
    public void A_frame_larger_than_the_thumbnail_is_scaled_to_the_longest_side()
    {
        var frame = new ImageFrame(FrameDataUrl.LookMaxSide * 2, FrameDataUrl.LookMaxSide,
            new byte[FrameDataUrl.LookMaxSide * 2 * FrameDataUrl.LookMaxSide * 4]);

        var bytes = Decode(FrameDataUrl.ToDataUrl(frame));

        Assert.Equal(FrameDataUrl.LookMaxSide, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(FrameDataUrl.LookMaxSide / 2, BitConverter.ToInt32(bytes, 22));
        Assert.Equal(bytes.Length, BitConverter.ToInt32(bytes, 2));
        Assert.Equal(54 + (FrameDataUrl.LookMaxSide * (FrameDataUrl.LookMaxSide / 2) * 4), bytes.Length);
    }

    [Fact]
    public void The_first_source_row_is_written_at_the_end_of_the_pixels()
    {
        var frame = new ImageFrame(1, 2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var bytes = Decode(FrameDataUrl.ToDataUrl(frame));

        Assert.Equal((byte)5, bytes[54]);
        Assert.Equal((byte)6, bytes[55]);
        Assert.Equal((byte)7, bytes[56]);
        Assert.Equal((byte)8, bytes[57]);
        Assert.Equal((byte)1, bytes[58]);
        Assert.Equal((byte)2, bytes[59]);
        Assert.Equal((byte)3, bytes[60]);
        Assert.Equal((byte)4, bytes[61]);
    }

    private static byte[] Decode(string dataUrl)
    {
        Assert.StartsWith(Prefix, dataUrl, StringComparison.Ordinal);
        return Convert.FromBase64String(dataUrl[Prefix.Length..]);
    }
}
