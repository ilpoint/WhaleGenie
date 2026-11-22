using System;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>The debugger look is a thumbnail, not a native-resolution screenshot.</summary>
public class RunLookTests
{
    [Fact]
    public void A_large_frame_is_scaled_to_the_longest_side()
    {
        var frame = new ImageFrame(1000, 500, new byte[1000 * 500 * 4]);

        var bytes = Decode(RunBridge.ToDataUrl(frame));

        Assert.Equal(320, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(160, BitConverter.ToInt32(bytes, 22));
        Assert.Equal(54 + 320 * 160 * 4, bytes.Length);
    }

    [Fact]
    public void A_small_frame_keeps_its_size()
    {
        var frame = new ImageFrame(100, 50, new byte[100 * 50 * 4]);

        var bytes = Decode(RunBridge.ToDataUrl(frame));

        Assert.Equal(100, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(50, BitConverter.ToInt32(bytes, 22));
    }

    private static byte[] Decode(string dataUrl)
    {
        const string prefix = "data:image/bmp;base64,";
        Assert.StartsWith(prefix, dataUrl, StringComparison.Ordinal);
        return Convert.FromBase64String(dataUrl[prefix.Length..]);
    }
}
