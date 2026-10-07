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
/// The browser the editor keeps open while elements are picked off a page. It is opened on the
/// first pick and left standing for the next one, so a page that was navigated to — and logged
/// into — is not fetched again for every field, and it is closed with the dialog that opened it.
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

    /// <summary>The one browser the editor drives while the dialog is up.</summary>
    private static PlaywrightBrowserDevice? _browser;

    /// <summary>
    /// Opens the page when there is none yet — at the address the step is about when it has one —
    /// and answers with the selector of the element that was clicked, or an empty string when the
    /// person gave up.
    /// </summary>
    public static async Task<string> PickAsync(string url)
    {
        var browser = _browser ??= new PlaywrightBrowserDevice();
        if (!browser.IsOpen)
        {
            // Edge is the browser Windows already has, so the editor never has to fetch one.
            browser.Open("edge", Address(url), headless: false);
        }

        var hint = Strings.Get("Add.BrowserPickBanner");

        // Windows keeps a pinned window above every window that is not pinned, and the browser is
        // another program, so the pin cannot travel to it the way it travels to our own dialogs.
        // It is let go of while the person is on the page, and taken back up afterwards.
        using var unpinned = Unpin();
        return await Task.Run(() => browser.Pick(hint, PickTimeoutMs));
    }

    /// <summary>Closes the browsing page, which the dialog does when it goes away.</summary>
    public static void Close()
    {
        _browser?.Dispose();
        _browser = null;
    }

    /// <summary>
    /// The address to open at: the step's own when it is one, and a blank page otherwise, because
    /// a field holding a variable name is not somewhere to browse to.
    /// </summary>
    private static string Address(string url)
    {
        var trimmed = url.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : "about:blank";
    }

    /// <summary>Lets go of the pin on this program's windows while somebody is on the page.</summary>
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
