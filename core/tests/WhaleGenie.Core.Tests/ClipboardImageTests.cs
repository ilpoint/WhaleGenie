using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The picture format the clipboard carries, checked byte by byte. A device-independent bitmap
/// that is read with its rows the wrong way up, or with the wrong stride, is not an error — it is
/// a picture that looks almost right, which is the hardest kind of wrong to notice.
/// </summary>
public class ClipboardImageTests
{
    [Fact]
    public void A_picture_survives_the_trip_out_and_back()
    {
        var picture = Frame(3, 2);
        var back = ClipboardImage.FromDib(ClipboardImage.ToDib(picture));

        Assert.NotNull(back);
        Assert.Equal(3, back!.Width);
        Assert.Equal(2, back.Height);
        Assert.Equal(picture.Bgra, back.Bgra);
    }

    [Fact]
    public void Rows_that_run_the_other_way_are_read_the_right_way_up()
    {
        var picture = Frame(2, 3);
        var dib = ClipboardImage.ToDib(picture);

        // A negative height is the file saying its rows are already in top-down order, so the
        // reversal the writer does has to be undone here rather than applied twice.
        SetInt(dib, 8, -picture.Height);
        ReverseRows(dib, picture.Width, picture.Height);

        var back = ClipboardImage.FromDib(dib);

        Assert.NotNull(back);
        Assert.Equal(picture.Bgra, back!.Bgra);
    }

    [Fact]
    public void A_twenty_four_bit_picture_comes_back_with_an_opaque_alpha()
    {
        // One pixel of 24-bit colour, padded out to the four-byte row stride the format asks for.
        var bytes = new byte[40 + 4];
        SetInt(bytes, 0, 40);
        SetInt(bytes, 4, 1);
        SetInt(bytes, 8, 1);
        SetShort(bytes, 12, 1);
        SetShort(bytes, 14, 24);
        bytes[40] = 10;
        bytes[41] = 20;
        bytes[42] = 30;

        var back = ClipboardImage.FromDib(bytes);

        Assert.NotNull(back);
        Assert.Equal(new PixelColor(30, 20, 10), back![0, 0]);
        Assert.Equal(255, back.Bgra[3]);
    }

    [Fact]
    public void A_picture_that_is_not_the_shape_the_reader_knows_is_none()
    {
        Assert.Null(ClipboardImage.FromDib([]));
        Assert.Null(ClipboardImage.FromDib(new byte[40]));
        Assert.Null(ClipboardImage.FromDib([1, 2, 3]));
    }

    [Fact]
    public void A_picture_that_stops_halfway_is_none_rather_than_half_a_picture()
    {
        var dib = ClipboardImage.ToDib(Frame(4, 4));
        Assert.Null(ClipboardImage.FromDib(dib.AsSpan(0, dib.Length - 20)));
    }

    [Fact]
    public void A_colour_table_picture_the_reader_cannot_use_is_none()
    {
        var dib = ClipboardImage.ToDib(Frame(2, 2));
        SetShort(dib, 14, 8);
        Assert.Null(ClipboardImage.FromDib(dib));
    }

    [Fact]
    public void The_header_a_picture_is_written_with_says_what_every_reader_expects()
    {
        var image = Frame(3, 2);
        var dib = ClipboardImage.ToDib(image);

        Assert.Equal(40, ReadInt(dib, 0));
        Assert.Equal(3, ReadInt(dib, 4));
        Assert.Equal(2, ReadInt(dib, 8));
        Assert.Equal(1, ReadShort(dib, 12));
        Assert.Equal(32, ReadShort(dib, 14));
        Assert.Equal(0, ReadInt(dib, 16));
        Assert.Equal(3 * 4 * 2, ReadInt(dib, 20));

        // Bottom-up: the first row in the file is the last row of the picture.
        Assert.Equal(image.Bgra[12..16], dib[40..44]);
    }

    [Fact]
    public void A_bit_field_picture_skips_the_masks_between_the_header_and_the_pixels()
    {
        var bytes = new byte[40 + 12 + 4];
        SetInt(bytes, 0, 40);
        SetInt(bytes, 4, 1);
        SetInt(bytes, 8, 1);
        SetShort(bytes, 12, 1);
        SetShort(bytes, 14, 32);
        SetInt(bytes, 16, 3);
        bytes[52] = 10;
        bytes[53] = 20;
        bytes[54] = 30;
        bytes[55] = 40;

        var back = ClipboardImage.FromDib(bytes);

        Assert.NotNull(back);
        Assert.Equal(new PixelColor(30, 20, 10), back![0, 0]);
        Assert.Equal(40, back.Bgra[3]);
    }

    [Fact]
    public void A_twenty_four_bit_row_is_padded_to_the_stride_it_is_read_with()
    {
        var bytes = new byte[40 + 12];
        SetInt(bytes, 0, 40);
        SetInt(bytes, 4, 3);
        SetInt(bytes, 8, 1);
        SetShort(bytes, 12, 1);
        SetShort(bytes, 14, 24);
        bytes[40] = 10;
        bytes[41] = 20;
        bytes[42] = 30;
        bytes[43] = 40;
        bytes[44] = 50;
        bytes[45] = 60;
        bytes[46] = 70;
        bytes[47] = 80;
        bytes[48] = 90;

        var back = ClipboardImage.FromDib(bytes);

        Assert.NotNull(back);
        Assert.Equal(3, back!.Width);
        Assert.Equal(new PixelColor(30, 20, 10), back[0, 0]);
        Assert.Equal(new PixelColor(60, 50, 40), back[1, 0]);
        Assert.Equal(new PixelColor(90, 80, 70), back[2, 0]);
        Assert.Equal(255, back.Bgra[3]);
    }

    [Theory]
    [InlineData(0, 2, 32, 0)]     // a header that is not one
    [InlineData(39, 2, 32, 0)]
    [InlineData(40, 0, 32, 0)]    // nothing wide
    [InlineData(40, 2, 32, 0)]    // height zero, set below
    [InlineData(40, 2, 16, 0)]    // a depth the reader does not know
    [InlineData(40, 2, 32, 1)]    // a compression the reader does not know
    [InlineData(40, 2, 32, 4)]
    public void A_header_that_does_not_describe_an_ordinary_picture_is_none(
        int header, int width, int bitCount, int compression)
    {
        var bytes = new byte[40 + 32];
        SetInt(bytes, 0, header);
        SetInt(bytes, 4, width);
        SetInt(bytes, 8, header == 40 ? 0 : 2);
        SetShort(bytes, 14, bitCount);
        SetInt(bytes, 16, compression);

        Assert.Null(ClipboardImage.FromDib(bytes));
    }

    /// <summary>A picture where every byte is different, so a mixed-up row shows up at once.</summary>
    private static ImageFrame Frame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (byte)(index * 7 + 3);
        }

        return new ImageFrame(width, height, pixels);
    }

    private static void ReverseRows(byte[] dib, int width, int height)
    {
        var stride = width * 4;
        for (var row = 0; row < height / 2; row++)
        {
            for (var index = 0; index < stride; index++)
            {
                var top = 40 + row * stride + index;
                var bottom = 40 + (height - 1 - row) * stride + index;
                (dib[top], dib[bottom]) = (dib[bottom], dib[top]);
            }
        }
    }

    private static void SetInt(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
        bytes[at + 2] = (byte)(value >> 16);
        bytes[at + 3] = (byte)(value >> 24);
    }

    private static void SetShort(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
    }

    private static int ReadInt(byte[] bytes, int at)
        => bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24;

    private static int ReadShort(byte[] bytes, int at) => bytes[at] | bytes[at + 1] << 8;
}
