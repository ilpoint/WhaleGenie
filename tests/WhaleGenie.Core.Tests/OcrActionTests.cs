using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The text-recognition family on its own: the fake reader answers with the lines a test wrote down,
/// so reading, finding and clicking a line can be checked without a screen.
/// </summary>
public class OcrActionTests
{
    [Fact]
    public async Task Reading_hands_over_the_lines_it_read()
    {
        var context = new FakeRunContext();
        context.Machine.Ocr.Spans = [Line("总额", 10, 10), Line("42", 60, 10)];

        await Run(context, "ocr.recognize", Step("ocr.recognize",
            SampleFiles.Returns("text")));

        Assert.Equal("总额 42", context.Written["text"].AsText());
    }

    [Fact]
    public async Task Finding_a_line_hands_over_where_it_is_and_every_line_read()
    {
        var context = new FakeRunContext();
        context.Machine.Ocr.Spans = [Line("确定", 10, 20), Line("取消", 70, 20)];

        await Run(context, "ocr.findText", Step("ocr.findText",
            Fields.Text("queryText", "取消"),
            SampleFiles.Returns("found", "match", "every")));

        Assert.True(context.Written["found"].AsBool());
        Assert.Equal("取消", Field(context.Written["match"], FieldNames.Text).AsText());
        var every = context.Written["every"].Items;
        Assert.Equal("取消", Field(Assert.Single(every), FieldNames.Text).AsText());
    }

    [Fact]
    public async Task Clicking_a_line_clicks_where_it_was_read()
    {
        var context = new FakeRunContext();
        context.Machine.Ocr.Spans = [Line("下一頁", 10, 20)];

        await Run(context, "ocr.clickText", Step("ocr.clickText",
            Fields.Text("queryText", "下一頁"),
            SampleFiles.Returns("match", "every")));

        Assert.Contains("click left 25 25 1 0", context.Machine.Desk.Calls);
        Assert.Single(context.Searches);
    }

    private static TextSpan Line(string text, int x, int y)
        => new(text, new ScreenPoint(x, y), new ScreenSize(30, 10), 0.9);

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => OcrActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
