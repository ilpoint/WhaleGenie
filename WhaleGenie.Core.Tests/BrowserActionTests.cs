using System;
using System.Globalization;
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
[Collection("Browser")]
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

    [Fact]
    public void A_visible_browser_follows_its_window_size()
    {
        Assert.Null(PlaywrightBrowserDevice.ViewportFor(headless: true));

        var visible = PlaywrightBrowserDevice.ViewportFor(headless: false);
        Assert.NotNull(visible);
        Assert.Equal(-1, visible.Width);
        Assert.Equal(-1, visible.Height);
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
    /// A dialog stops the page where it stands until somebody answers it, so the answer is armed
    /// first and the click that raises it comes afterwards: this is why answering one is a block.
    /// </summary>
    [RealBrowserFact]
    public void A_dialog_is_answered_around_the_click_that_raises_it()
    {
        Site("<p id=log></p>"
            + "<button id=ask onclick=\"document.getElementById('log').textContent ="
            + " confirm('确定要提交吗？') ? 'yes' : 'no'\">提交</button>"
            + "<button id=name onclick=\"document.getElementById('log').textContent ="
            + " prompt('文件名？') || ''\">命名</button>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // Accepting it lets the page go on, and what the box said is handed over afterwards.
        device.ArmDialog(accept: true, text: string.Empty);
        device.Click(At("#ask"), ClickWay.Single, force: false, timeoutMs: 5000);
        Assert.Equal("确定要提交吗？", device.TakeDialog());
        Assert.Equal("yes", device.Text(At("#log"), PagePart.Text, "", 5000).Trim());

        // Dismissing it is the other answer, and the page goes the other way. What it said is still
        // what the step hands over: it is the dialog that was answered either way.
        device.ArmDialog(accept: false, text: string.Empty);
        device.Click(At("#ask"), ClickWay.Single, force: false, timeoutMs: 5000);
        Assert.Equal("确定要提交吗？", device.TakeDialog());
        Assert.Equal("no", device.Text(At("#log"), PagePart.Text, "", 5000).Trim());

        // A dialog that asks for writing gets what the step said.
        device.ArmDialog(accept: true, text: "报告.csv");
        device.Click(At("#name"), ClickWay.Single, force: false, timeoutMs: 5000);
        Assert.Equal("报告.csv", device.Text(At("#log"), PagePart.Text, "", 5000).Trim());

        // With nothing armed it is dismissed, which is what a browser does with nobody listening: a
        // step that never mentions dialogs behaves as it always did.
        device.TakeDialog();
        device.Click(At("#ask"), ClickWay.Single, force: false, timeoutMs: 5000);
        Assert.Equal("no", device.Text(At("#log"), PagePart.Text, "", 5000).Trim());
        Assert.Null(device.TakeDialog());
    }

    /// <summary>
    /// A script of the step's own runs inside the page, and what it returns is what the step hands
    /// over: the way round whatever the catalogue does not cover.
    /// </summary>
    [RealBrowserFact]
    public void A_script_of_the_steps_own_runs_inside_the_page()
    {
        Site("<title>报告</title><p class=row>甲</p><p class=row>乙</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        Assert.Equal("2", device.Evaluate("() => document.querySelectorAll('.row').length", 5000));

        // Writing comes back written as JSON, which is what makes the result one shape whatever the
        // script handed over.
        Assert.Equal("\"报告\"", device.Evaluate("() => document.title", 5000));

        // A record keeps its shape, so a macro reads one field of it rather than cutting the text up.
        var record = device.Evaluate(
            "() => ({ rows: document.querySelectorAll('.row').length,"
            + " first: document.querySelector('.row').textContent })", 5000);
        Assert.Contains("\"rows\":2", record, StringComparison.Ordinal);
        Assert.Contains("\"first\":\"甲\"", record, StringComparison.Ordinal);

        // A script that goes wrong is the browser failing, and it says what the page said.
        var broke = Assert.Throws<DeviceActionException>(
            () => device.Evaluate("() => { throw new Error('落空'); }", 5000));
        Assert.Equal("Run.BrowserFailed", broke.Key);
    }

    /// <summary>
    /// The three ways of clicking reach the page as three different things, which is what a page
    /// whose own menu only opens on the right button depends on.
    /// </summary>
    [RealBrowserFact]
    public void A_double_click_a_right_click_and_a_forced_click_all_reach_the_page()
    {
        Site("<p id=log></p>"
            + "<button id=row ondblclick=\"log.textContent += 'double '\">row</button>"
            + "<button id=menu oncontextmenu=\"log.textContent += 'menu '; return false;\">menu</button>"
            // A button that never stands still, which is what the driver refuses to click: it waits
            // for the element to hold the same place twice in a row, and this one never does.
            + "<style>@keyframes pulse { from { transform: scale(1); } to { transform: scale(1.04); } }</style>"
            + "<button id=moving style=\"animation: pulse 80ms infinite alternate\""
            + " onclick=\"log.textContent += 'moving '\">press</button>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        device.Click(At("#row"), ClickWay.Double, force: false, timeoutMs: 5000);
        Assert.Equal("double", device.Text(At("#log"), PagePart.Text, "", 5000).Trim());

        // The page's own menu opens on the right button, and the page's listener for it runs.
        device.Click(At("#menu"), ClickWay.Right, force: false, timeoutMs: 5000);
        Assert.Equal("double menu", device.Text(At("#log"), PagePart.Text, "", 5000).Trim());

        // Never standing still, the same click is refused unless the step says to make it anyway,
        // and then it reaches the button: this is what a page whose own buttons are always animating
        // leaves an author no other way round of.
        Assert.Throws<DeviceActionException>(
            () => device.Click(At("#moving"), ClickWay.Single, force: false, timeoutMs: 1000));
        device.Click(At("#moving"), ClickWay.Single, force: true, timeoutMs: 5000);
        Assert.Equal("double menu moving", device.Text(At("#log"), PagePart.Text, "", 5000).Trim());
    }

    /// <summary>
    /// Writing typed one key at a time is what a page that watches the typing itself sees: the field
    /// hands every key on as it arrives, and a gap asked for between two of them is left.
    /// </summary>
    [RealBrowserFact]
    public void Writing_typed_one_key_at_a_time_is_seen_key_by_key()
    {
        Site("<input id=search><p id=seen></p>"
            + "<script>"
            + "window.__gaps = []; window.__steps = [];"
            + "const box = document.getElementById('search');"
            + "box.addEventListener('input', () => {"
            + "  window.__gaps.push(Date.now());"
            + "  window.__steps.push(box.value); });"
            + "</script>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // Written in at once, the page sees one change to the field and never a key.
        device.Fill(At("#search"), "换一整段", FillWay.Fill, 0, 5000);
        Assert.Equal("1", device.InPage("() => String(window.__gaps.length)"));
        Assert.Equal("换一整段", device.Text(At("#search"), PagePart.Value, "", 5000));

        // Typed one key at a time, the page watches the field fill up one key at a time, and what it
        // held before is replaced rather than added to.
        device.InPage("() => { window.__gaps = []; window.__steps = []; return true; }");
        device.Fill(At("#search"), "abc", FillWay.Type, 0, 5000);
        Assert.Equal("""["a","ab","abc"]""", device.InPage("() => JSON.stringify(window.__steps)"));
        Assert.Equal("abc", device.Text(At("#search"), PagePart.Value, "", 5000));

        // And a gap asked for between two keys is one the page can measure.
        device.InPage("() => { window.__gaps = []; return true; }");
        device.Fill(At("#search"), "de", FillWay.Type, 150, 5000);
        var apart = device.InPage(
            "() => window.__gaps.length > 1 ? String(window.__gaps[1] - window.__gaps[0]) : '0'");
        Assert.True(int.Parse(apart, CultureInfo.InvariantCulture) >= 100, apart);
    }

    /// <summary>The three ways of moving a page that name no element each change where it is showing.</summary>
    [RealBrowserFact]
    public void The_page_can_be_moved_by_a_screen_to_the_bottom_and_back_to_the_top()
    {
        // A page long enough to scroll, which is what a page that lists something always is.
        Site("<div style=\"height:4000px\">长</div>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        device.Scroll(null, ScrollWay.Screen, 5000);
        var one = InPage("window.scrollY", device);
        Assert.True(one > 0, "one screen down left the page where it was");

        device.Scroll(null, ScrollWay.Bottom, 5000);
        var end = InPage("window.scrollY", device);
        Assert.True(end > one, $"the bottom left the page at {end}, one screen was {one}");

        device.Scroll(null, ScrollWay.Top, 5000);
        Assert.Equal(0, InPage("window.scrollY", device));

        // Scrolling to an element is the way that names one, and it is what makes a page that only
        // loads something once it is nearly on screen load it.
        File.WriteAllText(Path.Combine(_site, "long.html"),
            "<html><body><div style=\"height:3000px\"></div><p id=mark>到了</p></body></html>");
        device.GoTo(new Uri(Path.Combine(_site, "long.html")).AbsoluteUri, 5000);

        device.Scroll(At("#mark"), ScrollWay.Element, 5000);
        Assert.True(InPage("window.scrollY", device) > 0);

        // The one way of scrolling that needs an element refuses a step that named none.
        var nobody = Assert.Throws<DeviceActionException>(
            () => device.Scroll(null, ScrollWay.Element, 5000));
        Assert.Equal("Run.BrowserNoSelector", nobody.Key);
    }

    /// <summary>
    /// A file the page hands over is saved where the step says, whether the step clicked something to
    /// start it or waited for the page to start one by itself.
    /// </summary>
    [RealBrowserFact]
    public void A_file_the_page_hands_over_is_saved_where_the_step_says()
    {
        Site("<button id=export onclick=\"exporting()\">导出</button>"
            + "<script>"
            + "window.exporting = () => {"
            + "  const blob = new Blob(['金额,日期\\n100,3 日'], { type: 'text/csv' });"
            + "  const link = document.createElement('a');"
            + "  link.href = URL.createObjectURL(blob);"
            + "  link.download = '订单.csv';"
            + "  link.click(); };"
            + "</script>");

        var where = Path.Combine(_site, "下载");
        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // The step clicks the element that starts it, and the file is saved where it was told to.
        var saved = device.Download(At("#export"), where, 5000);
        Assert.Equal("订单.csv", saved.Name);
        Assert.Equal(Path.Combine(where, "订单.csv"), saved.Path);
        Assert.Contains("100,3 日", File.ReadAllText(saved.Path), StringComparison.Ordinal);

        // A step that names no element waits for the next download the page starts by itself, and a
        // file that arrived before anybody asked for it is handed over rather than thrown away.
        device.InPage("() => { window.exporting(); return true; }");
        var waited = device.Download(null, where, 5000);
        Assert.Equal("订单.csv", waited.Name);
        Assert.True(File.Exists(waited.Path));

        // Nothing turning up within the step's own clock is the step running out of time.
        device.InPage("() => { window.exporting = () => {}; return true; }");
        var late = Assert.Throws<DeviceActionException>(
            () => device.Download(At("#export"), where, 500));
        Assert.Equal("Run.Timeout", late.Key);
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

    /// <summary>A number the page worked out, read back out of it as a number rather than as text.</summary>
    private static double InPage(string script, PlaywrightBrowserDevice device)
        => double.TryParse(device.InPage($"() => String({script})"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
