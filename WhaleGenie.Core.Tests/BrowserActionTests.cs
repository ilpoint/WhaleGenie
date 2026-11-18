using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The browser family on its own: the fake page answers what it is asked and writes down what it was
/// told, so a step can be checked without a browser being started.
/// </summary>
public class BrowserActionTests
{
    [Fact]
    public async Task Opening_a_page_hands_the_engine_and_the_address_to_the_browser()
    {
        var context = new FakeRunContext();

        await Run(context, "browser.open", Step("browser.open",
            Fields.Text("browser", "chrome"),
            Fields.Text("url", "https://example.com")));

        Assert.Contains("browserOpen chrome https://example.com False (none)",
            context.Machine.Desk.Calls);
    }

    [Fact]
    public async Task A_click_names_the_element_and_the_way_it_is_pressed()
    {
        var context = new FakeRunContext();

        await Run(context, "browser.click", Step("browser.click",
            Fields.Text("pageSelector", "button#save"),
            Fields.Text("clickHow", "double")));

        var call = context.Machine.Desk.Calls.Single(item => item.StartsWith("browserClick",
            System.StringComparison.Ordinal));
        Assert.Contains("Double", call);
        Assert.Contains("save", call);
    }

    [Fact]
    public async Task A_page_table_is_handed_over_as_records_named_by_its_columns()
    {
        var context = new FakeRunContext();
        context.Machine.Browser.Table = new PageTable(["金额", "日期"],
            [["100", "1月"], ["200", "2月"]]);

        await Run(context, "browser.readTable", Step("browser.readTable",
            SampleFiles.Returns("table")));

        var rows = context.Written["table"].Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal("100", Field(rows[0], "金额").AsText());
        Assert.Equal("2月", Field(rows[1], "日期").AsText());
    }

    [Fact]
    public async Task A_dialog_answers_the_box_and_runs_the_steps_that_raise_it()
    {
        var context = new FakeRunContext();

        await Run(context, "browser.dialog", Step("browser.dialog",
            Fields.Text("dialogHow", "accept"),
            Fields.Steps("body"),
            SampleFiles.Returns("message")));

        Assert.Single(context.Ran);
        Assert.Equal("确定要提交吗？", context.Written["message"].AsText());
        Assert.Contains(context.Machine.Desk.Calls,
            call => call.StartsWith("browserArmDialog True", System.StringComparison.Ordinal));
    }

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => BrowserActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
