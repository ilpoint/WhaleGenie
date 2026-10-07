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
    /// <summary>
    /// The browsers this program has a page open in, oldest first.
    /// </summary>
    /// <remarks>
    /// A macro and the editor's element picker reach the browser through separate device layers,
    /// but they are the same person looking at the same page. So the picker asks here first and
    /// works on the page a macro opened, instead of starting a second browser with none of the
    /// pages, and none of the logins, the macro had brought up.
    /// </remarks>
    private static readonly List<PlaywrightBrowserDevice> Opened = [];

    /// <summary>Guards the list, which a macro's own thread and the editor both reach.</summary>
    private static readonly object OpenedGate = new();

    /// <summary>
    /// Playwright's own step from an <c>iframe</c> element down into the document inside it. Chained
    /// selectors do not take it on their own — without this, a click aimed at a frame waits for an
    /// element of that name in the top document and times out.
    /// </summary>
    private const string FrameStep = "internal:control=enter-frame";

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    /// <summary>Handed the selector the person clicked, by the page's own script.</summary>
    private TaskCompletionSource<string>? _picked;

    /// <summary>Handed the frame that answered, so the frame can be written into the selector.</summary>
    private IFrame? _answeredFrame;

    /// <summary>Whether the picker's bindings and init script have been installed on this page.</summary>
    private bool _listening;

    /// <summary>Whether the browser was opened without a window.</summary>
    private bool _headless;

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

    /// <summary>
    /// True while there is a page to drive. A page the person closed themselves is not one: the
    /// next step that asks for the browser has to open it again rather than be handed a window
    /// that is no longer there.
    /// </summary>
    public bool IsOpen => _page is { IsClosed: false };

    /// <summary>True when the browser has no window, so there is nothing on screen to work on.</summary>
    public bool IsHeadless => _headless;

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
            _headless = headless;
            _page = await _browser.NewPageAsync();
            Remember();
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
        _answeredFrame = null;
        try
        {
            Wait(async () =>
            {
                // The page is what the person is about to point at, and it was opened from behind
                // this program's own windows, so it is brought in front of them — otherwise the
                // editor the step is being written in covers the very elements to be clicked.
                try
                {
                    await page.BringToFrontAsync();
                }
                catch (PlaywrightException)
                {
                    // A window that will not come forward is no reason to refuse the pick.
                }

                if (!_listening)
                {
                    // Everything here sits on the context rather than on the page, because picking
                    // has to reach every tab and every frame of them: the answer binding is the
                    // only thing that says which frame answered (and the frame is part of the
                    // selector), and a picker that only knew the first page would leave every
                    // other tab dead. The "still wanted" binding is what the init script asks
                    // before putting the picker back after a navigation, since an init script
                    // cannot be removed once added.
                    await page.Context.ExposeBindingAsync(
                        "__whalegeniePicked",
                        (BindingSource source, string? selector) =>
                        {
                            _answeredFrame = source.Frame;
                            _picked?.TrySetResult(selector ?? string.Empty);
                        });
                    await page.Context.ExposeBindingAsync(
                        "__whalegenieListening", (BindingSource _) => _picked is not null);
                    await page.Context.AddInitScriptAsync(BrowserPickerScript.InitScript(hint));
                    _listening = true;
                }

                // Every tab, and every frame of every tab: a page built out of frames is still one
                // page to the person looking at it, and a click that landed anywhere the picker had
                // not been put would go straight through to the page instead of being taken.
                foreach (var frame in Frames(page))
                {
                    try
                    {
                        await frame.EvaluateAsync(BrowserPickerScript.Source, new { hint });
                    }
                    catch (PlaywrightException) when (!ReferenceEquals(frame, page.MainFrame))
                    {
                        // A frame or a tab can go away between being listed and being armed.
                    }
                }
            });

            if (!answer.Task.Wait(Math.Max(1000, timeoutMs)))
            {
                return string.Empty;
            }

            return answer.Task.Result is { Length: > 0 } picked
                ? InFrame(page, _answeredFrame, picked)
                : string.Empty;
        }
        finally
        {
            _picked = null;
            _answeredFrame = null;
            TryStopPicking(page);
        }
    }

    /// <summary>
    /// The selector with the frames it sits inside written in front of it, because the click the
    /// macro makes later is aimed at the whole page rather than at the frame the person pointed in.
    /// Playwright hands that click on to the next frame at each written-in step, so one string still
    /// names the element.
    /// </summary>
    private static string InFrame(IPage page, IFrame? frame, string selector)
    {
        if (frame is null || ReferenceEquals(frame, page.MainFrame))
        {
            return selector;
        }

        try
        {
            return Wait(async () => await WayInAsync(frame) + selector);
        }
        catch (PlaywrightException)
        {
            // The frame went away between the pick and writing it down; the bare selector is the
            // most that can still be said about the element.
            return selector;
        }
    }

    /// <summary>The way in to a frame: the selector of each frame on the way, outermost first.</summary>
    private static async Task<string> WayInAsync(IFrame frame)
    {
        var way = new List<string>();
        for (var at = frame; at.ParentFrame is not null; at = at.ParentFrame)
        {
            if (await at.FrameElementAsync() is not { } element)
            {
                break;
            }

            way.Insert(0, await element.EvaluateAsync<string>(BrowserPickerScript.FramePath));
        }

        return string.Concat(way.Select(selector => selector + " >> " + FrameStep + " >> "));
    }

    /// <summary>
    /// Every frame the picker has to reach: each frame of each tab of the browser. Tabs are opened
    /// by the person as much as by the macro, and the one being looked at is not always the first.
    /// </summary>
    private static IEnumerable<IFrame> Frames(IPage page)
        => page.Context.Pages.SelectMany(tab => tab.Frames);

    /// <summary>
    /// Takes the picker off the page when the wait ends first — a person who never clicked should
    /// not be left with a banner and a highlight following the pointer around the page.
    /// </summary>
    private static void TryStopPicking(IPage page)
    {
        foreach (var frame in Frames(page))
        {
            try
            {
                Wait(async () => await frame.EvaluateAsync(
                    "() => { if (typeof window.__whalegenieStop === 'function') { window.__whalegenieStop(); } }"));
            }
            catch (PlaywrightException)
            {
                // The page went away while picking, which is one of the ways picking can end.
            }
        }
    }

    public void Close()
    {
        Forget();
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
        _headless = false;
        _listening = false;
    }

    /// <summary>
    /// The page this program already has open and shown, for whoever wants to work on it rather
    /// than start a browser of their own. A browser opened without a window is not one of these:
    /// there is nothing on screen to work on.
    /// </summary>
    public static PlaywrightBrowserDevice? OpenPage()
    {
        lock (OpenedGate)
        {
            return Opened.LastOrDefault(device => device.IsOpen && !device.IsHeadless);
        }
    }

    /// <summary>Adds this device to the ones with a page open, newest last.</summary>
    private void Remember()
    {
        lock (OpenedGate)
        {
            Opened.Remove(this);
            Opened.Add(this);
        }
    }

    /// <summary>Takes this device off the ones with a page open.</summary>
    private void Forget()
    {
        lock (OpenedGate)
        {
            Opened.Remove(this);
        }
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
