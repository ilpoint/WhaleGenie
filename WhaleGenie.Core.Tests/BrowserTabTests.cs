using System;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The tabs of a real browser, driven without a window. Everything else about the browser is
/// checked through the double device layer, which says what the engine asked for; which tab is the
/// one a click opened, and whether a tab can be found by name or by number, is only true or false
/// against a browser that really opens one. No person and no screen are needed for any of it, so
/// unlike picking an element off a page this is a check rather than something to do by hand.
/// </summary>
public class BrowserTabTests : IDisposable
{
    /// <summary>A folder of pages that link to each other, the way a site's pages do.</summary>
    private readonly string _site =
        Path.Combine(Path.GetTempPath(), $"wg-tabs-{Guid.NewGuid():N}");

    public BrowserTabTests() => Directory.CreateDirectory(_site);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_site, true);
        }
        catch (IOException)
        {
            // The browser may still have a handle on the page it was reading; the temporary folder
            // is the system's to clean up.
        }

        GC.SuppressFinalize(this);
    }

    [RealBrowserFact]
    public void A_macro_follows_the_tab_a_click_opened_and_comes_back_from_it()
    {
        // The second page holds an element of the very name the first one has, which is what a site
        // that lays all its pages out the same way looks like.
        Site("receipt",
            "<title>Receipt</title><a id=pay href=\"thank-you.html\" target=_blank>pay</a>");
        Site("thank-you", "<title>Thank you</title><p id=total>42.00</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address("receipt"), headless: true);

        device.Click("#pay");

        // The tab the click opened is what the steps after it mean to work on, and reading from it
        // is what says the macro really got there: the same site's other page has an element of
        // the very same name.
        device.SwitchTab(TabChoice.Newest, 0, string.Empty);
        Assert.Contains("thank-you.html", device.Url, StringComparison.Ordinal);
        Assert.Contains("42.00", device.Text("#total"), StringComparison.Ordinal);

        // The page it was on first is still open: asking for it by its title brings it back, which
        // is how a macro reads from one tab and fills in another.
        device.SwitchTab(TabChoice.Title, 0, "receipt");
        Assert.Contains("receipt.html", device.Url, StringComparison.Ordinal);

        // And what a macro does with the tab a click opened once it has read what it came for.
        device.SwitchTab(TabChoice.Newest, 0, string.Empty);
        device.CloseTab();
        Assert.True(device.IsOpen);
        Assert.Contains("receipt.html", device.Url, StringComparison.Ordinal);
    }

    [RealBrowserFact]
    public void A_tab_that_is_not_there_is_refused_rather_than_passed_over()
    {
        // Staying where it was would leave every step after this one working on the page the macro
        // was already on, which on a site whose pages look alike is wrong without looking wrong.
        using var device = new PlaywrightBrowserDevice();
        Site("receipt", "<title>Receipt</title>");
        device.Open("edge", Address("receipt"), headless: true);

        var missing = Assert.Throws<DeviceActionException>(
            () => device.SwitchTab(TabChoice.Index, 7, string.Empty));
        Assert.Equal("Run.BrowserNoTab", missing.Key);

        var unsaid = Assert.Throws<DeviceActionException>(
            () => device.SwitchTab(TabChoice.Title, 0, "   "));
        Assert.Equal("Run.BrowserTabNeedsText", unsaid.Key);

        // And the one that would go unnoticed: a step waiting for a tab that never opens would
        // otherwise be answered with the tab the macro is already on, which is the one the author
        // was leaving. The wait is cut short here; the whole of it is for the real thing.
        var patience = PlaywrightBrowserDevice.NewTabWait;
        try
        {
            PlaywrightBrowserDevice.NewTabWait = TimeSpan.FromMilliseconds(200);
            var none = Assert.Throws<DeviceActionException>(
                () => device.SwitchTab(TabChoice.Newest, 0, string.Empty));
            Assert.Equal("Run.BrowserNoNewTab", none.Key);
        }
        finally
        {
            PlaywrightBrowserDevice.NewTabWait = patience;
        }
    }

    /// <summary>Writes one page of the site down.</summary>
    private void Site(string name, string body)
        => File.WriteAllText(Path.Combine(_site, $"{name}.html"),
            $"<html><body>{body}</body></html>");

    /// <summary>The address one of the site's pages is read at.</summary>
    private string Address(string name) => new Uri(Path.Combine(_site, $"{name}.html")).AbsoluteUri;
}

/// <summary>
/// A check that drives a real browser. Edge is on every Windows this program runs on, but a machine
/// without it says the check was skipped rather than failing one about tabs.
/// </summary>
public sealed class RealBrowserFactAttribute : FactAttribute
{
    public RealBrowserFactAttribute()
    {
        // Asking the device itself keeps the places Edge can sit in one place.
        using var device = new PlaywrightBrowserDevice();
        if (!device.Ready("edge"))
        {
            Skip = "This machine has no Edge to drive.";
        }
    }
}
