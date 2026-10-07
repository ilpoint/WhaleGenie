using Microsoft.Playwright;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// A browser driven through Playwright. Edge is used by default, because Windows already has it:
/// a macro that just wants to fill a form in does not have to fetch a browser first. The engines
/// Playwright fetches for itself are still offered, and the device says whether they are there
/// before a macro tries one, so the user gets the command that installs it rather than a failure
/// from somewhere inside the driver.
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

    /// <summary>Handed the selector the person clicked, by the page's own script.</summary>
    private TaskCompletionSource<string>? _picked;

    /// <summary>Whether the picker's bindings and init script have been installed on this page.</summary>
    private bool _listening;

    /// <summary>
    /// The browsers Playwright fetches land under the profile's cache folder; the one being asked
    /// for having a folder there is what "already downloaded" means, and it is checked without
    /// starting anything. A channel browser — Edge, Chrome — is the machine's own, so it needs no
    /// download and is only checked for being present.
    /// </summary>
    public bool Ready(string browser)
    {
        return Channel(browser).Length > 0
            ? SystemBrowser(Channel(browser)) is not null
            : Directory.Exists(Downloaded(browser));
    }

    /// <summary>Where Playwright keeps the copy of an engine it downloaded.</summary>
    private static string Downloaded(string browser) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ms-playwright",
        browser.Trim().ToLowerInvariant() switch
        {
            "firefox" => "firefox",
            "webkit" => "webkit",
            _ => "chromium",
        });

    /// <summary>
    /// The file a system browser is started from, or null when this machine has no such browser.
    /// The two places Windows and the installers put them are both looked at, because a 32-bit
    /// Windows keeps even a 64-bit Edge under Program Files (x86).
    /// </summary>
    private static string? SystemBrowser(string channel)
    {
        var name = channel == "chrome" ? "Google\\Chrome" : "Microsoft\\Edge";
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            var file = Path.Combine(folder, name, "Application",
                channel == "chrome" ? "chrome.exe" : "msedge.exe");
            if (File.Exists(file))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>The channel a browser name asks for, empty when it means a downloaded engine.</summary>
    private static string Channel(string browser) => browser.Trim().ToLowerInvariant() switch
    {
        "edge" => "msedge",
        "chrome" => "chrome",
        _ => string.Empty,
    };

    public string InstallHint =>
        "This browser is not on this machine yet. Edge, the browser Windows already has, needs no "
        + "installing at all — pick it instead. A downloaded engine is fetched once with "
        + "\"playwright.ps1 install chromium\" (or firefox / webkit), run from the folder "
        + "WhaleGenie was installed in.";

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
            var channel = Channel(browser);
            _browser = await Launcher(browser).LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = channel.Length == 0 ? null : channel,
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

    public string Pick(string hint, int timeoutMs)
    {
        var page = Page();

        // The answer arrives on whichever thread Playwright dispatches on, so the wait below has
        // to be able to let go of the caller while it is still outstanding.
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _picked = answer;
        try
        {
            Wait(async () =>
            {
                if (!_listening)
                {
                    // The answer binding is what the page's script answers through; the "still
                    // wanted" binding is what the init script asks before putting the picker back
                    // after a navigation, since an init script cannot be removed once added.
                    await page.ExposeFunctionAsync(
                        "__whalegeniePicked", (string? selector) => _picked?.TrySetResult(selector ?? string.Empty));
                    await page.ExposeFunctionAsync("__whalegenieListening", () => _picked is not null);
                    await page.AddInitScriptAsync(BrowserPickerScript.InitScript(hint));
                    _listening = true;
                }

                await page.EvaluateAsync(BrowserPickerScript.Source, new { hint });
            });

            return answer.Task.Wait(Math.Max(1000, timeoutMs)) ? answer.Task.Result : string.Empty;
        }
        finally
        {
            _picked = null;
            TryStopPicking(page);
        }
    }

    /// <summary>
    /// Takes the picker off the page when the wait ends first — a person who never clicked should
    /// not be left with a banner and a highlight following the pointer around the page.
    /// </summary>
    private static void TryStopPicking(IPage page)
    {
        try
        {
            Wait(async () => await page.EvaluateAsync(
                "() => { if (window.__whalegeniePicking) {"
                + " window.__whalegeniePicking = false;"
                + " for (const node of document.querySelectorAll('[data-whalegenie-picker]')) { node.remove(); } } }"));
        }
        catch (PlaywrightException)
        {
            // The page went away while picking, which is one of the ways picking can end.
        }
    }

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
        _listening = false;
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
