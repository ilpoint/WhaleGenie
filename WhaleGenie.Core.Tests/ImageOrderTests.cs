using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// 找图的命中排序从字段一路走到搜索里：哪一档决定设备怎么走查、一次要几个，回来的是哪一个。
/// 这几条钉住的正是“排序”曾经名不副实的地方——先只取最像的几个，再按位置排。
/// </summary>
public class ImageOrderTests
{
    private static ExecutableStep Step(params ExecutableParameter[] parameters)
        => new() { Type = "vision.findImage", Parameters = parameters };

    private static ExecutableParameter Pictures()
        => Fields.Rows("images", Cells.Of(("image", "shot")));

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => ImageActions.Map[type](context, step, 0, CancellationToken.None);

    private static string Centre(FakeRunContext context)
    {
        var record = context.Written["match"];
        return $"{Side(record, FieldNames.X)},{Side(record, FieldNames.Y)}";
    }

    private static int Side(Value record, string name)
        => (int)record.Fields.First(field => field.Name == name).Value.AsNumber();

    private static async Task<FakeRunContext> Find(ExecutableStep step, params ImageMatch[] hits)
    {
        var context = new FakeRunContext();
        context.Machine.Vision.Matches.AddRange(hits);
        await Run(context, "vision.findImage", step);
        return context;
    }

    /// <summary>
    /// 默认档按阅读顺序数：最像的那一处排在右下，第 1 个命中仍旧是左上角那一个。搜索本身被要求
    /// 从左到右、从上往下走，只要一个。
    /// </summary>
    [Fact]
    public async Task The_first_hit_is_the_top_left_one_rather_than_the_surest()
    {
        var context = await Find(
            Step(Pictures(), SampleFiles.Returns("match")),
            new ImageMatch(0.95, new ScreenPoint(40, 40), new ScreenSize(4, 4)),
            new ImageMatch(0.93, new ScreenPoint(10, 10), new ScreenSize(4, 4)));

        Assert.Equal("12,12", Centre(context));
        var asked = Assert.Single(context.Machine.Vision.Queries);
        Assert.Equal(SearchWalk.Reading, asked.Walk);
        Assert.Equal(1, asked.Limit);
    }

    /// <summary>
    /// 「第 2 个命中」沿同一个方向数：搜索只往回两个，而不是把最像的两个拿来再排。
    /// </summary>
    [Fact]
    public async Task The_second_hit_is_counted_along_the_walk()
    {
        var context = await Find(
            Step(Pictures(), Fields.Text("matchIndex", "2"), SampleFiles.Returns("match")),
            new ImageMatch(0.99, new ScreenPoint(10, 10), new ScreenSize(4, 4)),
            new ImageMatch(0.90, new ScreenPoint(80, 40), new ScreenSize(4, 4)),
            new ImageMatch(0.95, new ScreenPoint(40, 40), new ScreenSize(4, 4)));

        Assert.Equal("42,42", Centre(context));
        Assert.Equal(2, Assert.Single(context.Machine.Vision.Queries).Limit);
    }

    /// <summary>
    /// 离指定点最近：指定点换成区域自己的坐标交给搜索，命中回来的是屏幕坐标；窗口档下同一个点
    /// 随窗口的角落走。
    /// </summary>
    [Fact]
    public async Task The_nearest_point_reaches_the_search_in_the_areas_own_pixels()
    {
        var context = new FakeRunContext
        {
            AreaOrigin = new ScreenPoint(200, 100),
            AnchorCorner = new ScreenPoint(200, 100),
        };

        context.Machine.Vision.Matches.Add(new ImageMatch(0.90, new ScreenPoint(60, 30),
            new ScreenSize(4, 4)));
        context.Machine.Vision.Matches.Add(new ImageMatch(0.90, new ScreenPoint(5, 5),
            new ScreenSize(4, 4)));

        await Run(context, "vision.findImage",
            Step(Pictures(), Fields.Text("orderBy", "near"), Fields.Text("nearX", "10"),
                Fields.Text("nearY", "10"), SampleFiles.Returns("match")));

        var asked = Assert.Single(context.Machine.Vision.Queries);
        Assert.Equal(SearchWalk.Near, asked.Walk);
        Assert.Equal(new ScreenPoint(10, 10), asked.Near);
        Assert.Equal("207,107", Centre(context));
    }

    /// <summary>勾了「全部命中」就整份都取，回来的一列也按同一个方向排。</summary>
    [Fact]
    public async Task The_whole_list_is_collected_and_handed_over_in_the_counted_order()
    {
        var context = await Find(
            Step(Pictures(), SampleFiles.Returns("match", "every")),
            new ImageMatch(0.99, new ScreenPoint(10, 30), new ScreenSize(4, 4)),
            new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(4, 4)));

        Assert.Equal(200, Assert.Single(context.Machine.Vision.Queries).Limit);
        Assert.Equal("12,12", Centre(context));

        var every = context.Written["every"].Items;
        Assert.Equal(["12,12", "12,32"], every.Select(item =>
            $"{Side(item, FieldNames.X)},{Side(item, FieldNames.Y)}"));
    }
}
