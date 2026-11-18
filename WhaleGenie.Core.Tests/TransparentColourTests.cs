using System;
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
/// 透明色：写下来的那几个颜色不参与比对，四个角同色时那个颜色也自动算进去（大漠的规则），
/// 关掉这一档就只认手写的。看的窗口是搜索拿到的查询，因为掩码最后是设备照着它做的。
/// </summary>
public class TransparentColourTests
{
    private static readonly PixelColor White = new(0xFF, 0xFF, 0xFF);
    private static readonly PixelColor Green = new(0x00, 0xFF, 0x00);
    private static readonly PixelColor Inside = new(0x12, 0x34, 0x56);

    private static ExecutableStep Step(params ExecutableParameter[] parameters)
        => new() { Type = "vision.findImage", Parameters = parameters };

    /// <summary>The colours a step leaves out: a list of them, or the one colour it names.</summary>
    private static ExecutableParameter Colours(params string[] written)
        => Fields.Computed("transparentColors", written.Length == 1
            ? Formula.Text(written[0])
            : Formula.List([.. written.Select(colour => (ExpressionNode)Formula.Text(colour))]));

    private static ExecutableParameter Pictures(params string[] names)
        => Fields.Rows("images", [.. names.Select(name => Cells.Of(("image", name)))]);

    /// <summary>Paints one pixel of a picture, which is how a check makes two corners disagree.</summary>
    private static ImageFrame Painted(ImageFrame picture, int x, int y, PixelColor colour)
    {
        var at = ((y * picture.Width) + x) * 4;
        picture.Bgra[at] = colour.B;
        picture.Bgra[at + 1] = colour.G;
        picture.Bgra[at + 2] = colour.R;
        picture.Bgra[at + 3] = 0xFF;
        return picture;
    }

    private static Task<Signal> Run(FakeRunContext context, ExecutableStep step)
        => ImageActions.Map["vision.findImage"](context, step, 0, CancellationToken.None);

    /// <summary>A picture whose four corners are <paramref name="corner"/> and whose inside is not.</summary>
    private static ImageFrame Frame(PixelColor corner, PixelColor inside, int width = 8, int height = 8)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                var colour = edge ? corner : inside;
                var at = ((y * width) + x) * 4;
                bytes[at] = colour.B;
                bytes[at + 1] = colour.G;
                bytes[at + 2] = colour.R;
                bytes[at + 3] = 0xFF;
            }
        }

        return new ImageFrame(width, height, bytes);
    }

    private static FakeRunContext Points()
    {
        var context = new FakeRunContext();
        context.Machine.Vision.Matches.Add(new ImageMatch(0.95, new ScreenPoint(10, 10),
            new ScreenSize(4, 4)));
        return context;
    }

    /// <summary>
    /// 一格能收好几个颜色，分号隔开就行；四个角同色时那个颜色也在里面，手写的那个不写两遍。
    /// </summary>
    [Fact]
    public async Task Several_colours_are_left_out_and_none_is_written_twice()
    {
        var context = Points();
        context.Images["ok.png"] = Frame(White, Inside);

        await Run(context, Step(Pictures("ok.png"),
            Colours("#FFFFFF", "#00FF00"), SampleFiles.Returns("match")));

        var asked = Assert.Single(context.Machine.Vision.Queries);
        Assert.Equal([White, Green], asked.Transparent);
    }

    /// <summary>只写一个颜色也算数：一个颜色就是一个值，几个颜色才是一个列表。</summary>
    [Fact]
    public async Task One_colour_written_alone_is_a_list_of_one()
    {
        var context = Points();
        context.Images["ok.png"] = Painted(Frame(White, Inside), 7, 0, Green);

        await Run(context, Step(Pictures("ok.png"), Colours("#00FF00"),
            SampleFiles.Returns("match")));

        Assert.Equal([Green], Assert.Single(context.Machine.Vision.Queries).Transparent);
    }

    /// <summary>
    /// 变量里装着整个列表时照读：列表里的列表按颜色逐个读下去，所以底色可以由别的一步算出来。
    /// </summary>
    [Fact]
    public async Task A_variable_that_holds_a_list_of_colours_is_read_through()
    {
        var context = Points();
        context.Images["ok.png"] = Painted(Frame(White, Inside), 7, 0, Green);
        ((IRunContext)context).Variables.Set("bg",
            Value.FromList([Value.FromText("#00FF00"), Value.FromList([Value.FromText("#112233")])]));

        await Run(context, Step(Pictures("ok.png"),
            Fields.Computed("transparentColors", Formula.Name("bg")),
            SampleFiles.Returns("match")));

        var asked = Assert.Single(context.Machine.Vision.Queries);
        Assert.Equal([Green, new PixelColor(0x11, 0x22, 0x33)], asked.Transparent);
    }

    /// <summary>四角同色时那一个自动算进去，不必手写：换个底色也认得出同一个图标。</summary>
    [Fact]
    public async Task The_colour_the_four_corners_share_is_left_out_by_itself()
    {
        var context = Points();
        context.Images["ok.png"] = Frame(White, Inside);

        await Run(context, Step(Pictures("ok.png"), SampleFiles.Returns("match")));

        Assert.Equal([White], Assert.Single(context.Machine.Vision.Queries).Transparent);
    }

    /// <summary>四角颜色不一样的图，自动那一档就什么也不加：那不是底色。</summary>
    [Fact]
    public async Task A_picture_whose_corners_differ_has_nothing_found_by_itself()
    {
        var context = Points();
        context.Images["ok.png"] = Painted(Frame(White, Inside), 7, 0, Green);
        await Run(context, Step(Pictures("ok.png"), SampleFiles.Returns("match")));

        Assert.Null(Assert.Single(context.Machine.Vision.Queries).Transparent);
    }

    /// <summary>关掉「自动识别透明色」就只认手写的那些。</summary>
    [Fact]
    public async Task The_automatic_colour_can_be_turned_off()
    {
        var context = Points();
        context.Images["ok.png"] = Frame(White, Inside);

        await Run(context, Step(Pictures("ok.png"),
            Fields.Flag("autoTransparent", false), SampleFiles.Returns("match")));

        Assert.Null(Assert.Single(context.Machine.Vision.Queries).Transparent);
    }

    /// <summary>
    /// 整张图一个颜色时不自动掩掉：那一格颜色就是这张图的全部，掩掉它等于什么也不比。
    /// </summary>
    [Fact]
    public async Task A_picture_of_one_colour_keeps_it()
    {
        var context = Points();
        context.Images["ok.png"] = Frame(White, White);

        await Run(context, Step(Pictures("ok.png"), SampleFiles.Returns("match")));

        Assert.Null(Assert.Single(context.Machine.Vision.Queries).Transparent);
    }

    /// <summary>几张小图各带各的底色：透明色按图算，不是按这一步算。</summary>
    [Fact]
    public async Task Every_picture_brings_the_colours_its_own_corners_share()
    {
        var context = Points();
        context.Images["one.png"] = Frame(White, Inside);
        context.Images["two.png"] = Frame(Green, Inside);

        await Run(context, Step(Pictures("one.png", "two.png"),
            Fields.Text("imageSet", "every"), SampleFiles.Returns("match", "every")));

        Assert.Equal(2, context.Machine.Vision.Queries.Count);
        Assert.Equal([White], context.Machine.Vision.Queries[0].Transparent);
        Assert.Equal([Green], context.Machine.Vision.Queries[1].Transparent);
    }

    /// <summary>读不出来的颜色报的是那个词，而不是被当成黑色掩掉。</summary>
    [Fact]
    public async Task A_colour_nobody_can_read_stops_the_step()
    {
        var context = Points();

        var result = await Record.ExceptionAsync(() => Run(context, Step(Pictures("ok.png"),
            Colours("#FFFFFF", "zzz"), SampleFiles.Returns("match"))));

        var failure = Assert.IsType<StepFailure>(result);
        Assert.Equal(RunKeys.BadPoint, failure.Key);
        Assert.Equal("zzz", failure.Detail);
        Assert.Empty(context.Machine.Vision.Queries);
    }
}
