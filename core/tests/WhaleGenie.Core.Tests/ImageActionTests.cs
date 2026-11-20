using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The picture-search family on its own: the pretend vision answers with the places a reference
/// picture turns up, so finding, waiting for and clicking one can be checked without a screen.
/// </summary>
public class ImageActionTests
{
    [Fact]
    public async Task Finding_a_picture_hands_over_where_it_turned_up()
    {
        var context = new FakeRunContext();
        context.Machine.Vision.Match = new ImageMatch(0.95, new ScreenPoint(20, 10),
            new ScreenSize(4, 4));

        await Run(context, "vision.findImage", Step("vision.findImage",
            Pictures(), SampleFiles.Returns("found", "match", "every")));

        Assert.True(context.Written["found"].AsBool());
        var match = context.Written["match"];
        Assert.Equal(22, Field(match, FieldNames.X).AsNumber());
        Assert.Equal(12, Field(match, FieldNames.Y).AsNumber());
        Assert.Single(context.Written["every"].Items);
    }

    [Fact]
    public async Task Waiting_for_a_picture_keeps_looking_until_it_turns_up()
    {
        var context = new FakeRunContext();
        context.Machine.Vision.MatchAfter = 3;
        context.Machine.Vision.Match = new ImageMatch(0.9, new ScreenPoint(5, 6),
            new ScreenSize(2, 2));

        await Run(context, "vision.waitImage", Step("vision.waitImage",
            Pictures(), SampleFiles.Returns("match")));

        Assert.Equal(3, context.Machine.Vision.Searches);
    }

    [Fact]
    public async Task Clicking_a_picture_clicks_its_middle_moved_by_the_offset()
    {
        var context = new FakeRunContext();
        context.Machine.Vision.Match = new ImageMatch(0.9, new ScreenPoint(20, 10),
            new ScreenSize(4, 4));

        await Run(context, "vision.clickImage", Step("vision.clickImage",
            Pictures(), Fields.Text("offsetX", "1"), Fields.Text("offsetY", "2"),
            SampleFiles.Returns("match", "every")));

        Assert.Contains("click left 23 14 1 0", context.Machine.Desk.Calls);
    }

    private static ExecutableParameter Pictures()
        => Fields.Rows("images", Cells.Of(("image", "shot")));

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => ImageActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
