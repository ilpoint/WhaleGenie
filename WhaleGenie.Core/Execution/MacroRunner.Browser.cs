using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // -------------------------------------------------------------------- browser

    /// <summary>
    /// The element a browser step means: the selectors its target field holds, one to a line and
    /// tried in that order, which of the elements a selector matches is the one the step wants, and
    /// whatever else the step said the right element has to be like.
    /// </summary>
    private PageTarget Element(ExecutableStep step)
        => PageTarget.Of(ReadValue(step, "pageSelector").AsText(), Index(step, "matchIndex", 1),
            Conditions(step));

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
    private IReadOnlyList<PageCondition> Conditions(ExecutableStep step)
    {
        var conditions = new List<PageCondition>();
        foreach (var row in step.Rows("narrow"))
        {
            var how = row.TryGetValue("how", out var way) ? way.Trim() : string.Empty;
            if (how.Length == 0)
            {
                continue;
            }

            if (!Enum.TryParse<PageFilter>(how, ignoreCase: true, out var kind))
            {
                throw new StepFailure(RunKeys.BrowserBadFilter, how);
            }

            var value = row.TryGetValue("value", out var said) ? said : string.Empty;
            conditions.Add(new PageCondition(kind, Read(value).AsText()));
        }

        return conditions;
    }

    /// <summary>
    /// The key or the reading a step may do without: a step whose target is empty sends its key to
    /// the page itself, or reads the whole page, and has nothing for the browser to look for.
    /// </summary>
    private PageTarget? OptionalElement(ExecutableStep step)
    {
        var target = Element(step);
        return target.Selectors.Count == 0 ? null : target;
    }

    /// <summary>
    /// Opens a browser, after checking the one Playwright drives is on the machine. The check
    /// happens here rather than inside the driver so the user is told what to install instead of
    /// being handed whatever the driver says when it finds nothing to start.
    /// </summary>
    private void BrowserOpen(ExecutableStep step, int depth)
    {
        var browser = _devices.Browser;
        var engine = ReadText(step, "browser").Trim();
        if (!browser.Ready(engine))
        {
            throw new StepFailure(RunKeys.BrowserMissing, browser.InstallHint);
        }

        var url = ReadValue(step, "url").AsText();
        browser.Open(engine, url, Flag(step, "headless", false),
            ReadValue(step, "profile").AsText());
        Log(LogLevel.Info, depth, step.Type, LogKeys.OpenedBrowser, engine, browser.Url);
    }

    /// <summary>
    /// Reads the page, or one element of it, into a variable. Which part of the element is read is
    /// the step's own choice: what it shows, what it holds, or one attribute of its markup.
    /// </summary>
    private void BrowserReadText(ExecutableStep step, int depth)
    {
        var text = _devices.Browser.Text(OptionalElement(step), PagePartOf(step),
            ReadText(step, "attribute"), step.Meta.TimeoutMs);
        Give(step, "text", Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserRead, text.Length);
    }

    /// <summary>
    /// Reads every element a selector names into a list, so a macro can walk what a page lists
    /// rather than naming each of them: the second row of a result has nothing of its own to write
    /// down, and a macro that reads one row at a time cannot be written at all.
    /// </summary>
    private void BrowserReadAll(ExecutableStep step, int depth)
    {
        // A step with no limit at all still gets one, the same default the editor shows, so a page
        // whose list keeps growing cannot fill memory.
        var limit = Math.Clamp(Number(step, "maxItems"), 1, MaxRows);
        var items = _devices.Browser.ReadAll(Element(step), PagePartOf(step),
            ReadText(step, "attribute"), limit, step.Meta.TimeoutMs);

        Give(step, "items", Value.FromList(items.Select(Value.FromText)));
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserReadAll, items.Count);
    }

    /// <summary>
    /// Reads a page's table into a variable, shaped the way every other table in the program is:
    /// one entry per row, each entry a record whose keys are the names above the columns. A table
    /// that writes no such names is read by column letter, which is what the window reader does
    /// with a grid whose column names nobody kept.
    /// </summary>
    private void BrowserReadTable(ExecutableStep step, int depth)
    {
        var limit = Math.Clamp(Number(step, "maxRows"), 1, MaxRows);

        // A step that names no table reads the page's own table element, which is what the field
        // starts out holding: this is the one element a page always names the same way.
        var target = Element(step);
        if (target.Selectors.Count == 0)
        {
            // The conditions still stand: which table is meant is the default answer, and the step
            // may have said which of the page's tables it wants without writing a selector at all.
            target = PageTarget.Of("table", Index(step, "matchIndex", 1), target.Conditions);
        }

        var table = _devices.Browser.ReadTable(target, limit, step.Meta.TimeoutMs);

        // A table that keeps no strip of column names still has columns: they are the places its
        // cells sit in, so the widest row says how many there are.
        var width = table.Columns.Count > 0
            ? table.Columns.Count
            : table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count);
        var names = ColumnNames([.. table.Columns.Select(Value.FromText)], width);

        var name = Give(step, "table", Value.FromList(
            table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    Value.FromText(index < row.Count ? row[index] : string.Empty)))))));

        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserReadTable, name, table.Rows.Count);
    }

    /// <summary>
    /// Where the page is and what it calls itself, which is what a macro reads when the step after
    /// it depends on which page the browser ended up on rather than on anything the page shows.
    /// </summary>
    private void BrowserReadPage(ExecutableStep step, int depth)
    {
        var page = _devices.Browser.ReadPage(step.Meta.TimeoutMs);
        Give(step, "url", Value.FromText(page.Url));
        Give(step, "title", Value.FromText(page.Title));
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserReadPage, page.Url, page.Title);
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
    private void BrowserSelect(ExecutableStep step, int depth)
    {
        var value = ReadValue(step, "optionValue").AsText();
        var byIndex = ReadText(step, "by").Trim().Equals("index", StringComparison.OrdinalIgnoreCase);
        if (byIndex && !Position(value))
        {
            throw new StepFailure(RunKeys.BadPosition, value);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserSelected,
            _devices.Browser.Select(Element(step), value, byIndex, step.Meta.TimeoutMs), value);
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
    private void BrowserPress(ExecutableStep step, int depth)
    {
        var key = ReadText(step, "keyChord").Trim();
        var keys = KeyNames.ResolveChord(key, out var unknown);
        if (keys.Count == 0 || unknown is not null)
        {
            throw new StepFailure(RunKeys.UnknownKey, unknown ?? key);
        }

        _devices.Browser.Press(OptionalElement(step), key, step.Meta.TimeoutMs);
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserPressed, key);
    }

    /// <summary>
    /// Waits for an element of the page to be in the state the step names. The step's own clock is
    /// what the wait is given, so the browser gives up at the same moment the step would rather
    /// than half a minute later, and running out is reported as the step having waited too long.
    /// </summary>
    private void BrowserWaitFor(ExecutableStep step, int depth)
    {
        var target = Element(step);
        var state = ElementStateOf(step);
        var waited = _devices.Browser.WaitFor(target, state, step.Meta.TimeoutMs);
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserWaited,
            waited ?? string.Join(" / ", target.Selectors));
    }

    /// <summary>Looks once for a page element in the state the step names.</summary>
    private void BrowserExists(ExecutableStep step, int depth)
    {
        var target = Element(step);
        var state = ElementStateOf(step);
        var found = _devices.Browser.Exists(target, state);
        Give(step, "found", Value.FromBool(found));
        Log(LogLevel.Info, depth, step.Type,
            found ? LogKeys.BrowserWaited : LogKeys.ElementMissing,
            string.Join(" / ", target.Selectors));
    }

    /// <summary>
    /// Clicks an element of the page. Which button is used, how many times it is pressed and whether
    /// it is made even through what stands in the way are the step's own answers: a page's own menu
    /// opens on the right button, and a list that opens in place needs two clicks.
    /// </summary>
    private void BrowserClick(ExecutableStep step, int depth)
    {
        var how = ReadText(step, "clickHow").Trim().ToLowerInvariant() switch
        {
            "double" => ClickWay.Double,
            "right" => ClickWay.Right,
            _ => ClickWay.Single,
        };

        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserClicked,
            _devices.Browser.Click(Element(step), how, Flag(step, "force", false),
                step.Meta.TimeoutMs));
    }

    /// <summary>
    /// Fills an element in. Written in at once is what a plain field needs; typed one key at a time
    /// is what a page that watches the typing itself needs, and the gap between two keys is the
    /// step's own answer.
    /// </summary>
    private void BrowserFill(ExecutableStep step, int depth)
    {
        var how = string.Equals(ReadText(step, "fillHow").Trim(), "type", StringComparison.OrdinalIgnoreCase)
            ? FillWay.Type
            : FillWay.Fill;

        var gap = how is FillWay.Type ? Number(step, "keyIntervalMs") : 0;
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserFilled,
            _devices.Browser.Fill(Element(step), ReadValue(step, "fillText").AsText(), how, gap,
                step.Meta.TimeoutMs));
    }

    /// <summary>
    /// Moves the page: to an element the step names, by one screen, to the end or back to the top.
    /// This is what a page that fetches more as it is read needs, because the writing below the fold
    /// arrives because something scrolled to where it would have been.
    /// </summary>
    private void BrowserScroll(ExecutableStep step, int depth)
    {
        var how = ReadText(step, "scrollHow").Trim().ToLowerInvariant() switch
        {
            "bottom" => ScrollWay.Bottom,
            "top" => ScrollWay.Top,
            "element" => ScrollWay.Element,
            _ => ScrollWay.Screen,
        };

        _devices.Browser.Scroll(OptionalElement(step), how, step.Meta.TimeoutMs);
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserScrolled);
    }

    /// <summary>
    /// Takes the next file the page hands over and saves it where the step says. The folder is read
    /// the way every other path a step is given is read, so one left out is the folder the macro file
    /// is in.
    /// </summary>
    private void BrowserDownload(ExecutableStep step, int depth)
    {
        var folder = ReadValue(step, "folder").AsText();
        var where = folder.Trim().Length > 0 ? _devices.Files.Resolve(folder) : _devices.Files.BaseFolder;
        var saved = _devices.Browser.Download(OptionalElement(step), where, step.Meta.TimeoutMs);

        Give(step, "path", Value.FromText(saved.Path));
        Give(step, "name", Value.FromText(saved.Name));
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserDownloaded, saved.Path);
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
    private async Task<Signal> RunDialog(ExecutableStep step, int depth, CancellationToken token)
    {
        var accept = !string.Equals(ReadText(step, "dialogHow").Trim(), "dismiss",
            StringComparison.OrdinalIgnoreCase);
        _devices.Browser.ArmDialog(accept, ReadValue(step, "dialogText").AsText());

        Signal signal;
        try
        {
            signal = await RunSteps(step.Children("body"), depth + 1, token);
        }
        catch
        {
            // A body that was stopped rather than failed still has to leave the browser unarmed: the
            // answer that was armed belongs to this step and to no other.
            _devices.Browser.TakeDialog();
            throw;
        }

        // A box whose steps never raised a dialog is a step that did not do what it says, rather
        // than one that worked and handed nothing over: the answer that was armed is taken back
        // either way, so a dialog put up by a later step is not answered with this one's answer.
        var message = _devices.Browser.TakeDialog();
        if (message is null)
        {
            throw new StepFailure(RunKeys.BrowserNoDialog);
        }

        Give(step, "message", Value.FromText(message));
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserAnswered, message);
        return signal;
    }

    /// <summary>
    /// Runs a script of the step's own inside the page, which is the way round whatever no action of
    /// the catalogue covers. What it returns comes back written as JSON, so a record or a list the
    /// script hands over keeps its shape and is read the way any other one is.
    /// </summary>
    private void BrowserEvaluate(ExecutableStep step, int depth)
    {
        var script = Template(ReadText(step, "script")).Trim();
        if (script.Length == 0)
        {
            throw new StepFailure(RunKeys.BrowserNoScript);
        }

        var result = _devices.Browser.Evaluate(script, step.Meta.TimeoutMs);
        Give(step, "result", Value.FromText(result));
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserEvaluated, result.Length);
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
    private void BrowserWaitForUrl(ExecutableStep step, int depth)
    {
        var wanted = ReadValue(step, "urlMatch").AsText();
        var how = Way(ReadText(step, "urlHow"));
        var options = OptionsOf(step, how);
        try
        {
            TextCompare.Check(wanted, how, options);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, wanted);
        }

        _devices.Browser.WaitForUrl(wanted, how, step.Meta.TimeoutMs, options);
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserAtAddress, _devices.Browser.Url);
    }

    /// <summary>
    /// Waits for the page to have got as far as the step names in its loading. Which of the three
    /// moments a macro wants is its own answer: reading the writing on a page needs the document and
    /// not the pictures, while a page that fills itself in by fetching needs the network to be quiet.
    /// </summary>
    private void BrowserWaitForLoad(ExecutableStep step, int depth)
    {
        var state = ReadText(step, "loadState").Trim().ToLowerInvariant() switch
        {
            "idle" => PageLoad.Idle,
            "ready" => PageLoad.Ready,
            _ => PageLoad.Loaded,
        };

        _devices.Browser.WaitForLoad(state, step.Meta.TimeoutMs);
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserLoaded);
    }

    /// <summary>
    /// Hands files to a file input, one path to a line. A relative path is read from the folder the
    /// macro file is in, the same as every other path a step is given, so a macro that hands over a
    /// file beside it can be moved to another machine as it stands.
    /// </summary>
    private void BrowserUpload(ExecutableStep step, int depth)
    {
        var paths = Lines(ReadValue(step, "uploadFiles").AsText())
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(_devices.Files.Resolve)
            .ToList();
        if (paths.Count == 0)
        {
            throw new StepFailure(RunKeys.MissingPath);
        }

        var element = _devices.Browser.Upload(Element(step), paths, step.Meta.TimeoutMs);
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserUploaded, paths.Count, element);
    }

    /// <summary>
    /// Moves the macro to another tab of the browser that is open. Many sites hand the page the
    /// macro came for to a new tab, and the macro would otherwise go on aiming its steps at the tab
    /// it was on — where, on a site whose pages look alike, a step finds something that matches and
    /// works on the wrong page without anything looking wrong.
    /// </summary>
    private void BrowserSwitchTab(ExecutableStep step, int depth)
    {
        var choice = ReadText(step, "tabHow").Trim().ToLowerInvariant() switch
        {
            "index" => TabChoice.Index,
            "title" => TabChoice.Title,
            "url" or "address" => TabChoice.Address,
            _ => TabChoice.Newest,
        };

        _devices.Browser.SwitchTab(choice, Number(step, "tabIndex"),
            ReadValue(step, "tabMatch").AsText());
        Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserTab, _devices.Browser.Url);
    }

    /// <summary>
    /// Runs a short script: a command, a batch file, a PowerShell or an AutoHotkey script, or a
    /// program of the macro's own choosing. This is the step that covers whatever the catalogue
    /// does not — anything with a command line of its own can be scripted — and a macro value gets
    /// in by being written into the text as <c>{{name}}</c> before the script runs. The script is
    /// either written in the step or named as a file of the macro's own, and a file is run where it
    /// lies rather than being written out again.
    /// </summary>

    /// <summary>
    /// The browser actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchBrowser(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "browser.open":
                BrowserOpen(step, depth);
                return Signal.Normal;
            case "browser.goTo":
                _devices.Browser.GoTo(ReadValue(step, "url").AsText(), step.Meta.TimeoutMs);
                Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserAddress, _devices.Browser.Url);
                return Signal.Normal;
            case "browser.click":
                BrowserClick(step, depth);
                return Signal.Normal;
            case "browser.fill":
                BrowserFill(step, depth);
                return Signal.Normal;
            case "browser.readText":
                BrowserReadText(step, depth);
                return Signal.Normal;
            case "browser.readAll":
                BrowserReadAll(step, depth);
                return Signal.Normal;
            case "browser.readTable":
                BrowserReadTable(step, depth);
                return Signal.Normal;
            case "browser.readPage":
                BrowserReadPage(step, depth);
                return Signal.Normal;
            case "browser.select":
                BrowserSelect(step, depth);
                return Signal.Normal;
            case "browser.check":
                Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserChecked,
                    _devices.Browser.Check(Element(step), Flag(step, "checked", true),
                        step.Meta.TimeoutMs));
                return Signal.Normal;
            case "browser.hover":
                Log(LogLevel.Info, depth, step.Type, LogKeys.BrowserHovered,
                    _devices.Browser.Hover(Element(step), step.Meta.TimeoutMs));
                return Signal.Normal;
            case "browser.press":
                BrowserPress(step, depth);
                return Signal.Normal;
            case "browser.waitFor":
                BrowserWaitFor(step, depth);
                return Signal.Normal;
            case "browser.exists":
                BrowserExists(step, depth);
                return Signal.Normal;
            case "browser.waitForUrl":
                BrowserWaitForUrl(step, depth);
                return Signal.Normal;
            case "browser.waitForLoad":
                BrowserWaitForLoad(step, depth);
                return Signal.Normal;
            case "browser.scroll":
                BrowserScroll(step, depth);
                return Signal.Normal;
            case "browser.download":
                BrowserDownload(step, depth);
                return Signal.Normal;
            case "browser.dialog":
                return await RunDialog(step, depth, token);
            case "browser.evaluate":
                BrowserEvaluate(step, depth);
                return Signal.Normal;
            case "browser.upload":
                BrowserUpload(step, depth);
                return Signal.Normal;
            case "browser.switchTab":
                BrowserSwitchTab(step, depth);
                return Signal.Normal;
            case "browser.closeTab":
                _devices.Browser.CloseTab();
                Log(LogLevel.Info, depth, step.Type, LogKeys.ClosedTab);
                return Signal.Normal;
            case "browser.close":
                _devices.Browser.Close();
                Log(LogLevel.Info, depth, step.Type, LogKeys.ClosedBrowser);
                return Signal.Normal;

            // ------------------------------------------------------------------ script
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
