using System;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The six things a macro does to a page that cannot be done by clicking and typing: picking from a
/// dropdown, leaving a box ticked, hovering, sending a key, waiting for an element, and handing over
/// a file. What the engine asks of the device is checked through the double device layer; whether a
/// browser really picks the option or really waits is only true or false against a browser, and a
/// page served from a temporary folder answers that with no person and no screen involved.
/// </summary>
public class BrowserActionTests : IDisposable
{
    /// <summary>The folder the pages of this check are read from.</summary>
    private readonly string _site = Path.Combine(Path.GetTempPath(), $"wg-page-{Guid.NewGuid():N}");

    /// <summary>The file handed to the page's file input.</summary>
    private readonly string _handover = Path.Combine(Path.GetTempPath(), $"wg-file-{Guid.NewGuid():N}.txt");

    public BrowserActionTests()
    {
        Directory.CreateDirectory(_site);
        File.WriteAllText(_handover, "what a macro hands over");
    }

    public void Dispose()
    {
        foreach (var folder in new[] { _site })
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
                // The browser may still hold the page it was reading; the temporary folder is the
                // system's to clean up.
            }
        }

        File.Delete(_handover);
        GC.SuppressFinalize(this);
    }

    [RealBrowserFact]
    public void A_dropdown_a_box_a_menu_a_key_and_a_file_all_reach_the_page()
    {
        Site(
            // Every part of the page writes down what was done to it, which is what says the step
            // reached the page: a dropdown and a checkbox report themselves, and the key and the
            // file are written down by the page's own listeners.
            "<select id=country onchange=\"picked.textContent = this.value\">"
            + "<option value=cn>China</option><option value=jp>Japan</option></select>"
            + "<p id=picked></p>"
            + "<input type=checkbox id=agree onchange=\"ticked.textContent = this.checked\">"
            + "<input type=checkbox id=news checked onchange=\"ticked.textContent = this.checked\">"
            + "<p id=ticked></p>"
            + "<div id=menu>Menu<span id=item hidden>Item</span></div>"
            + "<input id=search><p id=typed></p>"
            + "<input type=file id=report onchange=\"held.textContent = this.files[0].name\">"
            + "<p id=held></p>"
            + "<script>"
            + "document.getElementById('search').addEventListener('keydown',"
            + " e => document.getElementById('typed').textContent = e.key);"
            + "document.getElementById('menu').addEventListener('mouseover',"
            + " () => document.getElementById('item').hidden = false);"
            + "</script>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // The option is picked by its visible text rather than by its value, which is the reading a
        // person writing a macro expects of the box.
        device.Select(At("#country"), "Japan", byIndex: false, timeoutMs: 5000);
        Assert.Equal("jp", device.Text(At("#picked"), PagePart.Text, string.Empty, 5000).Trim());

        // And by its place in the list, counted from the top starting at 1.
        device.Select(At("#country"), "1", byIndex: true, timeoutMs: 5000);
        Assert.Equal("cn", device.Text(At("#picked"), PagePart.Text, string.Empty, 5000).Trim());

        // A box that is left as it was, and one that is unticked: what the step asks for is the
        // state to end up in, where a click on an already-ticked box would untick it.
        device.Check(At("#agree"), wanted: true, timeoutMs: 5000);
        Assert.Equal("true", device.Text(At("#ticked"), PagePart.Text, string.Empty, 5000).Trim());

        device.Check(At("#news"), wanted: false, timeoutMs: 5000);
        Assert.Equal("false", device.Text(At("#ticked"), PagePart.Text, string.Empty, 5000).Trim());

        // Hovering reveals what a click would not: the menu opens under the pointer, and the wait
        // for the item is what says it did.
        device.Hover(At("#menu"), 5000);
        device.WaitFor(At("#item"), ElementWait.Visible, 2000);

        // A key sent to the page's own body arrives where the page is listening for it. The page
        // writes down the key name it was handed, which is what says the right key was pressed.
        device.Press(At("#search"), "Enter", 5000);
        Assert.Equal("Enter", device.Text(At("#typed"), PagePart.Text, string.Empty, 5000).Trim());

        // A file cannot be typed into a page, so handing it over is the way round it: the input
        // holds the name of the file that was handed over.
        device.Upload(At("#report"), [_handover], 5000);
        Assert.Equal(Path.GetFileName(_handover), device.Text(At("#held"), PagePart.Text, string.Empty, 5000).Trim());
    }

    [RealBrowserFact]
    public void Waiting_ends_the_moment_the_element_arrives_and_gives_up_when_it_never_does()
    {
        Site(
            "<p id=spinner>loading</p>"
            + "<script>setTimeout(() => {"
            + " document.getElementById('spinner').remove();"
            + " const row = document.createElement('div');"
            + " row.id = 'row'; row.textContent = 'ready';"
            + " document.body.appendChild(row); }, 200);</script>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        device.WaitFor(At("#row"), ElementWait.Visible, 5000);
        device.WaitFor(At("#spinner"), ElementWait.Detached, 5000);
        Assert.Equal("ready", device.Text(At("#row"), PagePart.Text, string.Empty, 5000).Trim());

        // An element that never turns up is the step running out of time rather than the browser
        // failing: the wait gets the length the step was allowed and says so when it passes.
        var late = Assert.Throws<DeviceActionException>(
            () => device.WaitFor(At("#never"), ElementWait.Visible, 300));
        Assert.Equal("Run.Timeout", late.Key);
    }

    [RealBrowserFact]
    public void A_key_the_page_has_nothing_for_is_refused_rather_than_typed()
    {
        Site("<p>plain</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var unknown = Assert.Throws<DeviceActionException>(() => device.Press(null, "Frobnicate", 5000));
        Assert.Equal("Run.UnknownPageKey", unknown.Key);
    }

    /// <summary>
    /// One element is three answers, and a step says which of them it is after: the writing the
    /// element shows, what it holds, or one attribute of its markup.
    /// </summary>
    [RealBrowserFact]
    public void A_step_reads_the_writing_it_shows_the_value_it_holds_or_an_attribute()
    {
        Site("<input id=card value=1234>"
            + "<a id=link href=\"https://example.com/report\">报告</a>"
            + "<p id=note>备注</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // What a box holds and what it shows are two different things, and the address a link goes
        // to is neither of them: each is read by naming it rather than by being the default.
        Assert.Equal("1234", device.Text(At("#card"), PagePart.Value, "", 5000));
        Assert.Equal("https://example.com/report",
            device.Text(At("#link"), PagePart.Attribute, "href", 5000));
        Assert.Equal("报告", device.Text(At("#link"), PagePart.Text, "", 5000));

        // An element without that attribute is read as empty rather than counted as missing: the
        // question was what the attribute says, and this element says nothing.
        Assert.Equal("", device.Text(At("#note"), PagePart.Attribute, "href", 5000));

        // Reading an attribute without saying which one is a question with no answer.
        var unnamed = Assert.Throws<DeviceActionException>(
            () => device.Text(At("#link"), PagePart.Attribute, "  ", 5000));
        Assert.Equal("Run.BrowserNoAttribute", unnamed.Key);
    }

    /// <summary>
    /// A page lists the same thing many times over, and every one of them can be read in one go,
    /// starting wherever the step says.
    /// </summary>
    [RealBrowserFact]
    public void Every_element_a_selector_names_is_read_and_the_list_starts_where_the_step_says()
    {
        Site("<p class=row>第一行</p><p class=row>第二行</p><p class=row>第三行</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // From the top, which is what the field starts at.
        var all = device.ReadAll(At(".row"), PagePart.Text, "", 100, 5000);
        Assert.Equal(["第一行", "第二行", "第三行"], all);

        // And from the second onwards, which is what a macro does with a list whose first entry it
        // has already read.
        var rest = device.ReadAll(PageTarget.Of(".row", 2), PagePart.Text, "", 100, 5000);
        Assert.Equal(["第二行", "第三行"], rest);

        // The most entries a step asked for is the most it is handed.
        Assert.Equal(["第一行", "第二行"],
            device.ReadAll(At(".row"), PagePart.Text, "", 2, 5000));

        // A selector that names nothing is a failure rather than an empty list, the same as it is
        // for every other step that names an element.
        var nothing = Assert.Throws<DeviceActionException>(
            () => device.ReadAll(At(".absent"), PagePart.Text, "", 100, 300));
        Assert.Equal("Run.BrowserNotFound", nothing.Key);
    }

    /// <summary>
    /// A table is read out of the page's own markup: the names the page writes above its columns,
    /// and every row under them.
    /// </summary>
    [RealBrowserFact]
    public void A_table_of_a_page_is_read_with_its_column_names_and_its_rows()
    {
        Site("<table id=report><thead><tr><th>金额</th><th>日期</th></tr></thead>"
            + "<tbody><tr><td>100</td><td>3 日</td></tr>"
            + "<tr><td>200</td><td>4 日</td></tr></tbody></table>"
            // A table that writes no strip of names holds data from its first row, and the engine
            // names the columns by letter rather than calling that row the names.
            + "<table id=plain><tr><td>Ann</td><td>31</td></tr></table>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var table = device.ReadTable(At("#report"), 100, 5000);
        Assert.Equal(["金额", "日期"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["100", "3 日"], table.Rows[0]);
        Assert.Equal(["200", "4 日"], table.Rows[1]);

        var plain = device.ReadTable(At("#plain"), 100, 5000);
        Assert.Empty(plain.Columns);
        Assert.Equal(["Ann", "31"], plain.Rows[0]);

        // The most rows a step asked for is the most that is read, so a table nobody bounded cannot
        // be read forever.
        Assert.Single(device.ReadTable(At("#report"), 1, 5000).Rows);
    }

    /// <summary>
    /// A condition about an element of the page is a look rather than a wait, and it is asked again
    /// by whatever block holds it: the same question has to answer "no" while the page is fetching
    /// and "yes" once it has arrived.
    /// </summary>
    [RealBrowserFact]
    public void The_state_of_an_element_of_the_page_is_asked_about_rather_than_waited_for()
    {
        Site("<p id=spinner>loading</p>"
            + "<script>setTimeout(() => {"
            + " document.getElementById('spinner').remove();"
            + " const row = document.createElement('p');"
            + " row.id = 'row'; row.textContent = 'ready';"
            + " document.body.appendChild(row); }, 300);</script>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // The spinner is there and the row is not, and both answers come back at once rather than
        // after the step's own clock.
        Assert.True(device.Exists(At("#spinner"), ElementWait.Visible));
        Assert.False(device.Exists(At("#row"), ElementWait.Visible));
        Assert.False(device.Exists(At("#row"), ElementWait.Attached));
        Assert.True(device.Exists(At("#row"), ElementWait.Detached));

        device.WaitFor(At("#row"), ElementWait.Visible, 5000);

        Assert.True(device.Exists(At("#row"), ElementWait.Attached));
        Assert.False(device.Exists(At("#row"), ElementWait.Detached));
        Assert.True(device.Exists(At("#spinner"), ElementWait.Hidden));
        Assert.True(device.Exists(At("#spinner"), ElementWait.Detached));

        // A condition that names no element is a mistake in the step rather than an answer.
        var unnamed = Assert.Throws<DeviceActionException>(
            () => device.Exists(PageTarget.Of("", 1), ElementWait.Visible));
        Assert.Equal("Run.BrowserNoSelector", unnamed.Key);
    }

    /// <summary>
    /// Waiting for an address is what a macro does after a click that goes somewhere: the element it
    /// is after lives on the page the click leads to, and naming it too early finds the page being
    /// left behind.
    /// </summary>
    [RealBrowserFact]
    public void A_wait_can_be_for_an_address_the_page_turns_out_to_be_at()
    {
        // A page that sends itself somewhere else a moment after it is read, which is what a site
        // that decides where to go from a script does.
        Site("<p>leaving</p>"
            + "<script>setTimeout(() => { location.hash = '#receipt/42'; }, 200);</script>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        device.WaitForUrl("receipt", TextCompare.Contains, 5000);
        Assert.Contains("receipt", device.Url, StringComparison.Ordinal);

        // The same five ways of comparing writing, including a pattern.
        device.WaitForUrl(@"#receipt/\d+$", TextCompare.Regex, 5000);

        // An address the page never reaches is the step running out of time rather than the browser
        // failing, the same as waiting for an element that never turns up.
        var late = Assert.Throws<DeviceActionException>(
            () => device.WaitForUrl("somewhere-else", TextCompare.Contains, 300));
        Assert.Equal("Run.Timeout", late.Key);
    }

    /// <summary>
    /// The three moments of a page's loading, each of which a macro can wait for by name.
    /// </summary>
    [RealBrowserFact]
    public void Each_moment_of_a_pages_loading_can_be_waited_for()
    {
        Site("<p>ready</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // The page has already arrived at all three by the time the browser has answered, and each
        // of them ends the moment it is there rather than at the end of the step's own clock.
        device.WaitForLoad(PageLoad.Ready, 5000);
        device.WaitForLoad(PageLoad.Loaded, 5000);
        device.WaitForLoad(PageLoad.Idle, 5000);

        // A page that takes its time gets as far as the document and no further, which is what the
        // three names are for: waiting for the document alone ends while the rest is still coming.
        File.WriteAllText(Path.Combine(_site, "slow.html"),
            "<html><head><title>慢</title></head><body>"
            + "<script>document.write('ready');</script>");
        device.GoTo(new Uri(Path.Combine(_site, "slow.html")).AbsoluteUri, 5000);
        device.WaitForLoad(PageLoad.Ready, 5000);
    }

    /// <summary>Where the page is and what it calls itself, which is what a step reads off the page itself.</summary>
    [RealBrowserFact]
    public void A_page_says_where_it_is_and_what_it_calls_itself()
    {
        Site("<title>报告页</title><p>正文</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var page = device.ReadPage(5000);
        Assert.Equal(Address(), page.Url);
        Assert.Equal("报告页", page.Title);
    }

    /// <summary>Writes the check's one page down.</summary>
    private void Site(string body)
        => File.WriteAllText(Path.Combine(_site, "page.html"),
            $"<html><body>{body}</body></html>");

    /// <summary>The address that page is read at.</summary>
    private string Address() => new Uri(Path.Combine(_site, "page.html")).AbsoluteUri;

    /// <summary>The element a step means when its field holds one selector and it wants the first match.</summary>
    private static PageTarget At(string selector) => PageTarget.Of(selector, 1);
}
