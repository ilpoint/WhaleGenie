using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Core.Devices.Platform;

// Finding the element a step means, and the failure that says which line was tried.
public sealed partial class PlaywrightBrowserDevice
{
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
            throw new DeviceActionException(RunKeys.BrowserNoSelector);
        }

        var page = Page();
        if (target.Selectors.Count == 1)
        {
            var only = target.Selectors[0];
            return (only, Named(only, target.Conditions).Nth(target.MatchIndex - 1));
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(patience);
        while (true)
        {
            var (_, selector, _) = await Which(target);
            if (selector.Length > 0)
            {
                return (selector, Named(selector, target.Conditions).Nth(target.MatchIndex - 1));
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
            var matches = await Named(target.Selectors[line], target.Conditions).CountAsync();
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
    private async Task<bool> Reached(PageTarget target, string selector, ElementWait state)
    {
        var element = Named(selector, target.Conditions);
        if (await element.CountAsync() < target.MatchIndex)
        {
            return state is ElementWait.Hidden or ElementWait.Detached;
        }

        if (state is ElementWait.Attached or ElementWait.Detached)
        {
            return state is ElementWait.Attached;
        }

        var visible = await element.Nth(target.MatchIndex - 1).IsVisibleAsync();
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
                most = Math.Max(most,
                    Wait(async () => await Named(selector, target.Conditions).CountAsync()));
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
        => new(most > 0 ? RunKeys.BrowserTooFew : RunKeys.BrowserNotFound, Tried(target));

    /// <summary>
    /// The selectors that were tried and what else the step said the element has to be like,
    /// written the way the failure reports them. A step that narrowed the page down and found
    /// nothing is a step whose conditions are part of what it was looking for, so a failure naming
    /// only the selectors would send the person reading it after the wrong half of the step.
    /// </summary>
    private static string Tried(PageTarget target)
    {
        var tried = string.Join(" / ", target.Selectors);
        if (target.Conditions.Count == 0)
        {
            return tried;
        }

        var narrowed = target.Conditions.Select(condition => condition.Value.Length == 0
            ? condition.How.ToString()
            : $"{condition.How}={condition.Value}");
        return tried + " [" + string.Join(", ", narrowed) + "]";
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
            RunKeys.BrowserFailed,
            where.Length == 0 ? failure.Message : where + ": " + failure.Message);
    }
}
