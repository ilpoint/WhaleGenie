using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

// Opening a browser, naming an element, and the dialogs and scripts around a page.
public partial class DeviceActionTests
{
    [Fact]
    public async Task A_browser_is_opened_driven_and_closed()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Page = "Hello from the page";
        var (result, _, store) = await RunAsync(
        [
            Step("browser.open", Param("browser", "edge"), Param("url", "https://example.com"),
                Param("headless", "true")),
            Step("browser.goTo", Param("url", "https://example.com/next")),
            Step("browser.click", Param("pageSelector", "#go")),
            Step("browser.fill", Param("pageSelector", "#q"), Param("fillText", "macro")),
            Step("browser.readText", Param("pageSelector", "#result"), Returns("text=answer")),
            Step("browser.close"),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello from the page", store.Local.Values["answer"].AsText());
        Assert.Contains("browserOpen edge https://example.com True (none)", devices.Calls);
        Assert.Contains("browserGoTo https://example.com/next 0", devices.Calls);
        Assert.Contains("browserClick #go #1 Single False 0", devices.Calls);
        Assert.Contains("browserFill #q #1 macro Fill 0 0", devices.Calls);
        Assert.Contains("browserText #result #1 Text (none) 0", devices.Calls);
        Assert.Contains("browserClose", devices.Calls);
        Assert.False(devices.Browser.IsOpen);
    }

    [Fact]
    public async Task A_browser_that_is_not_installed_says_what_to_install()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Installed = false;
        var (result, _, _) = await RunAsync(
            [Step("browser.open", Param("url", "https://example.com"))], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BrowserMissing", result.Key);
        Assert.DoesNotContain("browserOpen", devices.Calls);
    }

    [Fact]
    public async Task A_selector_that_was_picked_off_a_page_is_used_as_written()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.click", Param("pageSelector", "[data-testid=\"row-2\"]")),
            Step("browser.click", Param("pageSelector", "div > p:nth-of-type(2)")),
            Step("browser.click", Param("pageSelector", "input[name=\"user\"]")),
            Step("browser.click",
                Param("pageSelector", "frame=#frame >> #inner")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserClick [data-testid=\"row-2\"] #1 Single False 0", devices.Calls);
        Assert.Contains("browserClick div > p:nth-of-type(2) #1 Single False 0", devices.Calls);
        Assert.Contains("browserClick input[name=\"user\"] #1 Single False 0", devices.Calls);
        Assert.Contains("browserClick frame=#frame >> #inner #1 Single False 0", devices.Calls);
    }

    [Fact]
    public async Task A_second_way_of_naming_the_element_and_which_match_reach_the_device()
    {
        // A page that has been redesigned is met by writing a second selector under the first, and
        // which of the matches the step means is a field of its own: both have to reach the device
        // untouched and in the order they were written, which is the whole point of a fallback. A
        // blank line is not a selector, and the spaces around one are not part of it either.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Within(2500, Step("browser.click",
                Param("pageSelector", "[data-testid=\"save\"]\n#save-2\n\n  text=\"Save\"  "),
                Param("matchIndex", "2"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(
            "browserClick [data-testid=\"save\"] / #save-2 / text=\"Save\" #2 Single False 2500",
            devices.Calls);
    }

    [Fact]
    public async Task What_a_browser_step_narrows_the_page_down_by_reaches_the_device()
    {
        // A selector that names five elements which look alike is narrowed down by saying what the
        // right one is like, and every such condition has to reach the device in the order it was
        // written and with the match the step means: they are all put to the page together, so an
        // order or a word lost on the way would hand the step whatever the page listed first.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Within(2500, Step("browser.click", Param("pageSelector", "#ok"), Param("matchIndex", "2"),
                Rows("narrow", Narrow("HasText", "确定"), Narrow("Visible")))),
            Step("browser.click", Param("pageSelector", "#plain")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("browserClick #ok #2 [HasText=确定, Visible=] Single False 2500", devices.Calls);

        // A step that adds nothing says nothing to the device about narrowing, which is what keeps
        // what one of those looks like from changing how every other browser step is read.
        Assert.Contains("browserClick #plain #1 Single False 0", devices.Calls);
    }

    [Fact]
    public async Task A_browser_step_narrowed_by_something_unreadable_is_refused()
    {
        // The way of narrowing a row asks for is written in the file, so a word the engine does not
        // know is a step asking for less than it means. Acting on whichever element a selector
        // happened to name first would be the wrong element without saying so.
        var (result, devices, _) = await RunAsync(
        [
            Step("browser.click", Param("pageSelector", "#ok"), Rows("narrow", Narrow("Nonsense", "x"))),
        ]);

        Assert.Equal("Run.BrowserBadFilter", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("browserClick", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_run_says_which_way_of_naming_the_element_it_used()
    {
        // Which of the selectors a target was written with is the thing its author cannot see from
        // the file alone: a second line is written for the day the first stops working, and the run
        // has to say when that day has come.
        var devices = new FakeDeviceLayer();
        devices.Browser.ChosenIndex = 2;
        var host = new SilentRunHost();
        var result = await new MacroRunner(new VariableStore(), host, devices)
            .RunAsync([Step("browser.click", Param("pageSelector", "#first\n#second"))]);

        Assert.True(result.Succeeded);
        Assert.Equal("#second", host.Entries.Single(entry => entry.Key == "Log.BrowserClicked").Arguments[0]);
    }

    /// <summary>
    /// One element is read three ways, and which of them a step is after is its own answer: what a
    /// box shows and what it holds are two different things, and the markup behind it is a third.
    /// </summary>
    [Fact]
    public async Task A_page_action_reads_whichever_part_of_the_element_it_was_told_to()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Page = "1234";
        var (result, _, store) = await RunAsync(
        [
            Within(2500, Step("browser.readText", Param("pageSelector", "#card"),
                Param("readWhat", "value"), Returns("text=卡号"))),
            Within(2500, Step("browser.readText", Param("pageSelector", "#link"),
                Param("readWhat", "attribute"), Param("attribute", "href"),
                Returns("text=链接"))),
            Step("browser.readText", Param("pageSelector", "#plain"), Returns("text=文字")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserText #card #1 Value (none) 2500", devices.Calls);
        Assert.Contains("browserText #link #1 Attribute href 2500", devices.Calls);

        // A step that says nothing about which part reads the writing the element shows, which is
        // what every such step meant before there was a choice.
        Assert.Contains("browserText #plain #1 Text (none) 0", devices.Calls);
        Assert.Equal("1234", store.Local.Values["卡号"].AsText());
    }

    /// <summary>
    /// Every element a selector names, read into one list: the second row of a result has no name of
    /// its own to write down, so a macro that reads a page's list one step at a time cannot be
    /// written at all.
    /// </summary>
    [Fact]
    public async Task Every_element_a_selector_names_is_read_as_one_list()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Items = ["甲", "乙", "丙"];
        var (result, _, store) = await RunAsync(
        [
            Within(2500, Step("browser.readAll",
                Param("pageSelector", ".row\n\n  #row-2  "), Param("matchIndex", "2"),
                Param("maxItems", "3"), Returns("items=行"))),
        ], devices);

        Assert.True(result.Succeeded);

        // The ways of naming an element travel in the order they were written, and the two numbers
        // travel with them: which match to start at, and how many entries are wanted.
        Assert.Contains("browserReadAll .row / #row-2 #2 Text (none) 3 2500", devices.Calls);

        var items = store.Local.Values["行"];
        Assert.True(items.IsList);
        Assert.Equal(["甲", "乙", "丙"], items.Items.Select(item => item.AsText()));
    }

    /// <summary>
    /// A page's table is read into the shape every other table in the program has: one entry per
    /// row, a field per cell, keyed by the names the page writes above its columns.
    /// </summary>
    [Fact]
    public async Task The_rows_of_a_page_table_are_keyed_by_the_names_above_its_columns()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Table = new(["金额", "日期"], [["100", "3 日"], ["200", "4 日"]]);

        var (result, _, store) = await RunAsync(
        [
            Step("browser.readTable", Param("pageSelector", "#report"), Param("matchIndex", "2"),
                Param("maxRows", "2"), Returns("table=表")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserReadTable #report #2 2 0", devices.Calls);

        var table = store.Local.Values["表"];
        Assert.Equal(2, table.Items.Count);
        Assert.Equal("100", table.Items[0].Field("金额")!.AsText());
        Assert.Equal("4 日", table.Items[1].Field("日期")!.AsText());
    }

    /// <summary>
    /// A table of a page that writes no strip of column names still has columns, and they are named
    /// the way the window reader names the columns of a grid whose names nobody kept.
    /// </summary>
    [Fact]
    public async Task A_page_table_that_writes_no_column_names_is_read_by_letter()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Table = new([], [["Ann", "31"]]);
        var (result, _, store) = await RunAsync(
        [
            Step("browser.readTable", Param("matchIndex", "2"), Returns("table=表")),
        ], devices);

        Assert.True(result.Succeeded);

        // A step that names no table reads the page's own table element, and the match number picks
        // which of those tables it means. The most rows it reads is the default every list reader
        // in the program uses.
        Assert.Contains("browserReadTable table #2 100 0", devices.Calls);
        Assert.Equal(["A", "B"], store.Local.Values["表"].Items[0].Fields.Select(field => field.Name));
    }

    /// <summary>
    /// A dialog is answered around the steps that raise it, because a page stops where it stands
    /// until somebody answers: the click that opened the box does not come back until it is answered,
    /// so clicking in one step and answering in the next cannot be written at all.
    /// </summary>
    [Fact]
    public async Task A_dialog_is_answered_around_the_steps_that_raise_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Dialog = "确定要提交吗？";
        var (result, _, store) = await RunAsync(
        [
            Step("browser.dialog", Param("dialogHow", "accept"), Param("dialogText", "报告.csv"),
                Body("body", Step("browser.click", Param("pageSelector", "#submit"))),
                Returns("message=回答")),
        ], devices);

        Assert.True(result.Succeeded);

        // The answer is armed before the body runs, and what the box said is handed over after it.
        Assert.Equal(
            ["browserArmDialog True 报告.csv", "browserClick #submit #1 Single False 0"],
            devices.Calls);
        Assert.Equal("确定要提交吗？", store.Local.Values["回答"].AsText());
    }

    /// <summary>
    /// A box whose steps never raised a dialog is a step that did not do what it says: a macro that
    /// means to answer something and never gets asked is worth hearing about.
    /// </summary>
    [Fact]
    public async Task A_dialog_that_never_came_up_is_a_step_that_did_not_work()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Dialog = null;
        var (result, _, _) = await RunAsync(
        [
            Step("browser.dialog",
                Body("body", Step("browser.click", Param("pageSelector", "#submit")))),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BrowserNoDialog", result.Key);
    }

    /// <summary>
    /// A synchronous call is the caller's own flow, so its browser steps are the dialog's own steps
    /// and may use the page it is holding.
    /// </summary>
    [Fact]
    public async Task A_synchronous_browser_call_works_inside_a_dialog()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Dialog = "继续吗？";
        var library = new Macros(("用页面", [Step("browser.click", Param("pageSelector", "#submit"))]));

        var (result, _, _) = await RunWithAsync(
        [
            Step("browser.dialog", Param("dialogHow", "accept"),
                Body("body", Step("control.runMacro", Param("macro", "用页面")))),
        ], devices, library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("browserClick #submit #1 Single False 0", devices.Calls);
    }

    /// <summary>
    /// A script of the step's own is the way round whatever the catalogue does not cover, and what it
    /// returns is what the step hands over.
    /// </summary>
    [Fact]
    public async Task A_script_of_the_steps_own_runs_in_the_page_and_hands_back_what_it_returned()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Evaluated = """{"rows":3}""";
        var (result, _, store) = await RunAsync(
        [
            Within(2500, Step("browser.evaluate",
                Param("script", "() => { return document.querySelectorAll('a').length; }"),
                Returns("result=数量"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(
            "browserEvaluate () => { return document.querySelectorAll('a').length; } 2500",
            devices.Calls);
        Assert.Equal("""{"rows":3}""", store.Local.Values["数量"].AsText());
    }

    /// <summary>
    /// A step with no script at all is a mistake in the step rather than a page that did nothing.
    /// </summary>
    [Fact]
    public async Task A_page_script_with_nothing_written_is_refused()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.evaluate", Param("script", "   ")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BrowserNoScript", result.Key);
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("browserEvaluate", StringComparison.Ordinal));
    }
}
