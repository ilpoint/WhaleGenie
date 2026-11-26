using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The capture family on its own: the pretend screen holds a picture and answers where a rectangle
/// was cut from, so taking a picture and waiting for it to settle can be checked without a desk.
/// </summary>
public class CaptureActionTests
{
    [Fact]
    public async Task Capturing_a_rectangle_keeps_the_picture_and_hands_over_its_middle()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Display = Screen(100, 50);

        await Run(context, "vision.capture", Step("vision.capture",
            Fields.Text("x", "10"), Fields.Text("y", "20"),
            Fields.Text("width", "30"), Fields.Text("height", "40"),
            SampleFiles.Returns("image", "place", "screenPoint")));

        Assert.Equal(30, context.Images["image"].Width);
        Assert.Equal(40, context.Images["image"].Height);
        var place = context.Written["place"];
        Assert.Equal(25, Field(place, FieldNames.X).AsNumber());
        Assert.Equal(40, Field(place, FieldNames.Y).AsNumber());
    }

    [Fact]
    public async Task Capturing_a_window_takes_the_whole_of_it()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Display = Screen(400, 300);
        context.Machine.Window.Open.Add(new WindowInfo(7, "Document",
            new ScreenPoint(30, 40), new ScreenSize(200, 100), false, false));

        await Run(context, "vision.captureWindow", Step("vision.captureWindow",
            Fields.Text("window", "Document"), SampleFiles.Returns("image")));

        Assert.Contains("capture 30 40 200 100", context.Machine.Desk.Calls);
        Assert.Equal(200, context.Images["image"].Width);
        Assert.Equal(100, context.Images["image"].Height);
    }

    [Fact]
    public async Task Capturing_to_a_file_writes_the_picture_where_the_step_says()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Display = Screen(100, 50);

        await Run(context, "vision.capture", Step("vision.capture",
            Fields.Text("width", "30"), Fields.Text("height", "40"), Fields.Text("path", "shot.png"),
            SampleFiles.Returns("image", "path")));

        Assert.True(context.Machine.Files.Blobs.ContainsKey("shot.png"));
        Assert.EndsWith("shot.png", context.Written["path"].AsText());
    }

    [Fact]
    public async Task Waiting_for_the_screen_to_settle_stops_once_two_frames_match()
    {
        var context = new FakeRunContext();
        context.Machine.Screen.Display = Screen(100, 50);

        await Run(context, "vision.waitStable", Step("vision.waitStable",
            Fields.Text("quietMs", "0"), Fields.Text("tolerance", "0"),
            Fields.Text("changedPercent", "0"), SampleFiles.Returns("screenPoint")));

        Assert.True(context.Machine.Screen.Captures.Count >= 2);
        Assert.True(context.Written.ContainsKey("screenPoint"));
    }

    private static ImageFrame Screen(int width, int height)
        => new(width, height, new byte[width * height * 4]);

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => CaptureActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
