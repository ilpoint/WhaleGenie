using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// The page elements are picked off. When a macro already has a browser open — the one the step is
/// about — that is the page picked on; one is started only when the program has none open, and it
/// is the page the macro goes on to use.
/// </summary>
/// <remarks>
/// The picking itself is the page's own work: the device hands the page a script that outlines
/// whatever the pointer is over and answers with a selector when something is clicked. Nothing
/// here reads the screen, which is what makes this different from the UI Automation picker next to
/// it — a page drawn by a browser is not a window full of controls.
///
/// The page is the one the program's device layer holds, which is the very page a <c>browser.*</c>
/// step drives. A page opened here is therefore the page a run uses, rather than a second one
/// beside it: what was picked on is what the macro acts on, logins and all. It is left open
/// between picks and let go of with the layer, because fetching it and signing in again for every
/// field would be worse than leaving a window up.
/// </remarks>
internal static class BrowserPicker
{
    /// <summary>How long a pick waits for a click before giving up.</summary>
    private const int PickTimeoutMs = 120_000;

    /// <summary>
    /// Goes to the page the step is about and answers with the selector of the element that was
    /// clicked on it, or an empty string when the person gave up.
    /// </summary>
    public static async Task<string> PickAsync(IBrowserDevice browser, string url)
    {
        var address = Address(url);
        if (!browser.IsOpen)
        {
            // Edge is the browser Windows already has, so the editor never has to fetch one.
            browser.Open("edge", address, headless: false);
        }
        else if (address.Length > 0 && !SamePage(browser.Url, address))
        {
            // A step that names a page is about that page, so a browser sitting somewhere else is
            // sent there — the same place the macro will be looking when this step runs.
            browser.GoTo(address);
        }

        var hint = Strings.Get("Add.BrowserPickBanner");

        // Letting go of the pin is what lets the page come up in front instead of behind us; the
        // device does the rest of the work of putting it there.
        using var unpinned = Unpin();
        return await Task.Run(() => browser.Pick(hint, PickTimeoutMs));
    }

    /// <summary>
    /// Outlines the element a selector names on the page a macro has open, which is what the
    /// "test" button next to the picker asks for. False when there is no page to look on or nothing
    /// there matches.
    /// </summary>
    /// <remarks>
    /// It works on the same page the pick does, so what is checked is what a run would find. The pin
    /// comes off while the frame is up, so the page can come in front of this program's own windows
    /// instead of staying behind the dialog the button was pressed in.
    /// </remarks>
    public static async Task<bool> HighlightAsync(IBrowserDevice browser, string selector)
    {
        if (!browser.IsOpen)
        {
            return false;
        }

        using var unpinned = Unpin();
        return await Task.Run(() => browser.Highlight(selector));
    }

    /// <summary>
    /// The address to go to: the step's own when it names a page, and none when it does not,
    /// because a field holding a variable name is not somewhere to browse to, and a blank address
    /// is what tells the browser to open without a page rather than go somewhere wrong.
    /// </summary>
    private static string Address(string url)
    {
        var trimmed = url.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : string.Empty;
    }

    /// <summary>
    /// Whether the page is already at an address, so a pick does not send it away from what the
    /// person is looking at over a difference as small as a trailing slash.
    /// </summary>
    private static bool SamePage(string current, string wanted) =>
        string.Equals(current.Trim().TrimEnd('/'), wanted.Trim().TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lets go of the pin on this program's windows while somebody is on the page: Windows keeps a
    /// pinned window above every window that is not pinned, and the browser is another program, so
    /// the pin cannot travel to it the way it travels to our own dialogs. The page itself is
    /// brought in front by the device, which is what keeps our windows from covering it.
    /// </summary>
    /// <remarks>
    /// Nothing else is done to the windows, and in particular they are not minimised: this program
    /// treats its own window being minimised as "go to the notification area", so a minimised main
    /// window hides itself and stays hidden — everything the person was working in disappears.
    /// </remarks>
    private static IDisposable Unpin()
    {
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.Windows.Where(window => window.Topmost).ToArray()
            ?? [];

        foreach (var window in windows)
        {
            window.Topmost = false;
        }

        return new Pinned(windows);
    }

    /// <summary>
    /// Takes the pin back up when the page is done with. A window that was closed while the pick
    /// was outstanding is skipped: the person can dismiss the dialog without waiting for it.
    /// </summary>
    private sealed class Pinned(IReadOnlyList<Window> windows) : IDisposable
    {
        public void Dispose()
        {
            foreach (var window in windows)
            {
                if (window.IsVisible)
                {
                    window.Topmost = true;
                }
            }
        }
    }
}
