using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend browser: whether it is open, where it is, what the page says, and every command the
/// engine sent it.
/// </summary>
internal sealed class FakeBrowser(FakeDesk desk) : IBrowserDevice
{
    /// <summary>Whether the browser this fake was asked about is installed.</summary>
    public bool Installed { get; set; } = true;

    public string Page { get; set; } = "page text";

    /// <summary>How long reading the page takes to return, for checking a call that cannot be cancelled.</summary>
    public int PageReadDelay { get; set; }

    public bool Ready(string browser) => Installed;

    public string InstallHint => "install the browser";

    public bool IsOpen { get; private set; }

    public string Url => Address;

    public string Address { get; private set; } = string.Empty;

    public void Open(string browser, string url, bool headless, string profile)
    {
        desk.Note($"browserOpen {browser} {url} {headless} {Profile(profile)}");
        IsOpen = true;
        Address = url;
    }

    public void GoTo(string url, int timeoutMs)
    {
        desk.Note($"browserGoTo {url} {timeoutMs}");
        Address = url;
    }

    public string Click(PageTarget target, ClickWay how, bool force, int timeoutMs)
    {
        desk.Note($"browserClick {Named(target)} {how} {force} {timeoutMs}");
        return Chosen(target);
    }

    public string Fill(PageTarget target, string text, FillWay how, int intervalMs,
        int timeoutMs)
    {
        desk.Note($"browserFill {Named(target)} {text} {how} {intervalMs} {timeoutMs}");
        return Chosen(target);
    }

    public void Scroll(PageTarget? target, ScrollWay how, int timeoutMs)
        => desk.Note($"browserScroll {Named(target)} {how} {timeoutMs}");

    /// <summary>Where a download lands and what the page called it, as though one had arrived.</summary>
    public DownloadResult Downloaded { get; set; } = new(@"C:\下载\报告.csv", "报告.csv");

    public DownloadResult Download(PageTarget? target, string folder, int timeoutMs)
    {
        desk.Note($"browserDownload {Named(target)} {folder} {timeoutMs}");
        return Downloaded;
    }

    /// <summary>The address the tab a switch moves to is showing, as though it had a page of its own.</summary>
    public string TabAddress { get; set; } = string.Empty;

    public void SwitchTab(TabChoice choice, int index, string match)
    {
        desk.Note($"browserSwitchTab {choice} {index} {match}");
        if (TabAddress.Length > 0)
        {
            Address = TabAddress;
        }
    }

    public void CloseTab() => desk.Note("browserCloseTab");

    public string Text(PageTarget? target, PagePart part, string attribute, int timeoutMs)
    {
        desk.Note($"browserText {Named(target)} {part} {Part(attribute)} {timeoutMs}");
        return Page;
    }

    /// <summary>What reading every match of a selector answers with, as though the page held them.</summary>
    public IReadOnlyList<string> Items { get; set; } = ["第一行", "第二行"];

    public IReadOnlyList<string> ReadAll(PageTarget target, PagePart part, string attribute,
        int maxItems, int timeoutMs)
    {
        desk.Note($"browserReadAll {Named(target)} {part} {Part(attribute)} {maxItems} {timeoutMs}");
        return Items;
    }

    /// <summary>What reading a table answers with, as though the page drew one.</summary>
    public PageTable Table { get; set; } = new(["金额"], [["100"], ["200"]]);

    public PageTable ReadTable(PageTarget target, int maxRows, int timeoutMs)
    {
        desk.Note($"browserReadTable {Named(target)} {maxRows} {timeoutMs}");
        return Table;
    }

    /// <summary>Where the page is and what it calls itself, as though it had loaded one.</summary>
    public PageInfo PageInfo { get; set; } = new("https://example.com/here", "示例页");

    public PageInfo ReadPage(int timeoutMs)
    {
        desk.Note($"browserReadPage {timeoutMs}");
        Thread.Sleep(PageReadDelay);
        return PageInfo;
    }

    /// <summary>What a condition about an element of the page answers with, as though it were there.</summary>
    /// <summary>Whether the page holds the element a step is looking for.</summary>
    public bool HasElement { get; set; } = true;

    public bool Exists(PageTarget target, ElementWait state)
    {
        desk.Note($"browserExists {Named(target)} {state}");
        return HasElement;
    }

    /// <summary>The address a wait for one ends at, as though the page had gone there.</summary>
    public string WaitingAddress { get; set; } = string.Empty;

    public void WaitForUrl(string match, string how, int timeoutMs,
        TextCompareOptions options)
    {
        desk.Note($"browserWaitForUrl {match} {how} {timeoutMs}");
        if (WaitingAddress.Length > 0)
        {
            Address = WaitingAddress;
        }
    }

    public void WaitForLoad(PageLoad state, int timeoutMs)
        => desk.Note($"browserWaitForLoad {state} {timeoutMs}");

    /// <summary>What the next dialog the page puts up says, as though one had appeared.</summary>
    public string? Dialog { get; set; } = "确定要提交吗？";

    public void ArmDialog(bool accept, string text)
        => desk.Note($"browserArmDialog {accept} {Part(text)}");

    public string? TakeDialog() => Dialog;

    /// <summary>What a script of the step's own answers with, as though the page had run it.</summary>
    public string Evaluated { get; set; } = "true";

    public string Evaluate(string script, int timeoutMs)
    {
        desk.Note($"browserEvaluate {script} {timeoutMs}");
        return Evaluated;
    }

    /// <summary>An attribute name as it reads in a note; nothing written is nothing asked for.</summary>
    private static string Part(string attribute)
        => attribute.Trim().Length == 0 ? "(none)" : attribute;

    /// <summary>What the next pick answers with, as though a person had clicked that element.</summary>
    public PickResult BrowserPicked { get; set; } =
        new("#picked", [new PickCandidate(PickKind.Id, "#picked", 1)]);

    public PickResult Pick(string hint, int timeoutMs)
    {
        desk.Note($"browserPick {hint} {timeoutMs}");
        return BrowserPicked;
    }

    public HighlightResult Highlight(PageTarget target)
    {
        desk.Note($"browserHighlight {Named(target)}");
        return IsOpen
            ? new HighlightResult(true, 1, target.Selectors.Count)
            : new HighlightResult(false, 0, 0);
    }

    public string Select(PageTarget target, string value, bool byIndex, int timeoutMs)
    {
        desk.Note($"browserSelect {Named(target)} {value} {byIndex} {timeoutMs}");
        return Chosen(target);
    }

    public string Check(PageTarget target, bool wanted, int timeoutMs)
    {
        desk.Note($"browserCheck {Named(target)} {wanted} {timeoutMs}");
        return Chosen(target);
    }

    public string Hover(PageTarget target, int timeoutMs)
    {
        desk.Note($"browserHover {Named(target)} {timeoutMs}");
        return Chosen(target);
    }

    public string Press(PageTarget? target, string key, int timeoutMs)
    {
        desk.Note($"browserPress {Named(target)} {key} {timeoutMs}");
        return target is null ? string.Empty : Chosen(target);
    }

    public string? WaitFor(PageTarget target, ElementWait state, int timeoutMs)
    {
        desk.Note($"browserWaitFor {Named(target)} {state} {timeoutMs}");
        return Chosen(target);
    }

    public string Upload(PageTarget target, IReadOnlyList<string> paths, int timeoutMs)
    {
        desk.Note($"browserUpload {Named(target)} {string.Join('|', paths)} {timeoutMs}");
        return Chosen(target);
    }

    /// <summary>
    /// A target as it reads in a note: every way of naming the element in the order it was written,
    /// and which of the matches the step means, because both are things the engine has to hand over
    /// untouched for a fallback to be worth writing down.
    /// </summary>
    private static string Named(PageTarget? target) => target is null || target.Selectors.Count == 0
        ? "(none)"
        : $"{string.Join(" / ", target.Selectors)} #{target.MatchIndex}{Narrowed(target)}";

    /// <summary>
    /// What a target narrows the page down by, written after which match the step means: the
    /// conditions a step adds are handed over with the selectors they go with, in the order they
    /// were written, and a target that adds none says nothing here.
    /// </summary>
    private static string Narrowed(PageTarget target) => target.Conditions.Count == 0
        ? string.Empty
        : " [" + string.Join(", ",
            target.Conditions.Select(condition => $"{condition.How}={condition.Value}")) + "]";

    /// <summary>
    /// Which of a target's selectors turns out to name the element, as though the ones before it had
    /// found nothing. 1 is the first, which is what a target with no fallbacks always answers.
    /// </summary>
    public int ChosenIndex { get; set; } = 1;

    /// <summary>The way of naming the element that won, which is what the run's own log reports.</summary>
    private string Chosen(PageTarget target)
        => target.Selectors.Count == 0
            ? string.Empty
            : target.Selectors[Math.Min(Math.Max(ChosenIndex, 1), target.Selectors.Count) - 1];

    private static string Profile(string profile) => profile.Length == 0 ? "(none)" : profile;

    public void Close()
    {
        desk.Note("browserClose");
        IsOpen = false;
    }
}
