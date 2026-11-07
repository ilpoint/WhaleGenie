using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// 参考图里那块纯色底不参与比对，是「带底色的小图标换个底色也要找得到」的全部依据。这件事只有
/// 真的走一遍 OpenCV 才算数：替身设备只记得住请求，说不出像素有没有被掩掉。
/// </summary>
public class TransparentPictureTests
{
    private static readonly PixelColor White = new(255, 255, 255);
    private static readonly PixelColor Blue = new(40, 90, 200);

    /// <summary>A pattern that is not a flat colour, so a mean-brightness difference cannot hide a real one.</summary>
    private static PixelColor Pattern(int x, int y)
        => new((byte)(x * 37 % 256), (byte)(y * 53 % 256), (byte)((x + y) * 29 % 256));

    private static ImageFrame Frame(int width, int height, Func<int, int, PixelColor> at)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var colour = at(x, y);
                var offset = ((y * width) + x) * 4;
                bytes[offset] = colour.B;
                bytes[offset + 1] = colour.G;
                bytes[offset + 2] = colour.R;
                bytes[offset + 3] = 255;
            }
        }

        return new ImageFrame(width, height, bytes);
    }

    /// <summary>The icon sits at 12,4 on a blue screen; the same icon is cropped with a white border round it.</summary>
    private static (ImageFrame Screen, ImageFrame Icon) Pair()
    {
        var screen = Frame(30, 14, (x, y) =>
            x >= 12 && x < 18 && y >= 4 && y < 10 ? Pattern(x - 12, y - 4) : Blue);

        var icon = Frame(10, 10, (x, y) =>
            x >= 2 && x < 8 && y >= 2 && y < 8 ? Pattern(x - 2, y - 2) : White);

        return (screen, icon);
    }

    /// <summary>
    /// 逐像素比对时白边与蓝底差得很远，所以不带透明色找不到、带上就找得到，位置仍是图标原来那一处。
    /// 这条同时说明掩码真的参与了比对，而不只是被记进了请求里。
    /// </summary>
    [Fact]
    public void A_flat_background_left_out_is_found_over_another_colour()
    {
        var (screen, icon) = Pair();
        var device = new OpenCvVisionDevice();

        var plain = device.FindAll(screen, icon, new VisionQuery(
            Algorithm: MatchAlgorithm.Difference, ConfidencePercent: 90));

        var masked = device.FindAll(screen, icon, new VisionQuery(
            Algorithm: MatchAlgorithm.Difference, ConfidencePercent: 90, Transparent: White));

        Assert.Empty(plain);
        var hit = Assert.Single(masked);
        Assert.Equal(10, hit.Location.X);
        Assert.Equal(2, hit.Location.Y);
    }

    /// <summary>
    /// 默认算法也要能吃掩码：这一步是绝大多数宏的写法，掩码在它下面被悄悄忽略的话，
    /// 上面那条用例照样绿，用户却什么也没得到。
    /// </summary>
    [Fact]
    public void The_default_algorithm_takes_the_transparent_colour_as_well()
    {
        var (screen, icon) = Pair();
        var device = new OpenCvVisionDevice();

        var masked = device.FindAll(screen, icon, new VisionQuery(
            ConfidencePercent: 90, Transparent: White));

        var hit = Assert.Single(masked);
        Assert.Equal(10, hit.Location.X);
        Assert.Equal(2, hit.Location.Y);
    }

    /// <summary>A colour the reference picture does not hold leaves the search as it was.</summary>
    [Fact]
    public void A_transparent_colour_that_is_not_in_the_picture_changes_nothing()
    {
        var (screen, icon) = Pair();
        var device = new OpenCvVisionDevice();
        var absent = new PixelColor(1, 2, 3);

        var plain = device.FindAll(screen, icon, new VisionQuery(
            Algorithm: MatchAlgorithm.Difference, ConfidencePercent: 90));

        var masked = device.FindAll(screen, icon, new VisionQuery(
            Algorithm: MatchAlgorithm.Difference, ConfidencePercent: 90, Transparent: absent));

        Assert.Equal(plain.Count, masked.Count);
    }
}
