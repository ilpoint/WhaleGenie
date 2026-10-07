using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// The page elements are picked off. When a macro already has a browser open — the one the step is
/// about — that is the page picked on; a browser of this picker's own is only opened when the
/// program has none, and that one is closed with the dialog that opened it.
/// </summary>
/// <remarks>
/// The picking itself is the page's own work: the device hands the page a script that outlines
/// whatever the pointer is over and answers with a selector when something is clicked. Nothing
/// here reads the screen, which is what makes this different from the UI Automation picker next to
/// it — a page drawn by a browser is not a window full of controls.
/// </remarks>
internal static class BrowserPicker
{
    /// <summary>How long a pick waits for a click before giving up.</summary>
    private const int PickTimeoutMs = 120_000;

    /// <summary>The browser this picker opened for itself, when the program had none open.</summary>
    private static PlaywrightBrowserDevice? _own;

    /// <summary>
    /// Goes to the page the step is about and answers with the selector of the element that was
    /// clicked on it, or an empty string when the person gave up.
    /// </summary>
    public static async Task<string> PickAsync(string url)
    {
        // The page a macro has open is the page the step is about, so it is picked on rather than
        // opened again: a browser of this picker's own would be a second, empty one, without the
        // pages the macro brought up and without the logins that went with them.
        var browser = _own ?? PlaywrightBrowserDevice.OpenPage();
        if (browser is null)
        {
            browser = _own = new PlaywrightBrowserDevice();
        }

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

        using var aside = StepAside();
        return await Task.Run(() => browser.Pick(hint, PickTimeoutMs));
    }

    /// <summary>
    /// Closes the page this picker opened for itself, which the dialog does when it goes away. A
    /// page the program already had open is left standing: it belongs to the macro that opened it.
    /// </summary>
    public static void Close()
    {
        _own?.Dispose();
        _own = null;
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
    /// Gets this program out of the way while somebody is on the page. Two things are in the way:
    /// the pin, which a window belonging to another program cannot be given, and the windows
    /// themselves, which sit on top of the very elements being pointed at.
    /// </summary>
    private static IDisposable StepAside()
    {
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.Windows.Where(window => window.IsVisible).ToArray()
            ?? [];

        var active = windows.FirstOrDefault(window => window.IsActive);
        var away = windows
            .Select(window => new SteppedAside(window, window.Topmost, window.WindowState))
            .ToArray();

        foreach (var window in away)
        {
            window.Window.Topmost = false;
            try
            {
                window.Window.WindowState = WindowState.Minimized;
            }
            catch (Exception)
            {
                // A window that will not be minimised is no reason to refuse the pick; it simply
                // stays where it is.
            }
        }

        return new OutOfTheWay(away, active);
    }

    /// <summary>One window that was moved out of the way, and what it looked like before.</summary>
    private sealed record SteppedAside(Window Window, bool Pinned, WindowState State);

    /// <summary>
    /// Puts the windows back when the page is done with. A window that was closed while the pick was
    /// outstanding is skipped: the person can dismiss the dialog without waiting for it.
    /// </summary>
    private sealed class OutOfTheWay(IReadOnlyList<SteppedAside> windows, Window? active) : IDisposable
    {
        public void Dispose()
        {
            foreach (var entry in windows)
            {
                if (!entry.Window.IsVisible)
                {
                    continue;
                }

                entry.Window.WindowState = entry.State;
                entry.Window.Topmost = entry.Pinned;
            }

            // The page was in front; the dialog the person was on goes back in front of it.
            if (active is { IsVisible: true })
            {
                active.Activate();
            }
        }
    }
}
