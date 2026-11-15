using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Platform.Windows;

namespace WhaleGenie.Core.Tests;

// Working a page: clicking, waiting, tabs, form controls and uploads.
public partial class DeviceActionTests
{
    /// <summary>
    /// How a step clicks and how it fills an element in are its own answers: a page's own menu opens
    /// on the right button, and a page that watches the typing itself has to be typed at rather than
    /// written into.
    /// </summary>
    [Fact]
    public async Task How_a_click_is_made_and_how_writing_goes_in_travel_with_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Within(2500, Step("browser.click", Param("pageSelector", "#menu"), Param("clickHow", "right"),
                Param("force", "true"))),
            Step("browser.click", Param("pageSelector", "#row"), Param("clickHow", "double")),
            Step("browser.click", Param("pageSelector", "#go")),
            Within(2500, Step("browser.fill", Param("pageSelector", "#search"),
                Param("fillText", "报告"), Param("fillHow", "type"), Param("keyIntervalMs", "80"))),
            Step("browser.fill", Param("pageSelector", "#name"), Param("fillText", "甲")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserClick #menu #1 Right True 2500", devices.Calls);
        Assert.Contains("browserClick #row #1 Double False 0", devices.Calls);

        // A step that says nothing about how it clicks, and one that says nothing about how it fills,
        // get what every such step meant before there was a choice: one click, writing at once.
        Assert.Contains("browserClick #go #1 Single False 0", devices.Calls);
        Assert.Contains("browserFill #search #1 报告 Type 80 2500", devices.Calls);
        Assert.Contains("browserFill #name #1 甲 Fill 0 0", devices.Calls);
    }

    /// <summary>
    /// Scrolling is what a page that fetches more as it is read needs, and the four ways of moving it
    /// are four answers of one question: what is not on screen yet has to be brought there.
    /// </summary>
    [Fact]
    public async Task The_page_is_moved_the_way_the_step_says()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Within(2500, Step("browser.scroll", Param("scrollHow", "element"),
                Param("pageSelector", "#row-9"), Param("matchIndex", "2"))),
            Step("browser.scroll", Param("scrollHow", "bottom")),
            Step("browser.scroll", Param("scrollHow", "top")),
            Step("browser.scroll"),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserScroll #row-9 #2 Element 2500", devices.Calls);
        Assert.Contains("browserScroll (none) Bottom 0", devices.Calls);
        Assert.Contains("browserScroll (none) Top 0", devices.Calls);

        // A step that says nothing moves the page by one screen, which is how a page that adds more
        // as it is read is walked.
        Assert.Contains("browserScroll (none) Screen 0", devices.Calls);
    }

    /// <summary>
    /// A file the page hands over is saved where the step says, and the path a step left out is the
    /// folder the macro file is in, the same as every other path a step is given.
    /// </summary>
    [Fact]
    public async Task A_file_the_page_hands_over_is_saved_where_the_step_says()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.Downloaded = new(@"C:\报告\订单.csv", "订单.csv");

        var (result, _, store) = await RunAsync(
        [
            Within(2500, Step("browser.download", Param("pageSelector", "#export"),
                Param("folder", "报告"), Returns("path=存到", "name=名字"))),
            Step("browser.download", Returns("path=默认存到")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains($@"browserDownload #export #1 {devices.Files.BaseFolder}\报告 2500", devices.Calls);

        // Nothing written in the folder field saves beside the macro, which is where a path written
        // on its own is read from.
        Assert.Contains($"browserDownload (none) {devices.Files.BaseFolder} 0", devices.Calls);
        Assert.Equal(@"C:\报告\订单.csv", store.Local.Values["存到"].AsText());
        Assert.Equal("订单.csv", store.Local.Values["名字"].AsText());
    }

    /// <summary>
    /// A condition about an element of the page is a look rather than a wait: what a macro asks
    /// before acting on something is whether it is there now, and waiting is a step, a block or a
    /// state of its own.
    /// </summary>
    /// <summary>
    /// Waiting for an address rather than for an element: the element a macro is after usually lives
    /// on the page a click leads to, and naming it too early finds the one being left behind.
    /// </summary>
    [Fact]
    public async Task A_wait_can_be_for_an_address_rather_than_for_an_element()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.WaitingAddress = "https://example.com/receipt/42";

        var host = new SilentRunHost();
        var result = await new MacroRunner(new VariableStore(), host, devices)
            .RunAsync(
            [
                Within(2500, Step("browser.waitForUrl", Param("urlMatch", "example.com/receipt"))),
                Step("browser.waitForUrl", Param("urlMatch", "^https://.*/receipt/\\d+$"),
                    Param("urlHow", "regex")),
            ]);

        Assert.True(result.Succeeded);
        Assert.Contains("browserWaitForUrl example.com/receipt contains 2500", devices.Calls);
        Assert.Contains("browserWaitForUrl ^https://.*/receipt/\\d+$ regex 0", devices.Calls);
        Assert.Contains("Log.BrowserAtAddress", host.Entries.Select(entry => entry.Key));
    }

    /// <summary>
    /// A word the engine does not know, and a pattern the machine cannot read, are mistakes in the
    /// step: they are reported as such rather than by waiting the step's whole clock out and then
    /// saying the page never arrived.
    /// </summary>
    [Fact]
    public async Task A_wait_for_an_address_refuses_a_word_it_does_not_know_before_waiting()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.waitForUrl", Param("urlMatch", "receipt"), Param("urlHow", "sideways")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadMatchMode", result.Key);
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("browserWaitForUrl", StringComparison.Ordinal));

        var (broken, _, _) = await RunAsync(
        [
            Step("browser.waitForUrl", Param("urlMatch", "([a-z"), Param("urlHow", "regex")),
        ], devices);

        Assert.Equal(RunStatus.Failed, broken.Status);
        Assert.Equal("Run.BadPattern", broken.Key);
    }

    /// <summary>
    /// Which moment of a page's loading a step waits for, listed with the words the engine reads.
    /// </summary>
    [Fact]
    public async Task A_wait_can_be_for_one_of_the_moments_a_page_loads_at()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Within(2500, Step("browser.waitForLoad", Param("loadState", "idle"))),
            Step("browser.waitForLoad", Param("loadState", "ready")),
            Step("browser.waitForLoad"),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserWaitForLoad Idle 2500", devices.Calls);
        Assert.Contains("browserWaitForLoad Ready 0", devices.Calls);

        // A step that says nothing waits for the page to have finished loading, which is what a
        // page that shows everything at once arrives at.
        Assert.Contains("browserWaitForLoad Loaded 0", devices.Calls);
    }

    /// <summary>
    /// Where the page is and what it calls itself: what a macro reads when the step after it depends
    /// on which page a click left the browser on rather than on anything the page shows.
    /// </summary>
    [Fact]
    public async Task Where_the_page_is_and_what_it_calls_itself_are_read()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.PageInfo = new("https://example.com/next", "下一步");

        var (result, _, store) = await RunAsync(
        [
            Within(2500, Step("browser.readPage", Returns("url=地址", "title=标题"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserReadPage 2500", devices.Calls);
        Assert.Equal("https://example.com/next", store.Local.Values["地址"].AsText());
        Assert.Equal("下一步", store.Local.Values["标题"].AsText());
    }

    [Fact]
    public async Task A_browser_opened_on_a_profile_names_it()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.open", Param("browser", "edge"), Param("url", "https://example.com"),
                Param("profile", "work")),
            Step("browser.open", Param("browser", "edge"), Param("url", "https://example.com")),
        ], devices);

        Assert.True(result.Succeeded);

        // The name is handed over as it was written; no name at all is a browser that forgets,
        // which is what a step that does not ask for a profile gets.
        Assert.Contains("browserOpen edge https://example.com False work", devices.Calls);
        Assert.Contains("browserOpen edge https://example.com False (none)", devices.Calls);
    }

    [Fact]
    public async Task A_macro_follows_a_click_into_the_tab_it_opened()
    {
        // Sites hand the page the macro came for to a new tab, and left on the old one every step
        // after the click aims at the wrong page: on a site whose pages look alike, a step finds an
        // element that happens to match over there and works on it without anything looking wrong.
        var devices = new FakeDeviceLayer();
        devices.Browser.TabAddress = "https://example.com/receipt";
        var (result, _, _) = await RunAsync(
        [
            Step("browser.open", Param("url", "https://example.com")),
            Step("browser.click", Param("pageSelector", "#pay")),
            Step("browser.switchTab", Param("tabHow", "newest"), Param("tabIndex", "1")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserSwitchTab Newest 1 ", devices.Calls);
    }

    [Fact]
    public async Task A_tab_can_be_asked_for_by_number_by_title_or_by_address()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.switchTab", Param("tabHow", "index"), Param("tabIndex", "3")),
            Step("browser.switchTab", Param("tabHow", "title"), Param("tabMatch", "收据")),
            Step("browser.switchTab", Param("tabHow", "url"), Param("tabMatch", "example.com/receipt")),
            // A word the engine does not know reads as "the newest one": the words come from the
            // action catalogue, so an unknown one is either a file written by hand or a value this
            // engine is older than, and the newest tab is the one a click leaves behind.
            Step("browser.switchTab", Param("tabHow", "sideways"), Param("tabIndex", "1")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserSwitchTab Index 3 ", devices.Calls);
        Assert.Contains("browserSwitchTab Title 0 收据", devices.Calls);
        Assert.Contains("browserSwitchTab Address 0 example.com/receipt", devices.Calls);
        Assert.Contains("browserSwitchTab Newest 1 ", devices.Calls);
    }

    [Fact]
    public async Task A_tab_the_macro_is_done_with_can_be_closed()
    {
        // Closing the tab a click opened is what a macro does with it afterwards, and the browser
        // itself stays open: the pages the macro still wants are in the tabs next to it.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.open", Param("url", "https://example.com")),
            Step("browser.closeTab"),
            Step("browser.close"),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserCloseTab", devices.Calls);
        Assert.Contains("browserClose", devices.Calls);
        Assert.Equal(2, devices.Calls.Count(call => call is "browserCloseTab" or "browserClose"));
    }

    [Fact]
    public async Task An_option_can_be_picked_by_its_text_or_by_its_position()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.select", Param("pageSelector", "#country"), Param("optionValue", "China")),
            Step("browser.select", Param("pageSelector", "#country"), Param("optionValue", "2"),
                Param("by", "index")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserSelect #country #1 China False 0", devices.Calls);
        Assert.Contains("browserSelect #country #1 2 True 0", devices.Calls);
    }

    [Fact]
    public async Task A_position_that_is_not_a_number_stops_the_step()
    {
        // The box holds the number of the option when "by number" is picked, so a word left in it is
        // a mistake in the step rather than a list to search through.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.select", Param("pageSelector", "#country"), Param("optionValue", "second"),
                Param("by", "index")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadPosition", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("browserSelect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_box_is_left_in_the_state_the_step_asked_for()
    {
        // Ticking a box that is already ticked has to leave it ticked, which a click would not: the
        // step says the state it means to leave behind, and both words for it reach the device.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.check", Param("pageSelector", "#agree")),
            Step("browser.check", Param("pageSelector", "#news"), Param("checked", "false")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserCheck #agree #1 True 0", devices.Calls);
        Assert.Contains("browserCheck #news #1 False 0", devices.Calls);
    }

    [Fact]
    public async Task Moving_over_an_element_reaches_the_device()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("browser.hover", Param("pageSelector", "#menu"))], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserHover #menu #1 0", devices.Calls);
    }

    [Fact]
    public async Task A_key_goes_to_the_element_or_to_the_page_itself()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.press", Param("pageSelector", "#search"), Param("keyChord", "Enter")),
            Step("browser.press", Param("keyChord", "Ctrl+S")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserPress #search #1 Enter 0", devices.Calls);
        Assert.Contains("browserPress (none) Ctrl+S 0", devices.Calls);
    }

    [Fact]
    public async Task A_key_name_the_keyboard_has_nothing_for_stops_the_step()
    {
        // The name goes into the page as a key; a name that means nothing would arrive at the
        // browser as a letter that happens to share its first character, which is worse than
        // stopping.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("browser.press", Param("keyChord", "Frobnicate"))], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.UnknownKey", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("browserPress", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Waiting_for_an_element_names_the_state_and_the_step_s_own_clock()
    {
        // The wait gets the step's own limit rather than one of its own, which is what the waiting
        // actions in this catalogue do: "how long this may take" is answered in one place.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Within(4000, Step("browser.waitFor", Param("pageSelector", "#results"))),
            Within(4000, Step("browser.waitFor", Param("pageSelector", "#spinner"),
                Param("elementState", "hidden"))),
            Within(4000, Step("browser.waitFor", Param("pageSelector", "#row"),
                Param("elementState", "detached"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserWaitFor #results #1 Visible 4000", devices.Calls);
        Assert.Contains("browserWaitFor #spinner #1 Hidden 4000", devices.Calls);
        Assert.Contains("browserWaitFor #row #1 Detached 4000", devices.Calls);
    }

    [Fact]
    public async Task Files_are_handed_to_the_page_beside_the_macro()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.upload", Param("pageSelector", "input[name=report]"),
                Param("uploadFiles", "report.pdf\n\nC:\\Temp\\notes.txt\n")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(
            $"browserUpload input[name=report] #1 {devices.Files.BaseFolder}\\report.pdf|C:\\Temp\\notes.txt 0",
            devices.Calls);
    }

    [Fact]
    public async Task An_upload_with_no_files_named_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("browser.upload", Param("pageSelector", "input[name=report]"), Param("uploadFiles", "  \n"))],
            devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MissingPath", result.Key);
    }
}
