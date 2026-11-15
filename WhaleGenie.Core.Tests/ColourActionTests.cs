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
/// The colour family on its own: the pretend screen holds one red pixel, so reading it, matching it
/// and finding it can be checked without a desk.
/// </summary>
public class ColourActionTests
{
    [Fact]
    public async Task Reading_a_pixel_hands_the_colour_over_both_ways()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Pixel = new PixelColor(255, 0, 0);

        await Run(context, "vision.getPixel", Step("vision.getPixel",
            Fields.Text("x", "3"), Fields.Text("y", "4"),
            SampleFiles.Returns("color", "number", "screenPoint")));

        Assert.Equal("#FF0000", context.Written["color"].AsText());
        Assert.Equal(255, context.Written["number"].AsNumber());
        Assert.Equal(3, Field(context.Written["screenPoint"], FieldNames.X).AsNumber());
    }

    [Fact]
    public async Task Matching_a_colour_answers_whether_the_pixel_shows_it()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Pixel = new PixelColor(250, 0, 0);

        await Run(context, "vision.matchColor", Step("vision.matchColor",
            Fields.Text("color", "#FF0000"), Fields.Text("tolerance", "5"),
            SampleFiles.Returns("matches", "color", "delta")));

        Assert.True(context.Written["matches"].AsBool());
        Assert.Equal("#FA0000", context.Written["color"].AsText());
    }

    [Fact]
    public async Task Finding_a_colour_hands_over_where_the_pixel_is()
    {
        var context = new FakeRunContext();
        var screen = new ImageFrame(20, 10, new byte[20 * 10 * 4]);
        var offset = ((5 * 20) + 4) * 4;
        screen.Bgra[offset + 2] = 255;
        screen.Bgra[offset + 3] = 255;
        context.Machine.Screen.Display = screen;
        context.Machine.Screen.Desktop = new ScreenLayout(new ScreenArea(0, 0, 20, 10),
            [new ScreenDisplay(new ScreenArea(0, 0, 20, 10), true)]);

        await Run(context, "vision.findColor", Step("vision.findColor",
            Fields.Text("color", "#FF0000"), Fields.Text("tolerance", "0"),
            SampleFiles.Returns("found", "match", "every")));

        Assert.True(context.Written["found"].AsBool());
        Assert.Equal(4, Field(context.Written["match"], FieldNames.X).AsNumber());
        Assert.Equal(5, Field(context.Written["match"], FieldNames.Y).AsNumber());
        Assert.Single(context.Written["every"].Items);
    }

    [Fact]
    public async Task A_pattern_is_found_where_its_colours_sit()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Display = Picture(
            "#000000,#000000,#000000",
            "#000000,#FF0000,#00FF00",
            "#000000,#0000FF,#000000");
        context.Machine.Screen.Desktop = new ScreenLayout(new ScreenArea(0, 0, 3, 3),
            [new ScreenDisplay(new ScreenArea(0, 0, 3, 3), true)]);
        var points = Fields.Rows("points",
            Cells.Of(("dx", "1"), ("dy", "0"), ("colour", "#00FF00")),
            Cells.Of(("dx", "0"), ("dy", "1"), ("colour", "#0000FF")));

        await Run(context, "vision.findColors", Step("vision.findColors",
            Fields.Text("color", "#FF0000"), Fields.Text("tolerance", "0"), points,
            SampleFiles.Returns("found", "match")));

        Assert.True(context.Written["found"].AsBool());
        Assert.Equal(1, Field(context.Written["match"], FieldNames.X).AsNumber());
    }

    [Fact]
    public void The_pattern_pass_over_the_screen_finds_the_base_colour()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Display = Picture(
            "#000000,#000000,#000000",
            "#000000,#FF0000,#00FF00",
            "#000000,#0000FF,#000000");
        context.Machine.Screen.Desktop = new ScreenLayout(new ScreenArea(0, 0, 3, 3),
            [new ScreenDisplay(new ScreenArea(0, 0, 3, 3), true)]);
        var points = Fields.Rows("points",
            Cells.Of(("dx", "1"), ("dy", "0"), ("colour", "#00FF00")));
        var step = Step("vision.findColors", Fields.Text("color", "#FF0000"),
            Fields.Text("tolerance", "0"), points);

        var hits = ColourActions.PatternHits(context, step);

        Assert.Single(hits);
        Assert.Equal(1, hits[0].Location.X);
    }

    [Fact]
    public void The_pattern_reads_its_offsets_and_colours()
    {
        var context = new FakeRunContext();
        var points = Fields.Rows("points",
            Cells.Of(("dx", "1"), ("dy", "0"), ("colour", "#00FF00")),
            Cells.Of(("dx", "0"), ("dy", "1"), ("colour", "#0000FF")));
        var step = Step("vision.findColors", Fields.Text("color", "#FF0000"), points);

        var around = ColourActions.Pattern(context, step);

        Assert.Equal(2, around.Count);
        Assert.Equal((1, 0), (around[0].Dx, around[0].Dy));
        Assert.Equal(PixelColor.Parse("#00FF00"), around[0].Colour);
        Assert.Equal((0, 1), (around[1].Dx, around[1].Dy));
    }

    [Fact]
    public void The_pattern_search_finds_the_picture_the_test_draws()
    {
        var frame = Picture(
            "#000000,#000000,#000000",
            "#000000,#FF0000,#00FF00",
            "#000000,#0000FF,#000000");
        var around = new[] { new ColourStep(1, 0, PixelColor.Parse("#00FF00")) };

        var hits = PixelSearch.FindPattern(frame, PixelColor.Parse("#FF0000"), 0, around, 10);

        Assert.Single(hits);
    }

    private static ImageFrame Picture(params string[] rows)
    {
        var width = rows[0].Split(',').Length;
        var bytes = new byte[width * rows.Length * 4];
        for (var y = 0; y < rows.Length; y++)
        {
            var cells = rows[y].Split(',');
            for (var x = 0; x < width; x++)
            {
                var colour = PixelColor.Parse(cells[x]);
                var at = ((y * width) + x) * 4;
                bytes[at] = colour.B;
                bytes[at + 1] = colour.G;
                bytes[at + 2] = colour.R;
                bytes[at + 3] = 255;
            }
        }

        return new ImageFrame(width, rows.Length, bytes);
    }

    [Fact]
    public async Task A_pattern_with_no_points_is_refused_rather_than_matching_everywhere()
    {
        var context = new FakeRunContext();

        var failure = await Assert.ThrowsAsync<StepFailure>(() => Run(context, "vision.findColors",
            Step("vision.findColors", Fields.Text("color", "#FF0000"),
                SampleFiles.Returns("found"))));

        Assert.Equal(RunKeys.NoColourPoints, failure.Key);
    }

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => ColourActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
