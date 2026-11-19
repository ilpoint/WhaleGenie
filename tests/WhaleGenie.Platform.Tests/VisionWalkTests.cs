using System;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Platform.Windows;

namespace WhaleGenie.Platform.Tests;

/// <summary>
/// 命中排序的走查在真的匹配器上：同一张图里三个一样的图标，四向各从自己的角取第一个，最近的点取
/// 离它最近的那个。替身设备排得再整齐也说明不了 OpenCV 那张分数图是怎么被读的，而“阅读顺序”能不能
/// 真的从左上角数起，正在这里。
/// </summary>
public class VisionWalkTests
{
    private static readonly PixelColor Blue = new(40, 90, 200);

    /// <summary>A pattern a shifted match cannot pretend to be.</summary>
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

    /// <summary>The icon of the picture: ten pixels square, drawn from the top left corner.</summary>
    private const int Icon = 10;

    private static readonly (int X, int Y)[] Spots = [(5, 5), (30, 5), (45, 25)];

    /// <summary>A blue screen with the same ten pixel icon at three places.</summary>
    private static (ImageFrame Screen, ImageFrame Picture) Three()
        => (Frame(60, 40, (x, y) =>
        {
            foreach (var spot in Spots)
            {
                if (x >= spot.X && x < spot.X + Icon && y >= spot.Y && y < spot.Y + Icon)
                {
                    return Pattern(x - spot.X, y - spot.Y);
                }
            }

            return Blue;
        }), Frame(Icon, Icon, Pattern));

    private static string Where(IReadOnlyList<ImageMatch> hits)
        => string.Join(", ", hits.Select(hit => $"{hit.Location.X},{hit.Location.Y}"));

    /// <summary>四向各从自己的角数起：同一个横排先数行，再数行内的左右。</summary>
    [Theory]
    [InlineData(SearchWalk.Reading, "5,5, 30,5, 45,25")]
    [InlineData(SearchWalk.ReadingBack, "30,5, 5,5, 45,25")]
    [InlineData(SearchWalk.ReadingUp, "45,25, 5,5, 30,5")]
    [InlineData(SearchWalk.ReadingBackUp, "45,25, 30,5, 5,5")]
    public void A_walk_counts_the_places_from_its_own_corner(SearchWalk walk, string expected)
    {
        var (screen, picture) = Three();
        var device = new OpenCvVisionDevice();

        var hits = device.FindAll(screen, picture,
            new VisionQuery(ConfidencePercent: 95, Limit: 3, Walk: walk));

        Assert.Equal(expected, Where(hits));
    }

    /// <summary>“离某个点最近”要看的是所有峰值，从最靠右下的那个图标说起也一样。</summary>
    [Fact]
    public void A_walk_to_the_nearest_point_takes_the_closest_place()
    {
        var (screen, picture) = Three();
        var device = new OpenCvVisionDevice();

        var hits = device.FindAll(screen, picture, new VisionQuery(ConfidencePercent: 95, Limit: 1,
            Walk: SearchWalk.Near, Near: new ScreenPoint(60, 40)));

        Assert.Equal("45,25", Where(hits));
    }

    /// <summary>
    /// 逐像素比的分数是“差多少”，越小越好：抹掉一处命中之后它必须读成最差，否则同一个地方会被当成
    /// 命中一次又一次，前两个命中就是同一处。
    /// </summary>
    [Fact]
    public void A_place_that_was_taken_is_not_found_again_by_a_difference_search()
    {
        var (screen, picture) = Three();
        var device = new OpenCvVisionDevice();

        var hits = device.FindAll(screen, picture, new VisionQuery(
            Algorithm: MatchAlgorithm.Difference, ConfidencePercent: 90, Limit: 2,
            Walk: SearchWalk.Score));

        Assert.Equal(2, hits.Count);
        Assert.Equal(2, hits.Select(hit => $"{hit.Location.X},{hit.Location.Y}").Distinct().Count());
    }
}
