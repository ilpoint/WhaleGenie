using System;
using Viktor.Core.Devices;

namespace Viktor.Core.Tests;

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
}
