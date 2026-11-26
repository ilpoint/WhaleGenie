using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How a step's fields become one order: the word it picked, the point a distance is measured from,
/// and how much of the weighted score is likeness. It is read once per look, which is what lets a
/// variable stand for the point and be read again by every look of a wait.
/// </summary>
public class HitOrderTests
{
    private static readonly VariableStore Variables = new();

    /// <summary>Two regions a step can search: one at the screen's corner, one further along it.</summary>
    private static List<(ImageFrame Frame, ScreenPoint Origin)> Areas()
        =>
        [
            (new ImageFrame(100, 50, new byte[100 * 50 * 4]), new ScreenPoint(0, 0)),
            (new ImageFrame(100, 50, new byte[100 * 50 * 4]), new ScreenPoint(200, 100)),
        ];

    private static HitOrder Read(ExecutableStep step, ScreenPoint? anchor = null)
        => HitOrder.Of(new StepArguments(step, Variables, 16, EvaluationContext.System), () => anchor, Areas());

    private static ExecutableStep Step(params ExecutableParameter[] parameters)
        => new() { Type = "vision.findImage", Parameters = parameters };

    private static ExecutableParameter Order(string written) => Fields.Text("orderBy", written);

    /// <summary>每个词对应哪一档排序，以及找图据此怎么走查。</summary>
    [Theory]
    [InlineData("reading", MatchOrder.Reading, SearchWalk.Reading)]
    [InlineData("readingBack", MatchOrder.ReadingBack, SearchWalk.ReadingBack)]
    [InlineData("readingUp", MatchOrder.ReadingUp, SearchWalk.ReadingUp)]
    [InlineData("readingBackUp", MatchOrder.ReadingBackUp, SearchWalk.ReadingBackUp)]
    [InlineData("combined", MatchOrder.Combined, SearchWalk.Score)]
    [InlineData("near", MatchOrder.Near, SearchWalk.Near)]
    [InlineData("centre", MatchOrder.Near, SearchWalk.Near)]
    [InlineData("random", MatchOrder.Random, SearchWalk.Score)]
    public void A_word_is_one_order_and_one_walk(string written, MatchOrder kind, SearchWalk walk)
    {
        var order = Read(Step(Order(written)));
        Assert.Equal(kind, order.Kind);
        Assert.Equal(walk, order.Walk);
    }

    /// <summary>
    /// 词表以外的词按阅读顺序读：哪些词合法由目录说了算，动作族再拒一次只是同一个答案说两遍。
    /// </summary>
    [Fact]
    public void A_word_the_table_does_not_know_is_reading_order()
        => Assert.Equal(MatchOrder.Reading, Read(Step(Order("whatever"))).Kind);

    /// <summary>
    /// 「离指定点最近」的两格与搜索区域同一基准：屏幕档是屏幕像素，窗口档是窗口客户区里的像素，
    /// 所以同一个 5,6 在两种基准下落在两个地方。
    /// </summary>
    [Fact]
    public void A_point_is_counted_from_the_corner_the_step_works_from()
    {
        var step = Step(Order("near"), Fields.Text("nearX", "5"), Fields.Text("nearY", "6"));

        Assert.Equal(new ScreenPoint(100 + 5, 200 + 6), Read(step, new ScreenPoint(100, 200)).Near);
        Assert.Equal(new ScreenPoint(5, 6), Read(step).Near);
    }

    /// <summary>两格都留空就是基准的左上角，不是别处。</summary>
    [Fact]
    public void A_point_left_empty_is_the_corner_of_the_basis()
    {
        var step = Step(Order("near"));
        Assert.Equal(new ScreenPoint(0, 0), Read(step).Near);
    }

    /// <summary>
    /// 「离画面中心最近」的点是所有搜索区域合起来那个矩形的中心，和“试看”把多块区域摆成一张图时
    /// 用的中心是同一个。
    /// </summary>
    [Fact]
    public void The_middle_is_the_one_of_everything_that_was_searched()
    {
        // The two areas above reach from 0,0 to 300,150.
        Assert.Equal(new ScreenPoint(150, 75), Read(Step(Order("centre"))).Near);
    }

    /// <summary>
    /// 权重没人写时是各占一半；写了 0 就是只看大小，这不是“没写”。
    /// </summary>
    [Fact]
    public void The_weight_is_half_unless_the_step_says_otherwise()
    {
        Assert.Equal(HitOrder.UsualScoreWeight, Read(Step(Order("combined"))).ScoreWeight);
        Assert.Equal(0, Read(Step(Order("combined"), Fields.Text("scoreWeight", "0"))).ScoreWeight);
        Assert.Equal(100, Read(Step(Order("combined"), Fields.Text("scoreWeight", "100"))).ScoreWeight);
    }

    /// <summary>
    /// 只有检测族提供权重格，可一个手写的数仍不能把排序算出量程：0 到 100 之外按两端的值算。
    /// </summary>
    [Fact]
    public void A_weight_outside_the_range_is_read_from_the_nearest_end()
    {
        Assert.Equal(100, Read(Step(Order("combined"), Fields.Text("scoreWeight", "250"))).ScoreWeight);
        Assert.Equal(0, Read(Step(Order("combined"), Fields.Text("scoreWeight", "-40"))).ScoreWeight);
    }
}
