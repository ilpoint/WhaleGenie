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
        device.Open("edge", Address(), headless: true);

        // The option is picked by its visible text rather than by its value, which is the reading a
        // person writing a macro expects of the box.
        device.Select(At("#country"), "Japan", byIndex: false, timeoutMs: 5000);
        Assert.Equal("jp", device.Text(At("#picked"), 5000).Trim());

        // And by its place in the list, counted from the top starting at 1.
        device.Select(At("#country"), "1", byIndex: true, timeoutMs: 5000);
        Assert.Equal("cn", device.Text(At("#picked"), 5000).Trim());

        // A box that is left as it was, and one that is unticked: what the step asks for is the
        // state to end up in, where a click on an already-ticked box would untick it.
        device.Check(At("#agree"), wanted: true, timeoutMs: 5000);
        Assert.Equal("true", device.Text(At("#ticked"), 5000).Trim());

        device.Check(At("#news"), wanted: false, timeoutMs: 5000);
        Assert.Equal("false", device.Text(At("#ticked"), 5000).Trim());

        // Hovering reveals what a click would not: the menu opens under the pointer, and the wait
        // for the item is what says it did.
        device.Hover(At("#menu"), 5000);
        device.WaitFor(At("#item"), ElementWait.Visible, 2000);

        // A key sent to the page's own body arrives where the page is listening for it. The page
        // writes down the key name it was handed, which is what says the right key was pressed.
        device.Press(At("#search"), "Enter", 5000);
        Assert.Equal("Enter", device.Text(At("#typed"), 5000).Trim());

        // A file cannot be typed into a page, so handing it over is the way round it: the input
        // holds the name of the file that was handed over.
        device.Upload(At("#report"), [_handover], 5000);
        Assert.Equal(Path.GetFileName(_handover), device.Text(At("#held"), 5000).Trim());
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
        device.Open("edge", Address(), headless: true);

        device.WaitFor(At("#row"), ElementWait.Visible, 5000);
        device.WaitFor(At("#spinner"), ElementWait.Detached, 5000);
        Assert.Equal("ready", device.Text(At("#row"), 5000).Trim());

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
        device.Open("edge", Address(), headless: true);

        var unknown = Assert.Throws<DeviceActionException>(() => device.Press(null, "Frobnicate", 5000));
        Assert.Equal("Run.UnknownPageKey", unknown.Key);
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
