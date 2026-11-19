using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Platform.Windows;

// Driving a page: going somewhere, clicking, writing, waiting, reading and scrolling.
public sealed partial class PlaywrightBrowserDevice
{
    public void GoTo(string url, int timeoutMs) => OnPage(async () =>
    {
        var page = Page();
        page.SetDefaultTimeout(Patience(timeoutMs));
        await page.GotoAsync(url);
    });

    public string Click(PageTarget target, ClickWay how, bool force, int timeoutMs)
        => OnElement(target, timeoutMs, element => how switch
        {
            ClickWay.Double => element.DblClickAsync(new LocatorDblClickOptions { Force = force }),
            ClickWay.Right => element.ClickAsync(
                new LocatorClickOptions { Button = MouseButton.Right, Force = force }),
            _ => element.ClickAsync(new LocatorClickOptions { Force = force }),
        });

    public string Fill(PageTarget target, string text, FillWay how, int intervalMs, int timeoutMs)
        => OnElement(target, timeoutMs, async element =>
        {
            if (how is FillWay.Type)
            {
                // What the field held is replaced the way a person replaces it, by selecting all of
                // it and typing over the top; writing the whole text in at once is a single change
                // the page only sees at the end, and a box that offers what matches as it goes never
                // offers anything.
                await element.FocusAsync();
                await element.PressAsync("Control+a");
                await element.PressSequentiallyAsync(text,
                    new LocatorPressSequentiallyOptions { Delay = intervalMs });
                return;
            }

            await element.FillAsync(text);
        });

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
            : throw new DeviceActionException(RunKeys.BadPosition, value);

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
        var written = PageKeys(key) ?? throw new DeviceActionException(RunKeys.UnknownPageKey, key);
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
            throw new DeviceActionException(RunKeys.BrowserNoSelector);
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
                    await Named(only, target.Conditions).Nth(target.MatchIndex - 1).WaitForAsync(
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
                        if (await Reached(target, selector, state))
                        {
                            return selector;
                        }
                    }

                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new DeviceActionException(RunKeys.Timeout,
                            patience.ToString(CultureInfo.InvariantCulture));
                    }

                    await Task.Delay(CandidateIntervalMs);
                }
            });
        }
        catch (TimeoutException)
        {
            throw new DeviceActionException(RunKeys.Timeout,
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
                var element = Named(selector, target.Conditions);

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

    /// <summary>
    /// Whether the element a target names is in the state asked for, looked at once.
    /// </summary>
    /// <remarks>
    /// Nothing is waited for here: the caller is a condition, which is asked again and again for as
    /// long as whatever block holds it keeps asking, so waiting inside one pass would hold the whole
    /// macro up. The target's own fallbacks are looked at in the order they were written, and the
    /// first that is in the state asked for is the answer.
    /// </remarks>
    public bool Exists(PageTarget target, ElementWait state)
    {
        if (target.Selectors.Count == 0)
        {
            throw new DeviceActionException(RunKeys.BrowserNoSelector);
        }

        try
        {
            return Wait(async () =>
            {
                // A look rather than an action, so the page's own default is left short: nothing here
                // is allowed to sit and wait for the element to turn up.
                var page = Page();
                page.SetDefaultTimeout(LookTimeoutMs);
                foreach (var selector in target.Selectors)
                {
                    if (await Reached(target, selector, state))
                    {
                        return true;
                    }
                }

                return false;
            });
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Failed(failure);
        }
    }

    /// <summary>
    /// Waits for the page to be somewhere else, which is what a macro waits for after a click that
    /// goes to another page: the address changes before the page it leads to has anything on it, so
    /// an element named too early is found on the page being left behind.
    /// </summary>
    public void WaitForUrl(string match, string how, int timeoutMs,
        TextCompareOptions options = default)
    {
        var patience = Patience(timeoutMs);
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(patience);
        try
        {
            Wait(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(patience);
                while (true)
                {
                    if (TextCompare.Matches(page.Url, match, how, options))
                    {
                        return;
                    }

                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new DeviceActionException(RunKeys.Timeout,
                            patience.ToString(CultureInfo.InvariantCulture));
                    }

                    await Task.Delay(CandidateIntervalMs);
                }
            });
        }
        catch (PlaywrightException failure)
        {
            throw Failed(failure);
        }
    }

    /// <summary>
    /// Waits for the page to have got as far as the state names. Running out of time is reported as
    /// the step having waited too long rather than as the browser failing, the same as every other
    /// wait: what the step asked about is whether the page got there, and "it did not" is an answer.
    /// </summary>
    public void WaitForLoad(PageLoad state, int timeoutMs)
    {
        var patience = Patience(timeoutMs);
        try
        {
            Wait(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(patience);
                await page.WaitForLoadStateAsync(Load(state),
                    new PageWaitForLoadStateOptions { Timeout = patience });
            });
        }
        catch (TimeoutException)
        {
            throw new DeviceActionException(RunKeys.Timeout,
                patience.ToString(CultureInfo.InvariantCulture));
        }
        catch (PlaywrightException failure)
        {
            throw Failed(failure);
        }
    }

    /// <summary>Which moment of a page's loading a state name means.</summary>
    private static LoadState Load(PageLoad state) => state switch
    {
        PageLoad.Loaded => LoadState.Load,
        PageLoad.Idle => LoadState.NetworkIdle,
        _ => LoadState.DOMContentLoaded,
    };

    /// <summary>
    /// Moves the page so that what the step is after is on screen. Scrolling to an element is the one
    /// way that names an element, and the page's own fallbacks are tried in the order they were
    /// written, so a step keeps working after a redesign the same way every other step does.
    /// </summary>
    public void Scroll(PageTarget? target, ScrollWay how, int timeoutMs)
    {
        if (how is ScrollWay.Element)
        {
            if (target is null || target.Selectors.Count == 0)
            {
                throw new DeviceActionException(RunKeys.BrowserNoSelector);
            }

            OnElement(target, timeoutMs, element => element.ScrollIntoViewIfNeededAsync());
            return;
        }

        OnPage(async () =>
        {
            var page = Page();
            page.SetDefaultTimeout(Patience(timeoutMs));
            await page.EvaluateAsync<JsonElement?>(Script(how), null);
        });
    }

    /// <summary>Which script of the page's own moves it the way the step asked.</summary>
    private static string Script(ScrollWay how) => how switch
    {
        ScrollWay.Bottom => BrowserPageScript.Bottom,
        ScrollWay.Top => BrowserPageScript.Top,
        _ => BrowserPageScript.Screen,
    };
}
