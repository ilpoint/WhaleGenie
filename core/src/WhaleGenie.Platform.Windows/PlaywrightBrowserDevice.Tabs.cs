using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Platform.Windows;

// The tab a macro works in: switching by number, title or address, and closing one.
public sealed partial class PlaywrightBrowserDevice
{
    public void SwitchTab(TabChoice choice, int index, string match)
        => OnPage(async () =>
        {
            // Looking a tab up by title or address is a question, and a step that forgot to say
            // what to look for would otherwise answer itself with the first tab there is.
            if (choice is TabChoice.Title or TabChoice.Address && match.Trim().Length == 0)
            {
                throw new DeviceActionException(RunKeys.BrowserTabNeedsText);
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
                throw new DeviceActionException(RunKeys.BrowserNoTab,
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
                throw new DeviceActionException(RunKeys.BrowserNoNewTab);
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
}
