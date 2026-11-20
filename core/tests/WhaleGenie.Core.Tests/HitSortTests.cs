using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The order one look's hits come back in: the four directions, the weighted score, the distance to
/// a point, and the reading order that settles a tie. The hits are the places the engine worked
/// with already — screen pixels and the sizes the search reported — so nothing here needs a screen.
/// </summary>
public class HitSortTests
{
    /// <summary>Three hits, the surest one at the bottom of the area rather than at the top.</summary>
    private static List<ImageMatch> Three()
    {
        var size = new ScreenSize(4, 4);
        return
        [
            new ImageMatch(0.99, new ScreenPoint(10, 50), size),
            new ImageMatch(0.95, new ScreenPoint(80, 10), size),
            new ImageMatch(0.91, new ScreenPoint(30, 10), size),
        ];
    }

    private static string Order(List<ImageMatch> hits, MatchOrder kind,
        ScreenPoint near = default, int weight = HitOrder.UsualScoreWeight)
    {
        HitSort.Sort(hits, new HitOrder(kind, near, weight));
        return string.Join(", ", hits.Select(hit => $"{hit.Center.X},{hit.Center.Y}"));
    }

    /// <summary>
    /// 四向各从自己的角数起：同一个横排里，先数行、再数行内的左右，行从上往下或者从下往上。
    /// </summary>
    [Theory]
    [InlineData(MatchOrder.Reading, "32,12, 82,12, 12,52")]
    [InlineData(MatchOrder.ReadingBack, "82,12, 32,12, 12,52")]
    [InlineData(MatchOrder.ReadingUp, "12,52, 32,12, 82,12")]
    [InlineData(MatchOrder.ReadingBackUp, "12,52, 82,12, 32,12")]
    public void The_four_directions_count_from_their_own_corner(MatchOrder kind, string expected)
        => Assert.Equal(expected, Order(Three(), kind));

    /// <summary>
    /// 找图的命中框都是参考图的大小，所以综合评分在找图上就是按相似度：最像的在最前，不管它在哪儿。
    /// </summary>
    [Fact]
    public void A_picture_search_is_ranked_by_likeness_because_every_box_is_the_same_size()
        => Assert.Equal("12,52, 82,12, 32,12", Order(Three(), MatchOrder.Combined));

    /// <summary>
    /// 检测框大小不同，权重因此真的会改变答案：权重 100 只看分数，权重 0 只看框，默认各占一半时
    /// 大框靠“面积归一化到 1”吃掉一半分，于是分数低一点的大框反而在前。
    /// </summary>
    [Fact]
    public void The_weight_decides_whether_likeness_or_size_leads()
    {
        var found = new List<Sighting>
        {
            new(new ImageMatch(0.80, new ScreenPoint(10, 10), new ScreenSize(120, 40)), 0, ""),
            new(new ImageMatch(0.99, new ScreenPoint(50, 5), new ScreenSize(4, 4)), 0, ""),
        };

        Assert.Equal("52,7", First(found, 100));
        Assert.Equal("70,30", First(found, 0));
        Assert.Equal("70,30", First(found, HitOrder.UsualScoreWeight));

        static string First(List<Sighting> hits, int weight)
        {
            HitSort.Sort(hits, new HitOrder(MatchOrder.Combined, default, weight));
            return $"{hits[0].Match.Center.X},{hits[0].Match.Center.Y}";
        }
    }

    /// <summary>离指定点最近：按命中中心到点的距离由近到远，最近的在最前。</summary>
    [Fact]
    public void The_closest_place_to_a_point_comes_first()
        => Assert.Equal("32,12, 12,52, 82,12", Order(Three(), MatchOrder.Near, new ScreenPoint(0, 0)));

    /// <summary>
    /// 同距的两处按阅读顺序排，与它们被搜到的先后无关：同一屏每次数出来的一样。
    /// </summary>
    [Fact]
    public void Two_places_the_same_distance_apart_are_counted_in_reading_order()
    {
        // Both middles are forty pixels from (100, 100): one straight up, one straight across.
        var hits = new List<ImageMatch>
        {
            new(0.9, new ScreenPoint(58, 98), new ScreenSize(4, 4)),
            new(0.9, new ScreenPoint(98, 58), new ScreenSize(4, 4)),
        };

        Assert.Equal("100,60, 60,100", Order(hits, MatchOrder.Near, new ScreenPoint(100, 100)));
    }

    /// <summary>随机不保证顺序，但保证还是那几处，一个不多一个不少。</summary>
    [Fact]
    public void Shuffled_is_still_the_same_places()
    {
        var order = Order(Three(), MatchOrder.Random);
        Assert.Equal(["12,52", "32,12", "82,12"], order.Split(", ").Order(StringComparer.Ordinal));
    }

    /// <summary>读出来的文字也按同一套排，最宽的框在前就是“整行而不是一个词”。</summary>
    [Fact]
    public void What_was_read_is_ranked_the_same_way()
    {
        var spans = new List<TextSpan>
        {
            new("Ready", new ScreenPoint(10, 10), new ScreenSize(30, 8), 30),
            new("Ready to go", new ScreenPoint(100, 90), new ScreenSize(80, 8), 30),
        };

        HitSort.Sort(spans, new HitOrder(MatchOrder.Area, default, HitOrder.UsualScoreWeight));
        Assert.Equal("Ready to go", spans[0].Text);
    }
}
