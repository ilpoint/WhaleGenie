using System;
using System.Text;
using WhaleGenie.Mcp;

namespace WhaleGenie.Tests;

/// <summary>
/// The picture the range check leaves behind. It is written to a .png, so what is written has to be
/// a PNG: the protocol carries an image as base64 text and a client library hands that text back in
/// a byte array, which used to be saved as-is — a file that opens as a page of letters.
/// </summary>
public sealed class RangeCheckPictureTests
{
    /// <summary>The bytes of a one-pixel PNG, which is all a picture needs to be to be a picture.</summary>
    private static readonly byte[] Tiny =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    /// <summary>Bytes that are already a PNG are kept as they came.</summary>
    [Fact]
    public void Raw_png_bytes_are_kept()
        => Assert.Equal(Tiny, McpRangeCheck.Raw(Tiny));

    /// <summary>Bytes that are base64 text are decoded into the PNG they spell.</summary>
    [Fact]
    public void Base64_text_is_decoded()
    {
        var text = Encoding.UTF8.GetBytes(Convert.ToBase64String(Tiny));
        Assert.Equal(Tiny, McpRangeCheck.Raw(text));
    }

    /// <summary>Nothing in, nothing out: a capture that answered no picture is not a crash.</summary>
    [Fact]
    public void Nothing_stays_nothing()
        => Assert.Empty(McpRangeCheck.Raw([]));
}
