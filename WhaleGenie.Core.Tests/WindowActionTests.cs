using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The window family on its own: the fake desktop says which windows are open and records what it
/// was told, so a step can be checked without a real window being moved.
/// </summary>
public class WindowActionTests
{
    [Fact]
    public async Task Existence_answers_whether_a_window_is_open()
    {
        var context = new FakeRunContext();

        await Run(context, "window.exists", Step("window.exists",
            SampleFiles.Returns("found")));
        Assert.False(context.Written["found"].AsBool());

        context.Machine.Window.Open.Add(Open("Document", 10, 20, 300, 200));
        await Run(context, "window.exists", Step("window.exists",
            SampleFiles.Returns("found")));

        Assert.True(context.Written["found"].AsBool());
    }

    [Fact]
    public async Task A_listing_hands_over_each_window_as_a_record()
    {
        var context = new FakeRunContext();
        context.Machine.Window.Open.Add(Open("Document", 10, 20, 300, 200));
        context.Machine.Window.Open.Add(Open("Mail", 40, 60, 500, 400));

        await Run(context, "window.list", Step("window.list",
            Fields.Text("details", "true"),
            SampleFiles.Returns("windows")));

        var windows = context.Written["windows"].Items;
        Assert.Equal(2, windows.Count);
        Assert.Equal("Document", Field(windows[0], FieldNames.Title).AsText());
        Assert.Equal("10,20,300,200", Field(windows[0], FieldNames.Box).AsText());
        Assert.Equal("40,60,500,400", Field(windows[1], FieldNames.Box).AsText());
    }

    [Fact]
    public async Task Reading_a_window_hands_over_its_box_and_the_part_inside_the_border()
    {
        var context = new FakeRunContext();
        context.Machine.Window.Open.Add(Open("Document", 10, 20, 300, 200));
        context.Machine.Window.ClientOrigins[1] = new ScreenPoint(18, 50);
        context.Machine.Window.ClientSizes[1] = new ScreenSize(280, 140);

        await Run(context, "window.info", Step("window.info",
            SampleFiles.Returns("window")));

        var record = context.Written["window"];
        Assert.Equal("10,20,300,200", Field(record, FieldNames.Box).AsText());
        Assert.Equal("18,50,280,140", Field(record, FieldNames.ClientBox).AsText());
        Assert.Equal(160, Field(record, FieldNames.X).AsNumber());
    }

    [Fact]
    public async Task Moving_a_window_sends_the_sides_the_step_wrote()
    {
        var context = new FakeRunContext();
        context.Machine.Window.Open.Add(Open("Document", 10, 20, 300, 200));

        await Run(context, "window.move", Step("window.move",
            Fields.Text("x", "5"),
            Fields.Text("y", "6"),
            Fields.Text(FieldNames.Width, "700"),
            Fields.Text(FieldNames.Height, "500")));

        Assert.Contains("moveWindow 1 5 6 700 500", context.Machine.Desk.Calls);
    }

    private static WindowInfo Open(string title, int x, int y, int width, int height)
        => new(1, title, new ScreenPoint(x, y), new ScreenSize(width, height), false, false);

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => WindowActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
