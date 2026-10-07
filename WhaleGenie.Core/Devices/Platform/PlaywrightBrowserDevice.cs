using Microsoft.Playwright;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// A browser driven through Playwright. The browsers themselves are a separate download, so the
/// device says whether they are there before a macro tries to use them: the message the user gets
/// is the command that puts them there, not a failure from somewhere inside the driver.
///
/// Playwright's own calls are asynchronous and come back on the context they were started from,
/// which is the interface thread while a macro is running. Every call is therefore handed to the
/// thread pool and waited on there, the same way the VIIPER client is, so a blocking wait can
/// never keep the answer from arriving.
/// </summary>
public sealed class PlaywrightBrowserDevice : IBrowserDevice, IDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    public bool Ready
    {
        get
        {
            // The driver ships its browsers under the profile's cache folder; the folder being
            // there is what "already downloaded" means, and it is checked without starting one.
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ms-playwright");
            return Directory.Exists(root)
                && Directory.EnumerateDirectories(root).Any(name =>
                    Path.GetFileName(name).StartsWith("chromium", StringComparison.OrdinalIgnoreCase));
        }
    }

    public string InstallHint =>
        "The browsers Playwright drives are not on this machine yet. Run "
        + "\"playwright.ps1 install chromium\" once, from the folder WhaleGenie was installed in, "
        + "and the browser actions work from then on.";

    public bool IsOpen => _page is not null;

    public string Url => _page?.Url ?? string.Empty;

    public void Open(string browser, string url, bool headless)
    {
        // A macro that opens a second browser means to keep using it, so the one before it is
        // let go of rather than left running behind the new page.
        Close();

        Wait(async () =>
        {
            _playwright ??= await Playwright.CreateAsync();
            _browser = await Launcher(browser).LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = headless,
            });
            _page = await _browser.NewPageAsync();
            if (!string.IsNullOrWhiteSpace(url))
            {
                await _page.GotoAsync(url);
            }
        });
    }

    public void GoTo(string url) => Wait(async () => await Page().GotoAsync(url));

    public void Click(string selector) => Wait(async () => await Page().ClickAsync(selector));

    public void Fill(string selector, string text)
        => Wait(async () => await Page().FillAsync(selector, text));

    public string Text(string selector) => Wait(async () =>
    {
        var page = Page();
        return string.IsNullOrWhiteSpace(selector)
            ? await page.InnerTextAsync("body")
            : await page.InnerTextAsync(selector);
    });

    public void Close()
    {
        if (_page is null && _browser is null)
        {
            return;
        }

        Wait(async () =>
        {
            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
        });

        _page = null;
        _browser = null;
    }

    public void Dispose()
    {
        Close();
        _playwright?.Dispose();
        _playwright = null;
    }

    /// <summary>The browser the step named, defaulting to the one Playwright always has.</summary>
    private IBrowserType Launcher(string browser)
    {
        var playwright = _playwright
            ?? throw new DeviceActionException("Run.BrowserNotOpen");
        return browser.Trim().ToLowerInvariant() switch
        {
            "firefox" => playwright.Firefox,
            "webkit" => playwright.Webkit,
            _ => playwright.Chromium,
        };
    }

    /// <summary>The open page, or a failure that says a browser has to be opened first.</summary>
    private IPage Page() => _page ?? throw new DeviceActionException("Run.BrowserNotOpen");

    private static void Wait(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

    private static T Wait<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
