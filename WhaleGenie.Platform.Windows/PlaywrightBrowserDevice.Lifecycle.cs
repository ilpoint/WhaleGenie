using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Platform.Windows;

// Closing the browser and letting it go, and the tabs the device keeps track of.
public sealed partial class PlaywrightBrowserDevice
{
    public void Close()
    {
        if (_page is null && _browser is null && _context is null)
        {
            return;
        }

        Wait(async () =>
        {
            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
            else if (_context is not null)
            {
                // A browser opened on a profile is the context: closing it is what closes the
                // window, and the profile itself is left where it lies for the next run to find.
                await _context.CloseAsync();
            }
        });

        _page = null;
        _browser = null;
        _context = null;
        _headless = false;
        _listening = false;
        _tabs.Clear();

        // The files of a browser that is gone are not files any later step can ask for: the driver
        // has already thrown the ones nobody saved away.
        lock (_caught)
        {
            _caught.Clear();
            _taken.Clear();
        }
    }

    public void Dispose()
    {
        Close();
        _playwright?.Dispose();
        _playwright = null;
        _arrivals.Dispose();
    }

    /// <summary>The browser the step named, defaulting to the one Playwright always has.</summary>
    private IBrowserType Launcher(string browser)
    {
        var playwright = _playwright
            ?? throw new DeviceActionException(RunKeys.BrowserNotOpen);
        return browser.Trim().ToLowerInvariant() switch
        {
            "firefox" => playwright.Firefox,
            "webkit" => playwright.Webkit,
            _ => playwright.Chromium,
        };
    }

    /// <summary>
    /// The page this device is working on, or null when there is none to work on. The page last
    /// written down can be gone while the browser still has tabs — the site took it away, or the
    /// macro closed it — and then the newest tab still open is taken up, because that is where the
    /// person is looking and what a macro means to go on with.
    /// </summary>
    private IPage? Current()
    {
        if (_page is null)
        {
            return null;
        }

        if (_page.IsClosed)
        {
            _page = Tabs().LastOrDefault(tab => !tab.IsClosed);
        }

        return _page;
    }

    /// <summary>The open page, or a failure that says a browser has to be opened first.</summary>
    private IPage Page() => Current() ?? throw new DeviceActionException(RunKeys.BrowserNotOpen);

    /// <summary>
    /// One key, or one chord, in the spelling the page's own keyboard knows it by, or null when it
    /// has nothing of that name. A chord is written the way a macro writes one, with "+" between
    /// the keys, and is turned into the page's own spelling of the same thing.
    /// </summary>
    private static string? PageKeys(string key)
    {
        var keys = new List<string>();
        foreach (var part in key.Split('+',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A macro's own name for a key is turned into the canonical one first, so that every
            // spelling the keyboard picker offers ("Ctrl", "control", "左Ctrl") arrives here as
            // the same thing.
            if (SharpKeys.ResolveCode(part) is not { } code)
            {
                return null;
            }

            var written = SharpKeys.Name(code);
            keys.Add(PageKeyNames.TryGetValue(written, out var page) ? page : Numbered(written));
        }

        return keys.Count == 0 ? null : string.Join('+', keys);
    }

    /// <summary>The number pad, which one side spells "NumPad5" and the other "Numpad5".</summary>
    private static string Numbered(string written)
        => written.StartsWith("NumPad", StringComparison.Ordinal)
            ? "Numpad" + written["NumPad".Length..]
            : written;

    /// <summary>What the page calls the state a step is waiting for.</summary>
    private static WaitForSelectorState WaitState(ElementWait state) => state switch
    {
        ElementWait.Hidden => WaitForSelectorState.Hidden,
        ElementWait.Attached => WaitForSelectorState.Attached,
        ElementWait.Detached => WaitForSelectorState.Detached,
        _ => WaitForSelectorState.Visible,
    };

    /// <summary>
    /// The tabs this browser has, oldest first, with any that turned up since the last look at the
    /// end and the ones that are gone dropped.
    /// </summary>
    private List<IPage> Tabs()
    {
        if (_page is not null)
        {
            foreach (var page in _page.Context.Pages)
            {
                if (!_tabs.Contains(page))
                {
                    _tabs.Add(page);
                }
            }

            _tabs.RemoveAll(page => page.IsClosed);
        }

        return [.. _tabs];
    }

    /// <summary>
    /// The first tab whose title, or whose address, holds this text; null when none does. A tab
    /// that went away while it was being looked at is passed over rather than failing the step.
    /// </summary>
    private static async Task<IPage?> FirstTabAsync(List<IPage> tabs, string match, bool byTitle)
    {
        foreach (var tab in tabs)
        {
            string text;
            try
            {
                text = byTitle ? await tab.TitleAsync() : tab.Url;
            }
            catch (PlaywrightException)
            {
                continue;
            }

            if (text.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private static void Wait(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

    private static T Wait<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
