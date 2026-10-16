using System.Globalization;
using System.Text.Json;
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
    /// How a macro writes the way in to a frame: the selector of the frame element itself, then the
    /// rest of the line. A line may say it several times over, outermost frame first.
    /// </summary>
    private const string FrameStep = "frame=";

    /// <summary>
    /// What is written between one part of a line and the next. Playwright reads it as a chain of
    /// its own, which is why a line a macro wrote by hand keeps whichever chain it asked for.
    /// </summary>
    private const string Chain = " >> ";

    /// <summary>The four ways of naming an element that are written rather than read as a selector.</summary>
    private static readonly string[] Semantic = ["label=", "placeholder=", "alt=", "title="];

    /// <summary>
    /// How long a check waits for the element it was handed. It is a question about a page that is
    /// already open, so waiting the half minute a macro's own step waits would leave the person
    /// staring at a dialog for a selector that is simply not there.
    /// </summary>
    private const float HighlightTimeoutMs = 3000;

    /// <summary>
    /// How often a target written as several selectors is tried while none of them has found
    /// anything yet. It is a look at a page that is already in memory, so it costs next to nothing;
    /// waiting a moment between two looks is what keeps a page that never shows the element from
    /// being asked hundreds of times a second.
    /// </summary>
    private const int CandidateIntervalMs = 100;

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

    /// <summary>
    /// How long a wait for an element waits when the step hands over no length of its own. It is
    /// Playwright's own default, kept here so a wait written into the device rather than into a step
    /// does not quietly become a wait of no length at all.
    /// </summary>
    private const int DefaultWaitMs = 30000;

    /// <summary>
    /// The keys whose spelling differs between a macro and the page's own keyboard. Everything else
    /// a macro can write — the letters, the digits, the function keys, and the keys both sides spell
    /// the same way ("Enter", "PageUp") — is handed over as it stands, so only the differences are
    /// written down here.
    /// </summary>
    private static readonly Dictionary<string, string> PageKeyNames = new(StringComparer.Ordinal)
    {
        ["Ctrl"] = "Control",
        ["Win"] = "Meta",
        ["Esc"] = "Escape",
        ["Up"] = "ArrowUp",
        ["Down"] = "ArrowDown",
        ["Left"] = "ArrowLeft",
        ["Right"] = "ArrowRight",
        ["-"] = "Minus",
        ["="] = "Equal",
        [","] = "Comma",
        ["."] = "Period",
        ["/"] = "Slash",
        [";"] = "Semicolon",
        ["'"] = "Quote",
        ["`"] = "Backquote",
        ["["] = "BracketLeft",
        ["]"] = "BracketRight",
        ["\\"] = "Backslash",
    };

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
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
        + "installing at all, so pick it instead. A downloaded engine is fetched once with "
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

    public void Open(string browser, string url, bool headless, string profile)
    {
        // The folder is worked out first, so a step that named a profile nobody can have fails
        // before the browser it was using has been let go of.
        var folder = profile.Trim();
        var where = folder.Length == 0 ? string.Empty : ProfileFolder(browser, folder);

        // A macro that opens a second browser means to keep using it, so the one before it is
        // let go of rather than left running behind the new page.
        Close();

        Wait(async () =>
        {
            _playwright ??= await Playwright.CreateAsync();
            var channel = Channel(browser);
            var launcher = Launcher(browser);

            // A profile is a browser of the person's own to come back to: the site it signed in to
            // remembers them next time, which is what makes a macro that logs in once worth writing.
            // With none, every run starts from nothing, which is what a macro that should leave
            // somebody's own session alone wants.
            if (where.Length > 0)
            {
                Directory.CreateDirectory(where);
                _context = await launcher.LaunchPersistentContextAsync(where,
                    new BrowserTypeLaunchPersistentContextOptions
                    {
                        Channel = channel.Length == 0 ? null : channel,
                        Headless = headless,
                    });
            }
            else
            {
                _browser = await launcher.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Channel = channel.Length == 0 ? null : channel,
                    Headless = headless,
                });
                _context = await _browser.NewContextAsync();
            }

            _headless = headless;

            // A browser opened on a profile comes up with the tab it was left on, which is the page
            // the macro means to work on; the first run of all has none and is given one.
            _page = _context.Pages.FirstOrDefault() ?? await _context.NewPageAsync();
            if (!string.IsNullOrWhiteSpace(url))
            {
                await _page.GotoAsync(url);
            }
        });
    }

    /// <summary>
    /// Where a profile is kept: beside the program, the way the packages a script asks for are, so
    /// a copy carried about keeps its own sign-ins; a copy installed where it may not write keeps
    /// them under the user's own folder instead.
    /// </summary>
    /// <remarks>
    /// The engine is part of the path because two browsers cannot share one profile — each keeps
    /// its own shape of it — so the same name means a separate browser under each.
    /// </remarks>
    private static string ProfileFolder(string browser, string profile)
    {
        if (profile is "." or ".."
            || profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || profile.EndsWith('.')
            || profile.EndsWith(' '))
        {
            throw new DeviceActionException("Run.BrowserBadProfile", profile);
        }

        return Path.Combine(ScriptFolders.Browsers, browser.Trim().ToLowerInvariant(), profile);
    }

    public void GoTo(string url, int timeoutMs) => OnPage(async () =>
    {
        var page = Page();
        page.SetDefaultTimeout(Patience(timeoutMs));
        await page.GotoAsync(url);
    });

    public string Click(PageTarget target, int timeoutMs)
        => OnElement(target, timeoutMs, element => element.ClickAsync());

    public string Fill(PageTarget target, string text, int timeoutMs)
        => OnElement(target, timeoutMs, async element => await element.FillAsync(text));

    public string Select(PageTarget target, string value, bool byIndex, int timeoutMs)
        => OnElement(target, timeoutMs, async element =>
        {
            if (byIndex)
            {
                // The page counts the options from the top starting at nought; a macro counts them
                // the way a person does, from one.
                await element.SelectOptionAsync(new SelectOptionValue { Index = Position(value) - 1 });
                return;
            }

            // A plain string is matched against the option's value and its visible text both, which
            // is what the step promises: one box, filled in with whichever of the two the person
            // writing the macro was reading off the page.
            await element.SelectOptionAsync(value);
        });

    /// <summary>
    /// A position in a list, which is handed over as the text of a field: something that is not a
    /// whole number counting from 1 is a mistake in the step rather than a list to look through.
    /// The engine checks this before the step runs, and it is checked again here so that the device
    /// is not the one place where a macro written by hand can hand over something meaningless.
    /// </summary>
    private static int Position(string value)
        => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)
           && at >= 1
            ? at
            : throw new DeviceActionException("Run.BadPosition", value);

    public string Check(PageTarget target, bool wanted, int timeoutMs)
        => OnElement(target, timeoutMs, async element =>
        {
            if (wanted)
            {
                await element.CheckAsync();
            }
            else
            {
                await element.UncheckAsync();
            }
        });

    public string Hover(PageTarget target, int timeoutMs)
        => OnElement(target, timeoutMs, element => element.HoverAsync());

    /// <summary>
    /// Hands one key, or one chord, to the page. A key a macro writes is its own name for it, and a
    /// name the page's keyboard has nothing for is refused here rather than sent as a letter that
    /// happens to share its first character.
    /// </summary>
    public string Press(PageTarget? target, string key, int timeoutMs)
    {
        var written = PageKeys(key) ?? throw new DeviceActionException("Run.UnknownPageKey", key);
        if (target is null || target.Selectors.Count == 0)
        {
            OnPage(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(Patience(timeoutMs));
                await page.Keyboard.PressAsync(written);
            });
            return string.Empty;
        }

        return OnElement(target, timeoutMs, element => element.PressAsync(written));
    }

    /// <summary>
    /// Waits for an element to reach the state a step asked for. Running out of time is that answer
    /// rather than the browser failing, so it is reported as the step having waited too long: a page
    /// that never showed the element is what the step was asking about, and "it never turned up" is
    /// what the person reading the failure needs to know.
    /// </summary>
    public string? WaitFor(PageTarget target, ElementWait state, int timeoutMs)
    {
        if (target.Selectors.Count == 0)
        {
            throw new DeviceActionException("Run.BrowserNoSelector");
        }

        var patience = Patience(timeoutMs);
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(patience);
        try
        {
            return Wait(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(patience);

                // One selector is the everyday case, and Playwright's own wait is better at it than
                // a clock of ours: it wakes when the page changes rather than looking again on a
                // timer.
                if (target.Selectors.Count == 1)
                {
                    var only = target.Selectors[0];
                    await Named(only).Nth(target.MatchIndex - 1).WaitForAsync(
                        new LocatorWaitForOptions
                        {
                            State = WaitState(state),
                            Timeout = patience,
                        });
                    return only;
                }

                // Several selectors are waited for together, since a selector that finds nothing is
                // exactly what the next one is a fallback for: the first of them to reach the state
                // asked for is the one the step is about.
                while (true)
                {
                    foreach (var selector in target.Selectors)
                    {
                        if (await Reached(selector, target.MatchIndex, state))
                        {
                            return selector;
                        }
                    }

                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new DeviceActionException("Run.Timeout",
                            patience.ToString(CultureInfo.InvariantCulture));
                    }

                    await Task.Delay(CandidateIntervalMs);
                }
            });
        }
        catch (TimeoutException)
        {
            throw new DeviceActionException("Run.Timeout",
                patience.ToString(CultureInfo.InvariantCulture));
        }
        catch (PlaywrightException failure)
        {
            throw Failed(failure);
        }
    }

    public string Upload(PageTarget target, IReadOnlyList<string> paths, int timeoutMs)
        => OnElement(target, timeoutMs, async element => await element.SetInputFilesAsync(paths));

    public string Text(PageTarget? target, PagePart part, string attribute, int timeoutMs)
    {
        if (target is null || target.Selectors.Count == 0)
        {
            return OnPage(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(Patience(timeoutMs));
                return await page.Locator("body").InnerTextAsync();
            });
        }

        return OnElement(target, timeoutMs, (element, _) => Part(element, part, attribute));
    }

    /// <summary>
    /// Every element a target names, read the way the step asked for. Which of the target's ways of
    /// naming the element is used is the same question as for one element, and the elements are the
    /// ones from the match the step named onward: "the third row and the ones after it" is what a
    /// macro that reads a list is after.
    /// </summary>
    public IReadOnlyList<string> ReadAll(PageTarget target, PagePart part, string attribute,
        int maxItems, int timeoutMs)
    {
        var patience = Patience(timeoutMs);
        var limit = Math.Max(1, maxItems);
        try
        {
            return Wait(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(patience);
                var (selector, _) = await Found(target, patience);
                var element = Named(selector);

                // The first entry is waited for the way one element is any other step: a page that is
                // still filling its list has nothing to read yet, and reading nothing at all would
                // leave the macro carrying on with a list that never arrived without saying so.
                var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(patience);
                var count = await element.CountAsync();
                while (count < target.MatchIndex)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw NotFound(target, count);
                    }

                    await Task.Delay(CandidateIntervalMs);
                    count = await element.CountAsync();
                }

                var items = new List<string>();
                for (var at = target.MatchIndex - 1; at < count && items.Count < limit; at++)
                {
                    items.Add(await Part(element.Nth(at), part, attribute));
                }

                return (IReadOnlyList<string>)items;
            });
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Missing(target, failure);
        }
    }

    /// <summary>
    /// The table a target names, read out of the page's own markup. Which of its rows are names and
    /// which are data is asked of the page, and the answer comes back as it stands: a table that
    /// writes no strip of names is read as having none.
    /// </summary>
    public PageTable ReadTable(PageTarget target, int maxRows, int timeoutMs)
        => OnElement(target, timeoutMs, async (element, _) =>
            TableOf(await element.EvaluateAsync<JsonElement?>(BrowserPageScript.Table),
                Math.Max(1, maxRows)));

    public PageInfo ReadPage(int timeoutMs) => OnPage(async () =>
    {
        var page = Page();
        page.SetDefaultTimeout(Patience(timeoutMs));
        return new PageInfo(page.Url, await page.TitleAsync());
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

    /// <summary>
    /// Finds the element a target names and does one of the things a step does to an element,
    /// answering with the selector that turned out to name it.
    /// </summary>
    private string OnElement(PageTarget target, int timeoutMs, Func<ILocator, Task> work)
        => OnElement(target, timeoutMs, async (element, selector) =>
        {
            await work(element);
            return selector;
        });

    /// <summary>
    /// One part of one element. The three parts are three different questions about the same
    /// element, and a step names which one it is after: the writing it shows, what it holds, or one
    /// attribute of its markup.
    /// </summary>
    private static async Task<string> Part(ILocator element, PagePart part, string attribute)
        => part switch
        {
            PagePart.Value => await element.InputValueAsync(),

            // A step that reads an attribute and names none is a step that did not say what to
            // read; the element on its own is not the answer to a question about its markup.
            PagePart.Attribute => attribute.Trim().Length > 0
                ? await element.GetAttributeAsync(attribute.Trim()) ?? string.Empty
                : throw new DeviceActionException("Run.BrowserNoAttribute"),

            _ => await element.InnerTextAsync(),
        };

    /// <summary>
    /// A table as the page wrote it down, with the names above its columns and its rows as lists of
    /// what the cells show. The most rows a step asked for is the most that is taken.
    /// </summary>
    private static PageTable TableOf(JsonElement? written, int maxRows)
    {
        if (written is not { ValueKind: JsonValueKind.Object } table)
        {
            return new PageTable([], []);
        }

        var columns = new List<string>();
        if (table.TryGetProperty("columns", out var heads) && heads.ValueKind == JsonValueKind.Array)
        {
            columns.AddRange(heads.EnumerateArray().Select(Cell));
        }

        var rows = new List<IReadOnlyList<string>>();
        if (table.TryGetProperty("rows", out var body) && body.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in body.EnumerateArray())
            {
                if (rows.Count >= maxRows)
                {
                    break;
                }

                rows.Add(row.ValueKind == JsonValueKind.Array
                    ? [.. row.EnumerateArray().Select(Cell)]
                    : []);
            }
        }

        return new PageTable(columns, rows);
    }

    /// <summary>What one cell of a table came back as, whether or not the page wrote a string.</summary>
    private static string Cell(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    /// <summary>
    /// Finds the element a target names and does something with it, answering with whatever that
    /// made of the element: both the element and the selector that named it are handed over, so a
    /// step whose own answer is "which one was used" has it to answer with.
    /// </summary>
    private T OnElement<T>(PageTarget target, int timeoutMs, Func<ILocator, string, Task<T>> work)
    {
        var patience = Patience(timeoutMs);
        try
        {
            return Wait(async () =>
            {
                Page().SetDefaultTimeout(patience);
                var (selector, element) = await Found(target, patience);
                return await work(element, selector);
            });
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Missing(target, failure);
        }
    }

    /// <summary>
    /// How long the browser gives its own part of a step: the step's own timeout when it has one,
    /// and the device's half minute otherwise, which is what a step that named no length of its own
    /// used to be given without being told.
    /// </summary>
    private static int Patience(int timeoutMs) => timeoutMs > 0 ? timeoutMs : DefaultWaitMs;

    /// <summary>
    /// The element a line of a target names: the frames it sits inside entered first, then the
    /// element itself, named whichever of the ways the line asked for.
    /// </summary>
    /// <remarks>
    /// A line that begins with none of the ways of writing a frame is Playwright's own selector,
    /// handed over as it stands so that everything a macro could write by hand — a CSS path, a
    /// chain of its own, a text or role engine — keeps working untouched. The four semantic ways
    /// are the ones Playwright has no selector spelling for, so they are asked as questions
    /// instead, with the meaning the API gives them: the words match anywhere in the name, with
    /// case and spacing set aside.
    /// </remarks>
    private ILocator Named(string line)
    {
        var rest = line;
        var place = new Place(Page(), null);
        while (true)
        {
            var cut = rest.IndexOf(Chain, StringComparison.Ordinal);
            var head = cut < 0 ? rest : rest[..cut];
            if (!head.StartsWith(FrameStep, StringComparison.Ordinal))
            {
                break;
            }

            var inside = head[FrameStep.Length..].Trim();
            if (inside.Length == 0 || cut < 0)
            {
                // A line that only says which frame to look in names no element inside it.
                throw new DeviceActionException("Run.BrowserNoSelector");
            }

            place = place.Frame(inside);
            rest = rest[(cut + Chain.Length)..];
        }

        foreach (var way in Semantic)
        {
            if (rest.StartsWith(way, StringComparison.Ordinal))
            {
                var text = rest[way.Length..].Trim();
                if (text.Length == 0)
                {
                    throw new DeviceActionException("Run.BrowserNoSelector");
                }

                return way switch
                {
                    "label=" => place.ByLabel(text),
                    "placeholder=" => place.ByPlaceholder(text),
                    "alt=" => place.ByAltText(text),
                    _ => place.ByTitle(text),
                };
            }
        }

        return place.Locator(rest);
    }

    /// <summary>
    /// Where a line is looked for: the page itself, or, once a line has said which frame it means,
    /// that frame. Both ask their questions the same way, which is why one of these stands for
    /// either of them.
    /// </summary>
    private sealed class Place(IPage page, IFrameLocator? frame)
    {
        public Place Frame(string selector) => frame is null
            ? new Place(page, page.FrameLocator(selector))
            : new Place(page, frame.FrameLocator(selector));

        public ILocator Locator(string selector) => frame is null
            ? page.Locator(selector)
            : frame.Locator(selector);

        public ILocator ByLabel(string text) => frame is null
            ? page.GetByLabel(text)
            : frame.GetByLabel(text);

        public ILocator ByPlaceholder(string text) => frame is null
            ? page.GetByPlaceholder(text)
            : frame.GetByPlaceholder(text);

        public ILocator ByAltText(string text) => frame is null
            ? page.GetByAltText(text)
            : frame.GetByAltText(text);

        public ILocator ByTitle(string text) => frame is null
            ? page.GetByTitle(text)
            : frame.GetByTitle(text);
    }

    /// <summary>
    /// The element a target names, waiting for one of its selectors to find something.
    /// </summary>
    /// <remarks>
    /// One selector is left to Playwright to wait for: it is what waits for an element to arrive and
    /// then for it to become clickable, and it wakes on a change of the page rather than looking
    /// again on a timer. Several are looked at one after another, because the whole point of a
    /// second selector is that the first may find nothing — and, since the element may still be on
    /// its way, the looks are spread over the step's own clock rather than made once.
    /// <para>
    /// A fallback stands in for "this way of naming the element finds nothing". It is not a second
    /// try at an element that was found but would not be clicked: what stood in the way of the click
    /// is what the failure has to say, and pointing the step at another element instead would be
    /// work the person writing the macro did not ask for.
    /// </para>
    /// </remarks>
    private async Task<(string Selector, ILocator Locator)> Found(PageTarget target, int patience)
    {
        if (target.Selectors.Count == 0)
        {
            throw new DeviceActionException("Run.BrowserNoSelector");
        }

        var page = Page();
        if (target.Selectors.Count == 1)
        {
            var only = target.Selectors[0];
            return (only, Named(only).Nth(target.MatchIndex - 1));
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(patience);
        while (true)
        {
            var (_, selector, _) = await Which(target);
            if (selector.Length > 0)
            {
                return (selector, Named(selector).Nth(target.MatchIndex - 1));
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw NotFound(target, Most(target));
            }

            await Task.Delay(CandidateIntervalMs);
        }
    }

    /// <summary>
    /// Which of a target's selectors names the element the step means, and how many elements that
    /// selector names, asked of the page as it is now.
    /// </summary>
    private async Task<(int Line, string Selector, int Matches)> Which(PageTarget target)
    {
        var page = Page();
        for (var line = 0; line < target.Selectors.Count; line++)
        {
            var matches = await Named(target.Selectors[line]).CountAsync();
            if (matches >= target.MatchIndex)
            {
                return (line + 1, target.Selectors[line], matches);
            }
        }

        return (0, string.Empty, 0);
    }

    /// <summary>
    /// Whether one selector of a target has reached the state a step is waiting for. A selector that
    /// names nothing is "gone" and "off the page", which is what waiting for those two means.
    /// </summary>
    private async Task<bool> Reached(string selector, int matchIndex, ElementWait state)
    {
        var element = Named(selector);
        if (await element.CountAsync() < matchIndex)
        {
            return state is ElementWait.Hidden or ElementWait.Detached;
        }

        if (state is ElementWait.Attached or ElementWait.Detached)
        {
            return state is ElementWait.Attached;
        }

        var visible = await element.Nth(matchIndex - 1).IsVisibleAsync();
        return state is ElementWait.Visible ? visible : !visible;
    }

    /// <summary>
    /// What to report when a page action failed. Not finding the element at all is the failure that
    /// turns up most — a page that has changed since the selector was written no longer has it —
    /// and when that is what happened, naming the selectors that were tried says more than what the
    /// driver said. Anything else, an element that is there but covered or switched off among them,
    /// is reported the way Playwright put it, because that message names what stood in the way.
    /// </summary>
    private DeviceActionException Missing(PageTarget target, Exception failure)
    {
        var most = Most(target);
        return most >= 0 && most < target.MatchIndex ? NotFound(target, most) : Failed(failure);
    }

    /// <summary>
    /// The most elements any of a target's selectors names right now, asked so that a failure can
    /// say which of the two it was: a page holding nothing of that name at all, or one holding some
    /// of it but fewer than the step asked for. Nothing to ask — the page or the browser went away
    /// — is answered with -1, which is the caller's own failure to report.
    /// </summary>
    private int Most(PageTarget target)
    {
        try
        {
            var page = Page();
            var most = 0;
            foreach (var selector in target.Selectors)
            {
                most = Math.Max(most, Wait(async () => await Named(selector).CountAsync()));
            }

            return most;
        }
        catch (Exception failure)
            when (failure is PlaywrightException or TimeoutException or DeviceActionException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The failure of not having found the element a step meant: which of the selectors were tried
    /// is what it says, and which of the two ways it went wrong is the key, because a page that
    /// holds three of what a step asked four of is not a page holding none of it, and saying it
    /// held none would send the person reading the failure after a selector that was right.
    /// </summary>
    private static DeviceActionException NotFound(PageTarget target, int most)
        => new(most > 0 ? "Run.BrowserTooFew" : "Run.BrowserNotFound", Tried(target));

    /// <summary>The selectors that were tried, written the way the failure reports them.</summary>
    private static string Tried(PageTarget target) => string.Join(" / ", target.Selectors);

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
            where.Length == 0 ? failure.Message : where + ": " + failure.Message);
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
    public HighlightResult Highlight(PageTarget target)
    {
        if (!IsOpen || target.Selectors.Count == 0)
        {
            return new HighlightResult(false, 0, 0);
        }

        try
        {
            return Wait(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(HighlightTimeoutMs);
                await page.BringToFrontAsync();

                // Which of a target's selectors names the element, and how many elements that one
                // names, are the two answers worth having next to "it is there": the step will use
                // the same selector, and a selector naming three elements is worth knowing about
                // before a run rather than after it.
                var (line, selector, matches) = await Which(target);
                if (line == 0)
                {
                    return new HighlightResult(false, 0, 0);
                }

                var element = Named(selector).Nth(target.MatchIndex - 1);
                await element.ScrollIntoViewIfNeededAsync();
                await element.EvaluateAsync<bool>(BrowserPickerScript.Flash, null,
                    new LocatorEvaluateOptions { Timeout = HighlightTimeoutMs });
                return new HighlightResult(true, line, matches);
            });
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            return new HighlightResult(false, 0, 0);
        }
    }

    public PickResult Pick(string hint, int timeoutMs)
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
                        (BindingSource source, string? candidates) =>
                        {
                            _answeredFrame = source.Frame;
                            _picked?.TrySetResult(candidates ?? string.Empty);
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
                return new PickResult(string.Empty, []);
            }

            if (answer.Task.Result is not { Length: > 0 } payload)
            {
                return new PickResult(string.Empty, []);
            }

            // The person picked in whichever tab they were looking at, and that is the tab they
            // mean the macro to work on. Left on the tab it was on before, every step after this
            // would aim at a page holding none of what they just pointed at — or, on a site whose
            // pages look alike, at the element over there that happens to match.
            if (_answeredFrame is { } answered && !ReferenceEquals(answered.Page, page))
            {
                _page = answered.Page;
            }

            // What the page wrote down is a list of ways of naming the element it was clicked on,
            // steadiest first. Which of them are any good is asked of the engine rather than taken
            // on the page's word: the selector the macro will use is read by Playwright, and a
            // page's own reading of a selector is not the same question.
            var chosen = Page();
            var ranked = Wait(async () => await Rank(Ways(payload)))
                .Select(candidate => candidate with
                {
                    Selector = InFrame(chosen, _answeredFrame, candidate.Selector),
                })
                .ToList();

            return ranked.Count == 0
                ? new PickResult(string.Empty, [])
                : new PickResult(ranked[0].Selector, ranked);
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
    /// Each frame on the way is written as that frame's own step, so one line still names the
    /// element.
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

    /// <summary>The way in to a frame: each frame on the way written as a frame of its own, outermost first.</summary>
    private static async Task<string> WayInAsync(IFrame frame)
    {
        var way = new List<string>();
        for (var at = frame; at.ParentFrame is not null; at = at.ParentFrame)
        {
            if (await at.FrameElementAsync() is not { } element)
            {
                break;
            }

            way.Insert(0, FrameStep + await element.EvaluateAsync<string>(BrowserPickerScript.FramePath) + Chain);
        }

        return string.Concat(way);
    }

    /// <summary>One way of naming the element a person clicked, as the page wrote it down.</summary>
    private sealed record RawCandidate(string Kind, string Selector);

    /// <summary>How the page's own spelling of those two fields is read: the script writes them its way.</summary>
    private static readonly JsonSerializerOptions Reading =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The ways of naming the element a pick answered with, read out of what the page sent. A
    /// payload that cannot be read is answered with nothing rather than refused: it is a pick that
    /// found nothing, which is something a pick is allowed to do.
    /// </summary>
    private static IReadOnlyList<RawCandidate> Ways(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RawCandidate>>(payload, Reading) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>How the page's own name for a kind of naming reads in the engine's terms.</summary>
    private static PickKind Kind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "testid" => PickKind.DataAttribute,
        "role" => PickKind.Role,
        "label" => PickKind.Label,
        "text" => PickKind.Text,
        "placeholder" => PickKind.Placeholder,
        "alt" => PickKind.AltText,
        "title" => PickKind.Title,
        "id" => PickKind.Id,
        "name" => PickKind.NameAttribute,
        "aria" => PickKind.AriaLabel,
        _ => PickKind.Path,
    };

    /// <summary>
    /// Every way of naming the picked element that is worth writing down, with how many elements
    /// each one names. A way that names nothing at all is dropped, because a page that has moved on
    /// since the click cannot be written down by a selector it no longer holds.
    /// </summary>
    private async Task<IReadOnlyList<PickCandidate>> Rank(IReadOnlyList<RawCandidate> raw)
    {
        var page = Page();
        var found = new List<PickCandidate>();
        foreach (var candidate in raw)
        {
            var matches = await Named(candidate.Selector).CountAsync();
            if (matches > 0)
            {
                found.Add(new PickCandidate(Kind(candidate.Kind), candidate.Selector, matches));
            }
        }

        // Nothing of what was clicked being on the page any more — it went away under the pick, or
        // the page was sent elsewhere while it was happening — leaves the path of tags as the one
        // way of naming it that was written down without asking the page anything.
        if (found.Count == 0 && raw.Count > 0)
        {
            found.Add(new PickCandidate(PickKind.Path, raw[^1].Selector, 0));
        }

        return found;
    }

    /// <summary>
    /// Every frame the picker has to reach: each frame of each tab of the browser. Tabs are opened
    /// by the person as much as by the macro, and the one being looked at is not always the first.
    /// </summary>
    private static IEnumerable<IFrame> Frames(IPage page)
        => page.Context.Pages.SelectMany(tab => tab.Frames);

    /// <summary>
    /// Runs a script in the page and answers with what it returned, which is how a check reaches
    /// the page the way the picker's own scripts do: a pick needs a person to click something, and
    /// what a pick then makes of the click is worth checking without one.
    /// </summary>
    internal string Evaluate(string script, object? argument = null)
        => Wait(async () => await Page().EvaluateAsync<string>(script, argument)) ?? string.Empty;

    /// <summary>
    /// Runs a script in one of the page's frames, which is how a check reaches the frame a pick
    /// answers from: a click made inside a frame is answered on that frame's own thread, and the
    /// entry it hands back carries the way in to it.
    /// </summary>
    internal string EvaluateIn(string frame, string script, object? argument = null) => Wait(async () =>
    {
        var inside = Page().Frame(frame)
            ?? throw new DeviceActionException("Run.BrowserNotFound", frame);
        return await inside.EvaluateAsync<string>(script, argument);
    }) ?? string.Empty;

    /// <summary>
    /// How many elements of the page a selector names, which is how a check says that what a pick
    /// wrote down is what the engine reads back.
    /// </summary>
    internal int Matches(string selector)
        => Wait(async () => await Named(selector).CountAsync());

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
