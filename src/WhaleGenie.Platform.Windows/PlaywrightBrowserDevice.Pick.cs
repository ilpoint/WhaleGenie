using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Platform.Windows;

// Marking an element, picking one off the page, and the candidates offered for the pick.
public sealed partial class PlaywrightBrowserDevice
{
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

                var element = Named(selector, target.Conditions).Nth(target.MatchIndex - 1);
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
            var matches = await Named(candidate.Selector, []).CountAsync();
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
    internal string InPage(string script, object? argument = null)
        => Wait(async () => await Page().EvaluateAsync<string>(script, argument)) ?? string.Empty;

    /// <summary>
    /// Runs a script in one of the page's frames, which is how a check reaches the frame a pick
    /// answers from: a click made inside a frame is answered on that frame's own thread, and the
    /// entry it hands back carries the way in to it.
    /// </summary>
    internal string EvaluateIn(string frame, string script, object? argument = null) => Wait(async () =>
    {
        var inside = Page().Frame(frame)
            ?? throw new DeviceActionException(RunKeys.BrowserNotFound, frame);
        return await inside.EvaluateAsync<string>(script, argument);
    }) ?? string.Empty;

    /// <summary>
    /// How many elements of the page a selector names, which is how a check says that what a pick
    /// wrote down is what the engine reads back.
    /// </summary>
    internal int Matches(string selector)
        => Matches(selector, []);

    /// <summary>
    /// How many elements of the page a written line names with these conditions on it, which is how
    /// a check says that a step narrows the page down to the one element it means.
    /// </summary>
    internal int Matches(string selector, IReadOnlyList<PageCondition> conditions)
        => Wait(async () => await Named(selector, conditions).CountAsync());

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
}
