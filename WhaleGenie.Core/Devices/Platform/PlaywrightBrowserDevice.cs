using System.Globalization;
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

    /// <summary>
    /// How long a check waits for the element it was handed. It is a question about a page that is
    /// already open, so waiting the half minute a macro's own step waits would leave the person
    /// staring at a dialog for a selector that is simply not there.
    /// </summary>
    private const float HighlightTimeoutMs = 3000;

    /// <summary>
    /// How long a switch to the newest tab waits for one to turn up. A click that opens a tab has
    /// answered by the time it comes back, but the tab itself reaches Playwright a moment later —
    /// tens of milliseconds on this machine — and a site that opens its tab after fetching
    /// something takes longer still.
    /// </summary>
    /// <remarks>
    /// A check sets it: waiting the whole way to see what happens when nothing opens would be five
    /// seconds of the test run for an answer that is already known.
    /// </remarks>
    internal static TimeSpan NewTabWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often that wait looks whether the tab has turned up.</summary>
    private const int NewTabIntervalMs = 50;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    /// <summary>
    /// The tabs this browser has, oldest first, as they were written down when they turned up.
    /// </summary>
    /// <remarks>
    /// Playwright hands over the pages that are open, but "which of these is the newest" is what a
    /// macro needs after a click opened a tab, and the order of that list is not something
    /// Playwright promises. Written down here as they turn up, the last one added is the newest
    /// whatever order they come in; the closed ones are dropped on every look.
    /// </remarks>
    private readonly List<IPage> _tabs = [];

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
    /// <remarks>
    /// A tab that has gone is not the end of the browser while another tab is still open — that is
    /// exactly where a click which opens a tab and lets the old one go leaves it — so what is
    /// looked at is the tabs, not only the page last written down.
    /// </remarks>
    public bool IsOpen => Current() is not null;

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

    public void GoTo(string url) => OnPage(async () => await Page().GotoAsync(url));

    public void Click(string selector) => OnPage(async () => await Page().ClickAsync(selector));

    public void Fill(string selector, string text)
        => OnPage(async () => await Page().FillAsync(selector, text));

    public string Text(string selector) => OnPage(async () =>
    {
        var page = Page();
        return string.IsNullOrWhiteSpace(selector)
            ? await page.InnerTextAsync("body")
            : await page.InnerTextAsync(selector);
    });

    public void SwitchTab(TabChoice choice, int index, string match)
        => OnPage(async () =>
        {
            // Looking a tab up by title or address is a question, and a step that forgot to say
            // what to look for would otherwise answer itself with the first tab there is.
            if (choice is TabChoice.Title or TabChoice.Address && match.Trim().Length == 0)
            {
                throw new DeviceActionException("Run.BrowserTabNeedsText");
            }

            var tabs = Tabs();
            var wanted = choice switch
            {
                TabChoice.Newest => await NewestTabAsync(),
                TabChoice.Index => index >= 1 && index <= tabs.Count ? tabs[index - 1] : null,
                TabChoice.Title => await FirstTabAsync(tabs, match, byTitle: true),
                _ => await FirstTabAsync(tabs, match, byTitle: false),
            };

            if (wanted is null)
            {
                throw new DeviceActionException("Run.BrowserNoTab",
                    choice is TabChoice.Index
                        ? index.ToString(CultureInfo.InvariantCulture)
                        : match);
            }

            _page = wanted;

            // A browser with a window shows the tab it is working on, the way clicking one does.
            await wanted.BringToFrontAsync();
        });

    /// <summary>
    /// The tab that appeared most recently, which is the one a click opened.
    /// </summary>
    /// <remarks>
    /// The tab a click opens reaches Playwright a moment after the click is answered, so a macro
    /// asking for "the newest" straight afterwards would be handed the tab it is already on — and
    /// every step after it would work on the wrong page. Nothing turning up is refused rather than
    /// passed over: the step asked for a tab that is not there, and quietly staying put is the one
    /// answer nobody can see.
    /// </remarks>
    private async Task<IPage?> NewestTabAsync()
    {
        var current = Page();
        var deadline = DateTime.UtcNow + NewTabWait;
        while (true)
        {
            var newest = Tabs().LastOrDefault();
            if (newest is not null && !ReferenceEquals(newest, current))
            {
                return newest;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new DeviceActionException("Run.BrowserNoNewTab");
            }

            await Task.Delay(NewTabIntervalMs);
        }
    }

    public void CloseTab() => OnPage(async () =>
    {
        await Page().CloseAsync();

        // The person is left on the tab next to the one they closed, which is the newest of the
        // ones still there; with none left there is no page, and the steps after this say so.
        _page = Tabs().LastOrDefault(tab => !tab.IsClosed);
    });

    /// <summary>
    /// Runs one of the page actions, and puts the page into the failure if it cannot be done.
    /// </summary>
    /// <remarks>
    /// "The element was not found" says very little on its own — and it is the failure that turns up
    /// most, because a page that has changed since the element was picked no longer has it. With the
    /// page it was looked for on, it usually says the whole thing: the macro was sent to the sign-in
    /// page, or somewhere else entirely, and the selector was never the problem.
    /// </remarks>
    private void OnPage(Func<Task> work)
    {
        try
        {
            Wait(work);
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Failed(failure);
        }
    }

    private T OnPage<T>(Func<Task<T>> work)
    {
        try
        {
            return Wait(work);
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Failed(failure);
        }
    }

    private DeviceActionException Failed(Exception failure)
    {
        string where;
        try
        {
            where = Url;
        }
        catch (PlaywrightException)
        {
            // The window is gone, which is what the failure is about; it has nothing to add.
            where = string.Empty;
        }

        return new DeviceActionException(
            "Run.BrowserFailed",
            where.Length == 0 ? failure.Message : where + " — " + failure.Message);
    }

    /// <summary>
    /// Outlines an element of the page for a moment, so a selector can be checked without running
    /// the step it was written for. False when nothing on the page matches, which is an answer
    /// rather than a failure: the question was "did that take the right element?".
    /// </summary>
    /// <remarks>
    /// The lookup goes through a locator rather than a plain <c>querySelectorAll</c> because a
    /// selector picked inside a frame carries the way in to that frame with it, and only Playwright
    /// walks one. The element is then scrolled into view and handed to a script of its own
    /// document, so the frame lands on screen where the person can see it.
    /// </remarks>
    public bool Highlight(string selector)
    {
        if (!IsOpen || string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        try
        {
            Wait(async () =>
            {
                var page = Page();
                await page.BringToFrontAsync();

                var element = page.Locator(selector).First;
                await element.ScrollIntoViewIfNeededAsync();
                await element.EvaluateAsync<bool>(BrowserPickerScript.Flash, null,
                    new LocatorEvaluateOptions { Timeout = HighlightTimeoutMs });
            });

            return true;
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            return false;
        }
    }

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

            if (answer.Task.Result is not { Length: > 0 } picked)
            {
                return string.Empty;
            }

            // The person picked in whichever tab they were looking at, and that is the tab they
            // mean the macro to work on. Left on the tab it was on before, every step after this
            // would aim at a page holding none of what they just pointed at — or, on a site whose
            // pages look alike, at the element over there that happens to match.
            if (_answeredFrame is { } answered && !ReferenceEquals(answered.Page, page))
            {
                _page = answered.Page;
            }

            return InFrame(Page(), _answeredFrame, picked);
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
        _tabs.Clear();
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
    private IPage Page() => Current() ?? throw new DeviceActionException("Run.BrowserNotOpen");

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
