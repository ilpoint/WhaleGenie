using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How a browser step finds the element it means: several ways of naming it tried in the order they
/// were written, which of the matches the step wants, the step's own clock, and the profile a
/// browser is left signed in on. All of it is true or false against a real browser, so the pages
/// are served from a temporary folder and driven without a window.
/// </summary>
public class BrowserTargetTests : IDisposable
{
    /// <summary>The folder the pages of this check are read from.</summary>
    private readonly string _site = Path.Combine(Path.GetTempPath(), $"wg-target-{Guid.NewGuid():N}");

    /// <summary>The name the profile of this check is kept under, which nothing else uses.</summary>
    private readonly string _profile = $"wg-test-{Guid.NewGuid():N}";

    public BrowserTargetTests() => Directory.CreateDirectory(_site);

    public void Dispose()
    {
        foreach (var folder in new[] { _site, Path.Combine(ScriptFolders.Browsers, "edge", _profile) })
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // The browser may still hold the page or the profile it was reading; the temporary
                // folder is the system's to clean up.
            }
        }

        GC.SuppressFinalize(this);
    }

    [RealBrowserFact]
    public void A_second_selector_is_used_when_the_first_finds_nothing()
    {
        Site("<div id=log></div>"
            + "<button id=a onclick=\"document.getElementById('log').textContent='a'\">A</button>"
            + "<button id=b onclick=\"document.getElementById('log').textContent='b'\">B</button>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // The first way of naming the element is the one used when it finds anything at all.
        device.Click(PageTarget.Of("#a\n#b", 1), 5000);
        Assert.Equal("a", device.Text(At("#log"), PagePart.Text, string.Empty, 5000).Trim());

        // A page that has been redesigned is met by a second line: the first selector is not there
        // any more and the step goes on working.
        var used = device.Click(PageTarget.Of("#gone\n#b", 1), 5000);
        Assert.Equal("#b", used);
        Assert.Equal("b", device.Text(At("#log"), PagePart.Text, string.Empty, 5000).Trim());
    }

    [RealBrowserFact]
    public void The_step_says_which_of_the_matches_it_means()
    {
        Site("<div id=log></div>"
            + "<button class=row onclick=\"document.getElementById('log').textContent='1'\">one</button>"
            + "<button class=row onclick=\"document.getElementById('log').textContent='2'\">two</button>"
            + "<button class=row onclick=\"document.getElementById('log').textContent='3'\">three</button>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        device.Click(PageTarget.Of(".row", 2), 5000);
        Assert.Equal("2", device.Text(At("#log"), PagePart.Text, string.Empty, 5000).Trim());

        // More matches than the page holds is the same answer as finding nothing, and it says which
        // of the two it was: the page holds elements of that name, just not that many of them.
        var missing = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of(".row", 4), 5000));
        Assert.Equal("Run.BrowserTooFew", missing.Key);
        Assert.Contains(".row", missing.Detail, StringComparison.Ordinal);
    }

    [RealBrowserFact]
    public void Nothing_found_names_every_way_that_was_tried_and_touches_nothing()
    {
        Site("<div id=log></div>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var missing = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of("#first\n#second", 1), 5000));

        Assert.Equal("Run.BrowserNotFound", missing.Key);
        Assert.Contains("#first", missing.Detail, StringComparison.Ordinal);
        Assert.Contains("#second", missing.Detail, StringComparison.Ordinal);

        // A step with nothing to look for says so rather than waiting its clock out.
        var empty = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of("  \n", 1), 5000));
        Assert.Equal("Run.BrowserNoSelector", empty.Key);
    }

    [RealBrowserFact]
    public void The_step_s_own_clock_is_what_the_browser_waits()
    {
        Site("<p>nothing to find here</p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var clock = Stopwatch.StartNew();
        var missing = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of("#never", 1), 800));
        clock.Stop();

        Assert.Equal("Run.BrowserNotFound", missing.Key);

        // The half minute Playwright falls back on is not what a step that said 800 milliseconds
        // gets: it is the step's own clock, and it is counted from the step rather than afterwards.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"waited {clock.Elapsed}");
    }

    [RealBrowserFact]
    public void A_profile_is_where_the_browser_stays_signed_in()
    {
        // What a browser remembers about a site is what a macro that logs in once lives on, so the
        // page writes something down and says what it found when it was opened.
        Site("<p id=seen></p><script>"
            + "const before = localStorage.getItem('mark') || 'empty';"
            + "localStorage.setItem('mark', 'here');"
            + "document.getElementById('seen').textContent = before;</script>");

        using var device = new PlaywrightBrowserDevice();

        device.Open("edge", Address(), headless: true, profile: _profile);
        Assert.Equal("empty", device.Text(At("#seen"), PagePart.Text, string.Empty, 5000).Trim());
        device.Close();

        // Opened on the same profile, the browser comes back to what the site remembers.
        device.Open("edge", Address(), headless: true, profile: _profile);
        Assert.Equal("here", device.Text(At("#seen"), PagePart.Text, string.Empty, 5000).Trim());
        device.Close();

        // Opened on none, it is a browser that remembers nothing, which is the default.
        device.Open("edge", Address(), headless: true, profile: string.Empty);
        Assert.Equal("empty", device.Text(At("#seen"), PagePart.Text, string.Empty, 5000).Trim());

        // A name a folder cannot have is refused before anything is started.
        var bad = Assert.Throws<DeviceActionException>(
            () => device.Open("edge", Address(), headless: true, profile: "a/b"));
        Assert.Equal("Run.BrowserBadProfile", bad.Key);
    }

    [RealBrowserFact]
    public void A_pick_offers_every_way_of_naming_the_element_the_steadies_first()
    {
        Site("<span class=wrap><button data-testid=save aria-label=\"Save changes\">Save</button></span>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // A pick waits for a person to click something, so what a click would do is done for it:
        // the picker is armed, the ways it works out are asked for, and they are handed back over
        // the very binding the page's own click handler answers through.
        var picking = Task.Run(() => device.Pick("pick something", 15000));
        var ways = Ways(device, "document.querySelector('button')");
        Assert.NotEmpty(ways);
        device.Evaluate("(ways) => window.__whalegeniePicked(ways)", ways);

        var picked = picking.GetAwaiter().GetResult();

        // The steadiest way of naming a button is the attribute the site keeps for its own tests,
        // and it is what the step is left holding.
        Assert.Equal("[data-testid=\"save\"]", picked.Selector);
        Assert.Equal(PickKind.DataAttribute, picked.Candidates[0].Kind);

        // The rest are there to be written under it: a page that has been redesigned is met by a
        // second way of naming the element rather than by a re-pick.
        Assert.Contains(picked.Candidates, way => way.Kind is PickKind.Role);
        Assert.Contains(picked.Candidates, way => way.Kind is PickKind.Text);
        Assert.Contains(picked.Candidates, way => way.Kind is PickKind.Path);
        Assert.All(picked.Candidates, way => Assert.True(way.Matches >= 1, way.Selector));

        // What was written down is what the engine reads back: the selector the pick went with
        // names exactly the one element it was clicked on.
        Assert.Equal(1, device.Matches(picked.Selector));
    }

    [RealBrowserFact]
    public void A_button_inside_a_shadow_root_is_named_and_clicked()
    {
        // A page built out of components keeps its elements inside shadow roots, where the page's
        // own reading of a CSS selector cannot see them and a way of naming one has to cross the
        // boundary to be any use.
        Site("<div id=host></div><p id=log></p><script>"
            + "const root = document.getElementById('host').attachShadow({ mode: 'open' });"
            + "root.innerHTML = '<button>Deep</button>';"
            + "root.querySelector('button').addEventListener('click',"
            + " () => { document.getElementById('log').textContent = 'deep'; });"
            + "</script>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var picking = Task.Run(() => device.Pick("pick something", 15000));
        var ways = Ways(device, "document.getElementById('host').shadowRoot.querySelector('button')");
        Assert.NotEmpty(ways);
        device.Evaluate("(ways) => window.__whalegeniePicked(ways)", ways);

        var picked = picking.GetAwaiter().GetResult();

        // The path of tags starts at the host rather than at the shadow root, which is the one way
        // of naming an element that reaches through the boundary, and the count is what says so:
        // it finds the very element the person clicked.
        var path = picked.Candidates.First(way => way.Kind is PickKind.Path).Selector;
        Assert.Contains("#host", path, StringComparison.Ordinal);
        Assert.Equal(1, device.Matches(path));

        device.Click(PageTarget.Of(path, 1), 5000);
        Assert.Equal("deep", device.Text(At("#log"), PagePart.Text, string.Empty, 5000).Trim());
    }

    [RealBrowserFact]
    public void A_form_control_is_named_by_its_label_and_by_its_placeholder()
    {
        Site("<label for=user>用户名</label><input id=user placeholder=\"账号\">"
            + "<input id=mail placeholder=\"邮箱\">");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // The words beside the box are how a person names a field, and they are asked of the page
        // rather than read as a selector: nothing about the markup has to line up with them.
        device.Fill(PageTarget.Of("label=用户名", 1), "报告", 5000);
        Assert.Equal("报告", device.Evaluate("() => document.getElementById('user').value"));

        // The words match anywhere in the name with case and spacing set aside, which is what the
        // question Playwright asks means when no exactness is asked for.
        Assert.Equal(1, device.Matches("label=用户"));
        device.Fill(PageTarget.Of("label=用户", 1), "再填一次", 5000);
        Assert.Equal("再填一次", device.Evaluate("() => document.getElementById('user').value"));

        // The grey hint inside a box names it too, and a name that only one box answers is one hit.
        Assert.Equal(1, device.Matches("placeholder=邮箱"));
        device.Fill(PageTarget.Of("placeholder=邮箱", 1), "a@b", 5000);
        Assert.Equal("a@b", device.Evaluate("() => document.getElementById('mail').value"));

        // A line that names a way but not the words is a line that names nothing.
        var empty = Assert.Throws<DeviceActionException>(
            () => device.Click(PageTarget.Of("label=", 1), 5000));
        Assert.Equal("Run.BrowserNoSelector", empty.Key);
    }

    [RealBrowserFact]
    public void A_picture_and_a_button_are_named_by_their_alt_text_and_their_title()
    {
        Site("<img id=logo alt=\"鲸灵首页\">"
            + "<p id=log></p>"
            + "<button id=save title=\"保存到文件\""
            + " onclick=\"document.getElementById('log').textContent='saved'\">图标</button>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        // A picture is named by what a reader who cannot see it is told, and a button that shows a
        // picture instead of words by the tip it offers.
        Assert.Equal(1, device.Matches("alt=鲸灵首页"));
        Assert.Equal(1, device.Matches("title=保存到文件"));
        Assert.Equal(0, device.Matches("alt=别的东西"));

        device.Click(PageTarget.Of("title=保存到文件", 1), 5000);
        Assert.Equal("saved", device.Text(At("#log"), PagePart.Text, string.Empty, 5000).Trim());
    }

    [RealBrowserFact]
    public void A_line_that_enters_a_frame_names_what_is_inside_it()
    {
        // A frame holds a document of its own, and a click aimed at the page waits for the element
        // in the wrong one: the way in is written down in front of the element, not left to be
        // guessed from where the element happens to sit.
        Site("<iframe id=pay name=inner srcdoc=\"<label for=card>卡号</label><input id=card>\"></iframe>"
            + "<p id=log></p>");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var inside = "frame=#pay >> label=卡号";
        Assert.Equal(1, device.Matches(inside));

        device.Fill(PageTarget.Of(inside, 1), "1234", 5000);
        Assert.Equal("1234", device.EvaluateIn("inner", "() => document.getElementById('card').value"));

        // The same way in is what a pick leaves behind: what a person pointed at inside a frame is
        // written down with the frame it was in.
        var picking = Task.Run(() => device.Pick("pick something", 15000));
        var ways = WaysIn(device, "inner", "document.querySelector('input')");
        Assert.NotEmpty(ways);
        device.EvaluateIn("inner", "(ways) => window.__whalegeniePicked(ways)", ways);

        var picked = picking.GetAwaiter().GetResult();
        Assert.StartsWith("frame=#pay >> ", picked.Selector, StringComparison.Ordinal);
        Assert.Equal(1, device.Matches(picked.Selector));
    }

    [RealBrowserFact]
    public void A_pick_names_a_form_control_by_the_words_the_page_shows()
    {
        Site("<label for=user>用户名</label><input id=user placeholder=\"账号\" title=\"登录名\">");

        using var device = new PlaywrightBrowserDevice();
        device.Open("edge", Address(), headless: true, profile: string.Empty);

        var picking = Task.Run(() => device.Pick("pick something", 15000));
        var ways = Ways(device, "document.getElementById('user')");
        Assert.NotEmpty(ways);
        device.Evaluate("(ways) => window.__whalegeniePicked(ways)", ways);

        var picked = picking.GetAwaiter().GetResult();
        var kinds = picked.Candidates.Select(way => way.Kind).ToList();

        // The ways that read the page's own words are offered in the order they are trusted in, and
        // each one is already counted: what stands beside the field says how many it names.
        Assert.Equal(PickKind.Role, kinds[0]);
        Assert.True(kinds.IndexOf(PickKind.Label) < kinds.IndexOf(PickKind.Placeholder));
        Assert.True(kinds.IndexOf(PickKind.Placeholder) < kinds.IndexOf(PickKind.Title));
        Assert.All(picked.Candidates, way => Assert.True(way.Matches >= 1, way.Selector));

        Assert.Contains(picked.Candidates, way => way.Selector == "label=用户名");
        Assert.Contains(picked.Candidates, way => way.Selector == "placeholder=账号");
        Assert.Contains(picked.Candidates, way => way.Selector == "title=登录名");

        // What the field is left holding is the steadiest of them, which is the first in the list.
        Assert.Equal(picked.Candidates[0].Selector, picked.Selector);
    }

    /// <summary>
    /// The ways of naming an element of the page, asked for the way a later look would ask: through
    /// the picker the device has just armed, once it is armed.
    /// </summary>
    private static string Ways(PlaywrightBrowserDevice device, string element)
        => Asked((script, argument) => device.Evaluate(script, argument), element);

    /// <summary>The same question asked of one of the page's frames, where a pick inside one answers from.</summary>
    private static string WaysIn(PlaywrightBrowserDevice device, string frame, string element)
        => Asked((script, argument) => device.EvaluateIn(frame, script, argument), element);

    /// <summary>The ways of naming an element, asked again until the picker has finished arming.</summary>
    private static string Asked(Func<string, object?, string> run, string element)
    {
        for (var look = 0; look < 200; look++)
        {
            var ways = run(
                "() => window.__whalegenieCandidates && " + element
                + " ? JSON.stringify(window.__whalegenieCandidates(" + element + "))"
                + " : ''",
                null);
            if (ways.Length > 0)
            {
                return ways;
            }

            Thread.Sleep(50);
        }

        return string.Empty;
    }

    /// <summary>Writes down the one page of this check.</summary>
    private void Site(string body)
        => File.WriteAllText(Path.Combine(_site, "page.html"),
            $"<html><body>{body}</body></html>");

    /// <summary>The address that page is read at.</summary>
    private string Address() => new Uri(Path.Combine(_site, "page.html")).AbsoluteUri;

    /// <summary>The element a step means when its field holds one selector and it wants the first match.</summary>
    private static PageTarget At(string selector) => PageTarget.Of(selector, 1);
}
