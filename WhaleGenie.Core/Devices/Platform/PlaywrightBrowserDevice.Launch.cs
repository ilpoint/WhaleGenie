using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Core.Devices.Platform;

// Whether a browser is there, where it came from, and how it is started.
public sealed partial class PlaywrightBrowserDevice
{
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
                        ViewportSize = ViewportFor(headless),
                        AcceptDownloads = true,
                    });
            }
            else
            {
                _browser = await launcher.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Channel = channel.Length == 0 ? null : channel,
                    Headless = headless,
                });
                _context = await _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportFor(headless),
                    AcceptDownloads = true,
                });
            }

            // A file the page hands over is kept until a step asks for it: without a listener the
            // driver throws the download away, and the step that wants it is often written after the
            // click that started it.
            _context.Download += (_, download) =>
            {
                Catch(download);
            };

            // A dialog stops the page where it stands until somebody answers it, so the listener is
            // put on every tab and left there: with nothing armed it dismisses, which is what the
            // driver does with no listener at all, and it is what the step that answers a dialog
            // arms before running the steps that make one appear. Tabs opened later are its own
            // business, since a page that puts up a dialog usually opens it where the macro is.
            foreach (var tab in _context.Pages)
            {
                Listen(tab);
            }

            _context.Page += (_, tab) => Listen(tab);

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
    /// A visible browser follows the size of its own window, so maximizing the window really makes
    /// the page larger. A headless browser keeps Playwright's fixed viewport because there is no
    /// window for the page to follow.
    /// </summary>
    internal static ViewportSize? ViewportFor(bool headless)
        => headless ? null : ViewportSize.NoViewport;

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
            throw new DeviceActionException(RunKeys.BrowserBadProfile, profile);
        }

        return Path.Combine(ScriptFolders.Browsers, browser.Trim().ToLowerInvariant(), profile);
    }
}
