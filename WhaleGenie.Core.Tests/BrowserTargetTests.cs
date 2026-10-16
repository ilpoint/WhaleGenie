using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How a browser step finds the element it means: several ways of naming it tried in the order they
/// were written, which of the matches the step wants, and the step's own clock. All of it is true
/// or false against a real browser, so the pages are served from a temporary folder and driven
/// without a window.
/// </summary>
public class BrowserTargetTests : IDisposable
{
    /// <summary>The folder the pages of this check are read from.</summary>
    private readonly string _site = Path.Combine(Path.GetTempPath(), $"wg-target-{Guid.NewGuid():N}");

    public BrowserTargetTests() => Directory.CreateDirectory(_site);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_site, true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The browser may still hold the page it was reading; the temporary folder is the
            // system's to clean up.
        }

        GC.SuppressFinalize(this);
    }

    [RealBrowserFact]
    public void A_second_selector_is_used_when_the_first_finds_nothing()
    {
        Site("<div id=log></div>"
            + "<button id=a onclick=\"document.getElementById('log').textContent='a'\">A</button>"
            + "<button id=b onclick=\"document.getElementById('log').textContent='b'\">B</button>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true);

        // The first way of naming the element is the one used when it finds anything at all.
        device.Click(PageTarget.Of("#a\n#b", 1), 5000);
        Assert.Equal("a", device.Text(At("#log"), 5000).Trim());

        // A page that has been redesigned is met by a second line: the first selector is not there
        // any more and the step goes on working.
        var used = device.Click(PageTarget.Of("#gone\n#b", 1), 5000);
        Assert.Equal("#b", used);
        Assert.Equal("b", device.Text(At("#log"), 5000).Trim());
    }

    [RealBrowserFact]
    public void The_step_says_which_of_the_matches_it_means()
    {
        Site("<div id=log></div>"
            + "<button class=row onclick=\"document.getElementById('log').textContent='1'\">one</button>"
            + "<button class=row onclick=\"document.getElementById('log').textContent='2'\">two</button>"
            + "<button class=row onclick=\"document.getElementById('log').textContent='3'\">three</button>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true);

        device.Click(PageTarget.Of(".row", 2), 5000);
        Assert.Equal("2", device.Text(At("#log"), 5000).Trim());

        // More matches than the page holds is the same answer as finding nothing, and it says which
        // of the two it was: the page holds elements of that name, just not that many of them.
        var missing = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of(".row", 4), 5000));
        Assert.Equal("Run.BrowserTooFew", missing.Key);
        Assert.Contains(".row", missing.Detail, StringComparison.Ordinal);
    }

    [RealBrowserFact]
    public void Nothing_found_names_every_way_that_was_tried_and_touches_nothing()
    {
        Site("<div id=log></div>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true);

        var missing = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of("#first\n#second", 1), 5000));

        Assert.Equal("Run.BrowserNotFound", missing.Key);
        Assert.Contains("#first", missing.Detail, StringComparison.Ordinal);
        Assert.Contains("#second", missing.Detail, StringComparison.Ordinal);

        // A step with nothing to look for says so rather than waiting its clock out.
        var empty = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of("  \n", 1), 5000));
        Assert.Equal("Run.BrowserNoSelector", empty.Key);
    }

    [RealBrowserFact]
    public void The_step_s_own_clock_is_what_the_browser_waits()
    {
        Site("<p>nothing to find here</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true);

        var clock = Stopwatch.StartNew();
        var missing = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of("#never", 1), 800));
        clock.Stop();

        Assert.Equal("Run.BrowserNotFound", missing.Key);

        // The half minute Playwright falls back on is not what a step that said 800 milliseconds
        // gets: it is the step's own clock, and it is counted from the step rather than afterwards.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"waited {clock.Elapsed}");
    }

    [RealBrowserFact]
    public void A_pick_offers_every_way_of_naming_the_element_the_steadies_first()
    {
        Site("<span class=wrap><button data-testid=save aria-label=\"Save changes\">Save</button></span>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true);

        // A pick waits for a person to click something, so what a click would do is done for it:
        // the picker is armed, the ways it works out are asked for, and they are handed back over
        // the very binding the page's own click handler answers through.
        var picking = Task.Run(() => device.Pick("pick something", 15000));
        var ways = Ways(device, "document.querySelector('button')");
        Assert.NotEmpty(ways);
        device.Evaluate("(ways) => window.__whalegeniePicked(ways)", ways);

        var picked = picking.GetAwaiter().GetResult();

        // The steadiest way of naming a button is the attribute the site keeps for its own tests,
        // and it is what the step is left holding.
        Assert.Equal("[data-testid=\"save\"]", picked.Selector);
        Assert.Equal(PickKind.DataAttribute, picked.Candidates[0].Kind);

        // The rest are there to be written under it: a page that has been redesigned is met by a
        // second way of naming the element rather than by a re-pick.
        Assert.Contains(picked.Candidates, way => way.Kind is PickKind.Role);
        Assert.Contains(picked.Candidates, way => way.Kind is PickKind.Text);
        Assert.Contains(picked.Candidates, way => way.Kind is PickKind.Path);
        Assert.All(picked.Candidates, way => Assert.True(way.Matches >= 1, way.Selector));

        // What was written down is what the engine reads back: the selector the pick went with
        // names exactly the one element it was clicked on.
        Assert.Equal(1, device.Matches(picked.Selector));
    }

    [RealBrowserFact]
    public void A_button_inside_a_shadow_root_is_named_and_clicked()
    {
        // A page built out of components keeps its elements inside shadow roots, where the page's
        // own reading of a CSS selector cannot see them and a way of naming one has to cross the
        // boundary to be any use.
        Site("<div id=host></div><p id=log></p><script>"
            + "const root = document.getElementById('host').attachShadow({ mode: 'open' });"
            + "root.innerHTML = '<button>Deep</button>';"
            + "root.querySelector('button').addEventListener('click',"
            + " () => { document.getElementById('log').textContent = 'deep'; });"
            + "</script>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true);

        var picking = Task.Run(() => device.Pick("pick something", 15000));
        var ways = Ways(device, "document.getElementById('host').shadowRoot.querySelector('button')");
        Assert.NotEmpty(ways);
        device.Evaluate("(ways) => window.__whalegeniePicked(ways)", ways);

        var picked = picking.GetAwaiter().GetResult();

        // The path of tags starts at the host rather than at the shadow root, which is the one way
        // of naming an element that reaches through the boundary, and the count is what says so:
        // it finds the very element the person clicked.
        var path = picked.Candidates.First(way => way.Kind is PickKind.Path).Selector;
        Assert.Contains("#host", path, StringComparison.Ordinal);
        Assert.Equal(1, device.Matches(path));

        device.Click(PageTarget.Of(path, 1), 5000);
        Assert.Equal("deep", device.Text(At("#log"), 5000).Trim());
    }

    /// <summary>
    /// The ways of naming an element of the page, asked for the way a later look would ask: through
    /// the picker the device has just armed, once it is armed.
    /// </summary>
    private static string Ways(PlaywrightBrowserDevice device, string element)
    {
        for (var look = 0; look < 200; look++)
        {
            var ways = device.Evaluate(
                "() => window.__whalegenieCandidates && " + element
                + " ? JSON.stringify(window.__whalegenieCandidates(" + element + "))"
                + " : ''");
            if (ways.Length > 0)
            {
                return ways;
            }

            Thread.Sleep(50);
        }

        return string.Empty;
    }

    /// <summary>Writes down the one page of this check.</summary>
    private void Site(string body)
        => File.WriteAllText(Path.Combine(_site, "page.html"),
            $"<html><body>{body}</body></html>");

    /// <summary>The address that page is read at.</summary>
    private string Address() => new Uri(Path.Combine(_site, "page.html")).AbsoluteUri;

    /// <summary>The element a step means when its field holds one selector and it wants the first match.</summary>
    private static PageTarget At(string selector) => PageTarget.Of(selector, 1);
}
