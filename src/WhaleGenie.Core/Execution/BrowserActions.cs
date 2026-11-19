using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The browser actions: opening a page, reading it, naming an element of it, and everything a step
/// does to that element. Which element a step means is read once, in <see cref="Element"/>, so a
/// click and a wait name the same one.
/// </summary>
internal static class BrowserActions
{
    /// <summary>
    /// The browser actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["browser.open"] = (run, step, depth, _) =>
            {
                BrowserOpen(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.goTo"] = (run, step, depth, _) =>
            {
                GoTo(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.click"] = (run, step, depth, _) =>
            {
                BrowserClick(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.fill"] = (run, step, depth, _) =>
            {
                BrowserFill(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.readText"] = (run, step, depth, _) =>
            {
                BrowserReadText(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.readAll"] = (run, step, depth, _) =>
            {
                BrowserReadAll(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.readTable"] = (run, step, depth, _) =>
            {
                BrowserReadTable(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.readPage"] = (run, step, depth, _) =>
            {
                BrowserReadPage(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.select"] = (run, step, depth, _) =>
            {
                BrowserSelect(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.check"] = (run, step, depth, _) =>
            {
                Check(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.hover"] = (run, step, depth, _) =>
            {
                Hover(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.press"] = (run, step, depth, _) =>
            {
                BrowserPress(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.waitFor"] = (run, step, depth, _) =>
            {
                BrowserWaitFor(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.exists"] = (run, step, depth, _) =>
            {
                BrowserExists(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.waitForUrl"] = (run, step, depth, _) =>
            {
                BrowserWaitForUrl(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.waitForLoad"] = (run, step, depth, _) =>
            {
                BrowserWaitForLoad(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.scroll"] = (run, step, depth, _) =>
            {
                BrowserScroll(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.download"] = (run, step, depth, _) =>
            {
                BrowserDownload(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.dialog"] = (run, step, depth, token) => RunDialog(run, step, depth, token),

            ["browser.evaluate"] = (run, step, depth, _) =>
            {
                BrowserEvaluate(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.upload"] = (run, step, depth, _) =>
            {
                BrowserUpload(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.switchTab"] = (run, step, depth, _) =>
            {
                BrowserSwitchTab(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.closeTab"] = (run, step, depth, _) =>
            {
                CloseTab(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["browser.close"] = (run, step, depth, _) =>
            {
                Close(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    /// <summary>
    /// The element a browser step means: the selectors its target field holds, one to a line and
    /// tried in that order, which of the elements a selector matches is the one the step wants, and
    /// whatever else the step said the right element has to be like.
    /// </summary>
    private static PageTarget Element(IRunContext run, ExecutableStep step)
        => PageTarget.Of(run.Args(step).ValueOf("pageSelector").AsText(), run.Index(step, "matchIndex", 1),
            Conditions(run, step));

    /// <summary>
    /// What a browser step says the element has to be like besides the way it is named: one row per
    /// condition, all of them holding at once, which is how a step tells the element it means from
    /// the others a selector names.
    /// </summary>
    /// <remarks>
    /// A row that says no way of narrowing is left out rather than refused: an empty row is what a
    /// half-filled form holds, and it is silent because a condition nobody asked for is not a
    /// condition. A row that says a way the engine does not know is refused, because a step that
    /// asked for less than it meant would otherwise act on the wrong element without saying so.
    /// </remarks>
    private static IReadOnlyList<PageCondition> Conditions(IRunContext run, ExecutableStep step)
    {
        var conditions = new List<PageCondition>();
        foreach (var row in step.Rows("narrow"))
        {
            var how = run.Word(row, "how");
            if (how.Length == 0)
            {
                continue;
            }

            if (!Enum.TryParse<PageFilter>(how, ignoreCase: true, out var kind))
            {
                throw new StepFailure(RunKeys.BrowserBadFilter, how);
            }

            var value = row.GetValueOrDefault("value") ?? Formula.Nothing;
            conditions.Add(new PageCondition(kind, run.Text(row, "value")));
        }

        return conditions;
    }

    /// <summary>
    /// The key or the reading a step may do without: a step whose target is empty sends its key to
    /// the page itself, or reads the whole page, and has nothing for the browser to look for.
    /// </summary>
    private static PageTarget? OptionalElement(IRunContext run, ExecutableStep step)
    {
        var target = Element(run, step);
        return target.Selectors.Count == 0 ? null : target;
    }

    /// <summary>
    /// Opens a browser, after checking the one Playwright drives is on the machine. The check
    /// happens here rather than inside the driver so the user is told what to install instead of
    /// being handed whatever the driver says when it finds nothing to start.
    /// </summary>
    private static void BrowserOpen(IRunContext run, ExecutableStep step, int depth)
    {
        var browser = run.Devices.Browser;
        var engine = run.Args(step).Text("browser").Trim();
        if (!browser.Ready(engine))
        {
            throw new StepFailure(RunKeys.BrowserMissing, browser.InstallHint);
        }

        var url = run.Args(step).ValueOf("url").AsText();
        browser.Open(engine, url, run.Flag(step, "headless", false),
            run.Args(step).ValueOf("profile").AsText());
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.OpenedBrowser, engine, browser.Url);
    }

    /// <summary>
    /// Reads the page, or one element of it, into a variable. Which part of the element is read is
    /// the step's own choice: what it shows, what it holds, or one attribute of its markup.
    /// </summary>
    private static void BrowserReadText(IRunContext run, ExecutableStep step, int depth)
    {
        var text = run.Devices.Browser.Text(OptionalElement(run, step), PagePartOf(step),
            run.Args(step).Text("attribute"), step.Meta.TimeoutMs);
        run.Give(step, "text", Value.FromText(text));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserRead, text.Length);
    }

    /// <summary>
    /// Reads every element a selector names into a list, so a macro can walk what a page lists
    /// rather than naming each of them: the second row of a result has nothing of its own to write
    /// down, and a macro that reads one row at a time cannot be written at all.
    /// </summary>
    private static void BrowserReadAll(IRunContext run, ExecutableStep step, int depth)
    {
        // A step with no limit at all still gets one, the same default the editor shows, so a page
        // whose list keeps growing cannot fill memory.
        var limit = Math.Clamp(run.Args(step).Number("maxItems"), 1, RunLimits.Rows);
        var items = run.Devices.Browser.ReadAll(Element(run, step), PagePartOf(step),
            run.Args(step).Text("attribute"), limit, step.Meta.TimeoutMs);

        run.Give(step, "items", Value.FromList(items.Select(Value.FromText)));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserReadAll, items.Count);
    }

    /// <summary>
    /// Reads a page's table into a variable, shaped the way every other table in the program is:
    /// one entry per row, each entry a record whose keys are the names above the columns. A table
    /// that writes no such names is read by column letter, which is what the window reader does
    /// with a grid whose column names nobody kept.
    /// </summary>
    private static void BrowserReadTable(IRunContext run, ExecutableStep step, int depth)
    {
        var limit = Math.Clamp(run.Args(step).Number("maxRows"), 1, RunLimits.Rows);

        // A step that names no table reads the page's own table element, which is what the field
        // starts out holding: this is the one element a page always names the same way.
        var target = Element(run, step);
        if (target.Selectors.Count == 0)
        {
            // The conditions still stand: which table is meant is the default answer, and the step
            // may have said which of the page's tables it wants without writing a selector at all.
            target = PageTarget.Of("table", run.Index(step, "matchIndex", 1), target.Conditions);
        }

        var table = run.Devices.Browser.ReadTable(target, limit, step.Meta.TimeoutMs);

        // A table that keeps no strip of column names still has columns: they are the places its
        // cells sit in, so the widest row says how many there are.
        var width = table.Columns.Count > 0
            ? table.Columns.Count
            : table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count);
        var names = TableColumns.Names([.. table.Columns.Select(Value.FromText)], width);

        var name = run.Give(step, "table", Value.FromList(
            table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    Value.FromText(index < row.Count ? row[index] : string.Empty)))))));

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserReadTable, name, table.Rows.Count);
    }

    /// <summary>
    /// Where the page is and what it calls itself, which is what a macro reads when the step after
    /// it depends on which page the browser ended up on rather than on anything the page shows.
    /// </summary>
    private static void BrowserReadPage(IRunContext run, ExecutableStep step, int depth)
    {
        var page = run.Devices.Browser.ReadPage(step.Meta.TimeoutMs);
        run.Give(step, "url", Value.FromText(page.Url));
        run.Give(step, "title", Value.FromText(page.Title));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserReadPage, page.Url, page.Title);
    }

    /// <summary>
    /// Which part of an element a page step reads. A word the engine does not know reads as the
    /// writing the element shows, which is what every such step meant before there was a choice.
    /// </summary>
    private static PagePart PagePartOf(ExecutableStep step)
        => step.Text("readWhat").Trim().ToLowerInvariant() switch
        {
            "value" => PagePart.Value,
            "attribute" => PagePart.Attribute,
            _ => PagePart.Text,
        };

    /// <summary>
    /// Picks an option of a dropdown. The position is turned into a number here, where the step's
    /// own name can be put on the failure: a box holding something that is not a whole number is a
    /// mistake in the step, and the person reading it wants to see which step.
    /// </summary>
    private static void BrowserSelect(IRunContext run, ExecutableStep step, int depth)
    {
        var value = run.Args(step).ValueOf("optionValue").AsText();
        var byIndex = run.Args(step).Text("by").Trim().Equals("index", StringComparison.OrdinalIgnoreCase);
        if (byIndex && !Position(value))
        {
            throw new StepFailure(RunKeys.BadPosition, value);
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserSelected,
            run.Devices.Browser.Select(Element(run, step), value, byIndex, step.Meta.TimeoutMs), value);
    }

    /// <summary>
    /// Whether a written place in a list is one a person could read off the page: a whole number,
    /// counted from the top starting at 1.
    /// </summary>
    private static bool Position(string value)
        => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)
            && at >= 1;

    /// <summary>
    /// Sends one key, or one chord, into the page. The name is checked against the same list the
    /// keyboard picker offers before it goes anywhere, so a key name that means nothing is refused
    /// here rather than arriving as a letter that happens to share its first character.
    /// </summary>
    private static void BrowserPress(IRunContext run, ExecutableStep step, int depth)
    {
        var key = run.Args(step).Text("keyChord").Trim();
        var keys = KeyNames.ResolveChord(key, out var unknown);
        if (keys.Count == 0 || unknown is not null)
        {
            throw new StepFailure(RunKeys.UnknownKey, unknown ?? key);
        }

        run.Devices.Browser.Press(OptionalElement(run, step), key, step.Meta.TimeoutMs);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserPressed, key);
    }

    /// <summary>
    /// Waits for an element of the page to be in the state the step names. The step's own clock is
    /// what the wait is given, so the browser gives up at the same moment the step would rather
    /// than half a minute later, and running out is reported as the step having waited too long.
    /// </summary>
    private static void BrowserWaitFor(IRunContext run, ExecutableStep step, int depth)
    {
        var target = Element(run, step);
        var state = ElementStateOf(step);
        var waited = run.Devices.Browser.WaitFor(target, state, step.Meta.TimeoutMs);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserWaited,
            waited ?? string.Join(" / ", target.Selectors));
    }

    /// <summary>Looks once for a page element in the state the step names.</summary>
    private static void BrowserExists(IRunContext run, ExecutableStep step, int depth)
    {
        var target = Element(run, step);
        var state = ElementStateOf(step);
        var found = run.Devices.Browser.Exists(target, state);
        run.Give(step, "found", Value.FromBool(found));
        run.Log(LogLevel.Info, depth, step.Type,
            found ? LogKeys.BrowserWaited : LogKeys.ElementMissing,
            string.Join(" / ", target.Selectors));
    }

    /// <summary>
    /// Clicks an element of the page. Which button is used, how many times it is pressed and whether
    /// it is made even through what stands in the way are the step's own answers: a page's own menu
    /// opens on the right button, and a list that opens in place needs two clicks.
    /// </summary>
    private static void BrowserClick(IRunContext run, ExecutableStep step, int depth)
    {
        var how = run.Args(step).Text("clickHow").Trim().ToLowerInvariant() switch
        {
            "double" => ClickWay.Double,
            "right" => ClickWay.Right,
            _ => ClickWay.Single,
        };

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserClicked,
            run.Devices.Browser.Click(Element(run, step), how, run.Flag(step, "force", false),
                step.Meta.TimeoutMs));
    }

    /// <summary>
    /// Fills an element in. Written in at once is what a plain field needs; typed one key at a time
    /// is what a page that watches the typing itself needs, and the gap between two keys is the
    /// step's own answer.
    /// </summary>
    private static void BrowserFill(IRunContext run, ExecutableStep step, int depth)
    {
        var how = string.Equals(run.Args(step).Text("fillHow").Trim(), "type", StringComparison.OrdinalIgnoreCase)
            ? FillWay.Type
            : FillWay.Fill;

        var gap = how is FillWay.Type ? run.Args(step).Number("keyIntervalMs") : 0;
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserFilled,
            run.Devices.Browser.Fill(Element(run, step), run.Args(step).ValueOf("fillText").AsText(), how, gap,
                step.Meta.TimeoutMs));
    }

    /// <summary>
    /// Moves the page: to an element the step names, by one screen, to the end or back to the top.
    /// This is what a page that fetches more as it is read needs, because the writing below the fold
    /// arrives because something scrolled to where it would have been.
    /// </summary>
    private static void BrowserScroll(IRunContext run, ExecutableStep step, int depth)
    {
        var how = run.Args(step).Text("scrollHow").Trim().ToLowerInvariant() switch
        {
            "bottom" => ScrollWay.Bottom,
            "top" => ScrollWay.Top,
            "element" => ScrollWay.Element,
            _ => ScrollWay.Screen,
        };

        run.Devices.Browser.Scroll(OptionalElement(run, step), how, step.Meta.TimeoutMs);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserScrolled);
    }

    /// <summary>
    /// Takes the next file the page hands over and saves it where the step says. The folder is read
    /// the way every other path a step is given is read, so one left out is the folder the macro file
    /// is in.
    /// </summary>
    private static void BrowserDownload(IRunContext run, ExecutableStep step, int depth)
    {
        var folder = run.Args(step).ValueOf("folder").AsText();
        var where = folder.Trim().Length > 0 ? run.Devices.Files.Resolve(folder) : run.Devices.Files.BaseFolder;
        var saved = run.Devices.Browser.Download(OptionalElement(run, step), where, step.Meta.TimeoutMs);

        run.Give(step, "path", Value.FromText(saved.Path));
        run.Give(step, "name", Value.FromText(saved.Name));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserDownloaded, saved.Path);
    }

    /// <summary>
    /// Answers the next dialog the page puts up, and runs the steps that make it appear.
    /// </summary>
    /// <remarks>
    /// It is a block because a dialog that nobody has answered stops the page: the click that raised
    /// it does not come back until somebody says yes or no, so a step that clicks and a step that
    /// answers afterwards cannot both run — the first one is still waiting when the second would
    /// start. The answer is therefore armed before the body runs, and the body holds the steps that
    /// trigger the dialog.
    /// </remarks>
    private static async Task<Signal> RunDialog(IRunContext run, ExecutableStep step, int depth, CancellationToken token)
    {
        var accept = !string.Equals(run.Args(step).Text("dialogHow").Trim(), "dismiss",
            StringComparison.OrdinalIgnoreCase);
        run.Devices.Browser.ArmDialog(accept, run.Args(step).ValueOf("dialogText").AsText());

        Signal signal;
        try
        {
            signal = await run.RunBlock(step.Children("body"), depth + 1, token);
        }
        catch
        {
            // A body that was stopped rather than failed still has to leave the browser unarmed: the
            // answer that was armed belongs to this step and to no other.
            run.Devices.Browser.TakeDialog();
            throw;
        }

        // A box whose steps never raised a dialog is a step that did not do what it says, rather
        // than one that worked and handed nothing over: the answer that was armed is taken back
        // either way, so a dialog put up by a later step is not answered with this one's answer.
        var message = run.Devices.Browser.TakeDialog();
        if (message is null)
        {
            throw new StepFailure(RunKeys.BrowserNoDialog);
        }

        run.Give(step, "message", Value.FromText(message));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserAnswered, message);
        return signal;
    }

    /// <summary>
    /// Runs a script of the step's own inside the page, which is the way round whatever no action of
    /// the catalogue covers. What it returns comes back written as JSON, so a record or a list the
    /// script hands over keeps its shape and is read the way any other one is.
    /// </summary>
    private static void BrowserEvaluate(IRunContext run, ExecutableStep step, int depth)
    {
        var script = Templates.Fill(run.Args(step).Text("script"), run.Variables).Trim();
        if (script.Length == 0)
        {
            throw new StepFailure(RunKeys.BrowserNoScript);
        }

        var result = run.Devices.Browser.Evaluate(script, step.Meta.TimeoutMs);
        run.Give(step, "result", Value.FromText(result));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserEvaluated, result.Length);
    }

    /// <summary>
    /// Which state of an element a step or a condition names. The four words are one list for both
    /// kinds of step, so a condition about an element and a wait for one say the same thing the same
    /// way. A word the engine does not know reads as "it is showing", which is what a step that is
    /// about to act on an element wants.
    /// </summary>
    private static ElementWait ElementStateOf(ExecutableStep step)
        => step.Text("elementState").Trim().ToLowerInvariant() switch
        {
            "hidden" => ElementWait.Hidden,
            "attached" => ElementWait.Attached,
            "detached" => ElementWait.Detached,
            _ => ElementWait.Visible,
        };

    /// <summary>
    /// Waits for the page to be at an address the step names. The way the address is compared is the
    /// one list of ways every piece of writing in the program is compared with, and it is read here
    /// first: a word that is none of the five, or a pattern the machine cannot read, is a mistake in
    /// the step and is reported before anything is waited for.
    /// </summary>
    private static void BrowserWaitForUrl(IRunContext run, ExecutableStep step, int depth)
    {
        var wanted = run.Args(step).ValueOf("urlMatch").AsText();
        var how = TextRule.Way(run.Args(step).Text("urlHow"));
        var options = TextRule.Options(how, run.Flag(step, "caseSensitive", false),
            run.Flag(step, "trim", true), run.Flag(step, "multiline", false));
        try
        {
            TextCompare.Check(wanted, how, options);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, wanted);
        }

        run.Devices.Browser.WaitForUrl(wanted, how, step.Meta.TimeoutMs, options);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserAtAddress, run.Devices.Browser.Url);
    }

    /// <summary>
    /// Waits for the page to have got as far as the step names in its loading. Which of the three
    /// moments a macro wants is its own answer: reading the writing on a page needs the document and
    /// not the pictures, while a page that fills itself in by fetching needs the network to be quiet.
    /// </summary>
    private static void BrowserWaitForLoad(IRunContext run, ExecutableStep step, int depth)
    {
        var state = run.Args(step).Text("loadState").Trim().ToLowerInvariant() switch
        {
            "idle" => PageLoad.Idle,
            "ready" => PageLoad.Ready,
            _ => PageLoad.Loaded,
        };

        run.Devices.Browser.WaitForLoad(state, step.Meta.TimeoutMs);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserLoaded);
    }

    /// <summary>
    /// Hands files to a file input, one path to a line. A relative path is read from the folder the
    /// macro file is in, the same as every other path a step is given, so a macro that hands over a
    /// file beside it can be moved to another machine as it stands.
    /// </summary>
    private static void BrowserUpload(IRunContext run, ExecutableStep step, int depth)
    {
        var paths = TextLines.Split(run.Args(step).ValueOf("uploadFiles").AsText())
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(run.Devices.Files.Resolve)
            .ToList();
        if (paths.Count == 0)
        {
            throw new StepFailure(RunKeys.MissingPath);
        }

        var element = run.Devices.Browser.Upload(Element(run, step), paths, step.Meta.TimeoutMs);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserUploaded, paths.Count, element);
    }

    /// <summary>
    /// Moves the macro to another tab of the browser that is open. Many sites hand the page the
    /// macro came for to a new tab, and the macro would otherwise go on aiming its steps at the tab
    /// it was on — where, on a site whose pages look alike, a step finds something that matches and
    /// works on the wrong page without anything looking wrong.
    /// </summary>
    private static void BrowserSwitchTab(IRunContext run, ExecutableStep step, int depth)
    {
        var choice = run.Args(step).Text("tabHow").Trim().ToLowerInvariant() switch
        {
            "index" => TabChoice.Index,
            "title" => TabChoice.Title,
            "url" or "address" => TabChoice.Address,
            _ => TabChoice.Newest,
        };

        run.Devices.Browser.SwitchTab(choice, run.Args(step).Number("tabIndex"),
            run.Args(step).ValueOf("tabMatch").AsText());
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserTab, run.Devices.Browser.Url);
    }

    private static void GoTo(IRunContext run, ExecutableStep step, int depth)
    {
        run.Devices.Browser.GoTo(run.Args(step).ValueOf("url").AsText(), step.Meta.TimeoutMs);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserAddress, run.Devices.Browser.Url);
    }

    private static void Check(IRunContext run, ExecutableStep step, int depth)
        => run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserChecked,
            run.Devices.Browser.Check(Element(run, step), run.Flag(step, "checked", true),
                step.Meta.TimeoutMs));

    private static void Hover(IRunContext run, ExecutableStep step, int depth)
        => run.Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserHovered,
            run.Devices.Browser.Hover(Element(run, step), step.Meta.TimeoutMs));

    private static void CloseTab(IRunContext run, ExecutableStep step, int depth)
    {
        run.Devices.Browser.CloseTab();
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClosedTab);
    }

    private static void Close(IRunContext run, ExecutableStep step, int depth)
    {
        run.Devices.Browser.Close();
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClosedBrowser);
    }
}
