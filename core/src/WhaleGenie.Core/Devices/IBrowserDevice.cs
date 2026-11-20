using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices;

// A browser and the page inside it.
/// <summary>
/// A browser the macro drives over its own automation protocol. Unlike the other devices this
/// one is stateful on purpose: a macro says "open this" and then "click that" the way a person
/// works, so the device keeps the page between steps until the macro closes it.
/// </summary>
/// <remarks>
/// A browser window holds several tabs, and the page a macro means is the one the person is
/// looking at rather than the first one that was opened. So the device keeps track of which tab it
/// is on, and the actions that move between tabs are what keeps that from being a guess.
/// </remarks>
public interface IBrowserDevice
{
    /// <summary>
    /// Whether this choice of browser can be started on this machine right now. The one Windows
    /// already ships — Edge — can always be used; the engines Playwright downloads by itself have
    /// to have been fetched first, so a machine that never ran a macro may not have them yet.
    /// </summary>
    bool Ready(string browser);

    /// <summary>How the browsers are put on the machine, told to the user when they are missing.</summary>
    string InstallHint { get; }

    /// <summary>
    /// Whether there is a page to drive right now. It is false again once the window the page was
    /// in has been closed, whether that was done by this program or by the person using it.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Starts a browser and opens a page at this address, under the profile named when one is. A
    /// profile is where the browser keeps what a site remembers about the person — the sign-in above
    /// all — so a macro that logs in once goes on finding itself logged in; no profile is a browser
    /// that forgets, which is what a macro that should not touch somebody's own session wants.
    /// </summary>
    void Open(string browser, string url, bool headless, string profile);

    /// <summary>
    /// Sends the page to this address, waiting for it to load no longer than the step's own clock
    /// gives it.
    /// </summary>
    void GoTo(string url, int timeoutMs);

    /// <summary>Where the page is right now.</summary>
    string Url { get; }

    /// <summary>
    /// Clicks the element the target names, and answers with the selector that turned out to name
    /// it: a target written as several selectors is one question with several answers, and which
    /// one was used is what the run's own log has to report. A forced click is made even through
    /// whatever stands in the way of it, which is what a page that hides its own buttons behind an
    /// overlay needs.
    /// </summary>
    string Click(PageTarget target, ClickWay how, bool force, int timeoutMs);

    /// <summary>
    /// Types text into the element the target names, replacing what it held, and answers with the
    /// selector that named it. Typed one key at a time rather than written in at once, it is what a
    /// page that watches the typing itself — a search box that offers suggestions, a form that
    /// checks each field as it is left — needs; the gap between two keys is the step's own, and
    /// nought means the keys go as fast as they can, with no pause between them that a page could
    /// notice.
    /// </summary>
    string Fill(PageTarget target, string text, FillWay how, int intervalMs, int timeoutMs);

    /// <summary>
    /// One part of the element the target names, or of the whole page when there is no target. The
    /// whole-page reading is what a macro wants when it is after the content rather than one field,
    /// and it is what a step that named no selector at all is read as; it reads the writing the page
    /// shows, there being no box and no markup of its own to ask for the other two parts of.
    /// </summary>
    string Text(PageTarget? target, PagePart part, string attribute, int timeoutMs);

    /// <summary>
    /// Every element the target names, each read the way the step asked for, starting at the match
    /// the target named and stopping after <paramref name="maxItems"/> of them.
    /// </summary>
    /// <remarks>
    /// A page holds lists of the same thing — every row of a result, every link of a menu — and the
    /// second of them has no name of its own to write down: reading them one step at a time is not
    /// something the person writing the macro can express. What is read is every match of the one
    /// selector that turned out to name anything, in the order the page shows them.
    /// </remarks>
    IReadOnlyList<string> ReadAll(PageTarget target, PagePart part, string attribute, int maxItems,
        int timeoutMs);

    /// <summary>
    /// The table the target names, read out of the page's own markup: the names it writes above its
    /// columns, and its rows under them. A table that writes no such names comes back with none, and
    /// the engine calls the columns by letter; the most rows read is
    /// <paramref name="maxRows"/>.
    /// </summary>
    PageTable ReadTable(PageTarget target, int maxRows, int timeoutMs);

    /// <summary>Where the page is and what it calls itself.</summary>
    PageInfo ReadPage(int timeoutMs);

    /// <summary>
    /// Whether the element the target names is in the state asked for right now.
    /// </summary>
    /// <remarks>
    /// It is a look rather than a wait: a condition is asked again and again by whatever block holds
    /// it, and a step that waits for an element is a step of its own. A page holding fewer elements
    /// than the target asks for is answered the way it is everywhere else — as naming nothing —
    /// which is what "gone" and "off the page" mean.
    /// </remarks>
    bool Exists(PageTarget target, ElementWait state);

    /// <summary>
    /// Waits for the page to be at an address the step names, comparing it the way
    /// <paramref name="how"/> says, and gives up after <paramref name="timeoutMs"/> milliseconds
    /// having waited none of its own.
    /// </summary>
    /// <remarks>
    /// What a macro waits for after a click that goes somewhere is the address rather than an
    /// element, because the element it is after often lives on the page the click leads to and
    /// naming it too early finds the one on the page being left behind.
    /// </remarks>
    void WaitForUrl(string match, string how, int timeoutMs,
        TextCompareOptions options = default);

    /// <summary>Waits for the page to have got as far as the state names in its loading.</summary>
    void WaitForLoad(PageLoad state, int timeoutMs);

    /// <summary>
    /// Moves the page so that what the step is after is on screen: the element the target names, or
    /// the page itself by one screen, to the bottom or back to the top.
    /// </summary>
    /// <remarks>
    /// Scrolling is what a page that fetches more as it is read needs: the writing below the fold
    /// arrives because something scrolled to where it would have been, and there is no element to
    /// wait for until it has. A target is only needed by the one way that scrolls to an element.
    /// </remarks>
    void Scroll(PageTarget? target, ScrollWay how, int timeoutMs);

    /// <summary>
    /// Takes the next file the page hands over, saving it into <paramref name="folder"/>, and
    /// answers with where it went and what it is called.
    /// </summary>
    /// <remarks>
    /// With a target, the element it names is clicked and the download that click starts is the one
    /// taken; with none, the step waits for the next download to come from anywhere on the page,
    /// which is what a macro does when the page decides for itself that something is a file. A
    /// download that started before this step asked for it is handed over rather than thrown away.
    /// Nobody turning up within the step's own clock is the step running out of time.
    /// </remarks>
    DownloadResult Download(PageTarget? target, string folder, int timeoutMs);

    /// <summary>
    /// Answers the next dialog the page puts up: accepted or dismissed, with writing of the step's
    /// own when the dialog is the kind that asks for some.
    /// </summary>
    /// <remarks>
    /// It is armed before the steps that make the dialog appear rather than called after them,
    /// because a dialog nobody has answered yet stops the step that raised it: in every browser the
    /// click that opens one does not come back until somebody says yes or no, so a step that clicks
    /// and a step that answers afterwards cannot both run. Only the first dialog of a block is
    /// answered this way; the ones after it are dismissed, which is what a browser does with nobody
    /// listening at all.
    /// </remarks>
    void ArmDialog(bool accept, string text);

    /// <summary>
    /// What the dialog a block answered said, and lets go of the armed answer.
    /// <see langword="null"/> when no dialog turned up, which is a step that did not do what it says.
    /// </summary>
    string? TakeDialog();

    /// <summary>
    /// Runs a script inside the page and answers with what it returned, written as JSON.
    /// </summary>
    /// <remarks>
    /// This is the way round to whatever no action of the catalogue covers — reading a page's own
    /// state, working something out from what it shows — at the price of the macro depending on the
    /// page's own script. Written as JSON rather than as text, a record or a list the script hands
    /// back keeps its shape and is read with <c>field()</c> the way any other record is.
    /// </remarks>
    string Evaluate(string script, int timeoutMs);

    /// <summary>
    /// Moves to another tab of the browser that is open, which is how a macro follows a click that
    /// opened one: the site hands the interesting page to a new tab, and the macro would otherwise
    /// go on aiming its steps at the tab it was already on — where the same site often has an
    /// element that looks just like the one it wants. A tab that cannot be found is refused rather
    /// than passed over, because every step after it would then act on the wrong page.
    /// </summary>
    void SwitchTab(TabChoice choice, int index, string match);

    /// <summary>
    /// Closes the tab the browser is on and moves to another one, which is what a macro does with
    /// the tab a click opened once it has read what it came for. Closing the last tab leaves no
    /// page behind, and the steps after it are refused like any other step with no browser open.
    /// </summary>
    void CloseTab();

    /// <summary>
    /// Puts the open page into picking mode and waits for the person to click an element on it,
    /// answering with the ways of naming what they clicked, the steadiest one first. An empty answer
    /// means they gave up, or nothing was clicked in time. The page is left the way it was either
    /// way.
    /// <paramref name="hint"/> is what the banner inside the page says, written in the reader's
    /// language by whoever asked for the pick — the engine holds no user-facing text of its own.
    /// </summary>
    PickResult Pick(string hint, int timeoutMs);

    /// <summary>
    /// Outlines the element the target names on the open page, so a selector can be checked against
    /// the page it was taken from without a run being started. Nothing found is an answer rather
    /// than a failure: the question was "did that take the right element?", and it is answered the
    /// same way the step itself would be, which of the target's selectors it used included.
    /// </summary>
    HighlightResult Highlight(PageTarget target);

    /// <summary>
    /// Picks an option of a dropdown the target names: the one whose value or visible text is
    /// <paramref name="value"/>, or the one at that position when <paramref name="byIndex"/> is set,
    /// counted from the top starting at 1. A dropdown the page itself draws out of other elements
    /// is not one of these, and is worked the way a person works it — click it, then click the
    /// option. Answers with the selector that named the dropdown.
    /// </summary>
    string Select(PageTarget target, string value, bool byIndex, int timeoutMs);

    /// <summary>
    /// Ticks or unticks a checkbox or a radio button, which is what a macro wants instead of
    /// clicking one: a click on an already-ticked box would untick it, while this says the state
    /// the macro means to leave behind. Asking for the state a box is already in does nothing
    /// rather than refusing. Answers with the selector that named the box.
    /// </summary>
    string Check(PageTarget target, bool wanted, int timeoutMs);

    /// <summary>
    /// Moves the pointer over the element the target names and leaves it there, which is what makes
    /// a menu, a tooltip or a row of buttons appear. Answers with the selector that named it.
    /// </summary>
    string Hover(PageTarget target, int timeoutMs);

    /// <summary>
    /// Presses one key while the page is the one listening, or while the element the target names
    /// is: the element takes the focus first, and no target at all leaves the focus where it is,
    /// which is how a macro sends a key to the page itself. The key is a name a macro writes, with
    /// keys joined by "+" for a chord. Answers with the selector that named the element, empty for
    /// a key sent to the page itself.
    /// </summary>
    string Press(PageTarget? target, string key, int timeoutMs);

    /// <summary>
    /// Waits for the element the target names to be in the state asked for, and gives up after
    /// <paramref name="timeoutMs"/> milliseconds, having waited none of its own. Running out of
    /// time is reported as such rather than as the browser failing, because what the step asked
    /// for is an answer to "did it turn up", and "no" is an answer. Answers with the selector that
    /// named the element, or nothing when the page never held one — which for "gone" and "off the
    /// page" is the state that was asked for.
    /// </summary>
    string? WaitFor(PageTarget target, ElementWait state, int timeoutMs);

    /// <summary>
    /// Hands files to a file input, which is the one thing on a page that cannot be done by typing:
    /// a browser refuses to be told a path by a script, so this is the way round it. Answers with
    /// the selector that named the input.
    /// </summary>
    string Upload(PageTarget target, IReadOnlyList<string> paths, int timeoutMs);

    /// <summary>Closes the browser and lets go of it, which a macro does when it is done.</summary>
    void Close();
}

/// <summary>What a step waits for an element of a page to be.</summary>
public enum ElementWait
{
    /// <summary>On the page and taking up room, so a person could see it.</summary>
    Visible,

    /// <summary>Gone, or on the page but taking up no room at all.</summary>
    Hidden,

    /// <summary>On the page, whether or not it can be seen.</summary>
    Attached,

    /// <summary>Not on the page at all, which is what a page that has thrown something away looks like.</summary>
    Detached,
}

/// <summary>Which part of an element a step reads, there being more than one thing to ask it.</summary>
/// <remarks>
/// What a box holds and what it shows are two different things: a password field shows dots and
/// holds the password, and a dropdown shows the option the person picked and holds the value behind
/// it. The markup is a third: an address a link goes to, a picture's description, a data attribute
/// the site keeps for itself.
/// </remarks>
public enum PagePart
{
    /// <summary>The writing the element shows.</summary>
    Text,

    /// <summary>What the element holds rather than shows.</summary>
    Value,

    /// <summary>One attribute of the element's markup, named by the step.</summary>
    Attribute,
}

/// <summary>How far along a page's loading a step waits for.</summary>
/// <remarks>
/// The three are the three moments a page can be said to have arrived at, and they are asked for by
/// name because which of them a macro wants is its own answer: a document that has been read is
/// enough for the writing on it, and the network falling quiet is what says everything the page
/// fetches by itself has come in.
/// </remarks>
public enum PageLoad
{
    /// <summary>The document has been read and drawn, and the page's own scripts have run.</summary>
    Ready,

    /// <summary>Everything the page itself asks for — its pictures, its stylesheets — has come in.</summary>
    Loaded,

    /// <summary>Nothing the page asks for in the background is still outstanding.</summary>
    Idle,
}

/// <summary>How a step clicks an element of a page.</summary>
public enum ClickWay
{
    /// <summary>One click with the left button, which is what almost every step means.</summary>
    Single,

    /// <summary>Two clicks in quick succession, which is how a page that opens something in place works.</summary>
    Double,

    /// <summary>One click with the right button, which is what opens a page's own menu.</summary>
    Right,
}

/// <summary>How a step puts writing into an element of a page.</summary>
public enum FillWay
{
    /// <summary>Written in at once, which is quicker and is what a plain field needs.</summary>
    Fill,

    /// <summary>Typed one key at a time, which is what a page watching the typing itself needs.</summary>
    Type,
}

/// <summary>Which way a step moves the page, when what it is after is not on screen yet.</summary>
public enum ScrollWay
{
    /// <summary>To the element the target names, which is also what makes a lazy page load it.</summary>
    Element,

    /// <summary>One screen further down, which is how a page that adds more as it is read is walked.</summary>
    Screen,

    /// <summary>All the way down, to whatever the page has at its end.</summary>
    Bottom,

    /// <summary>Back to the top, which is what a step that reads a page from its start needs.</summary>
    Top,
}

/// <summary>
/// A table of a page: the names written above its columns, and its rows.
/// </summary>
/// <remarks>
/// Both are read here rather than being worked out by the caller, because "is the first row a strip
/// of names or the first row of data" is a question about the markup that only the page can answer;
/// which name a column goes by when the page writes none is the engine's own business, since it is
/// the same answer the table reader of a window gives.
/// </remarks>
public sealed record PageTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Where a page is and what it calls itself.</summary>
public sealed record PageInfo(string Url, string Title);

/// <summary>A file a page handed over: where it was saved, and what the page called it.</summary>
public sealed record DownloadResult(string Path, string Name);

/// <summary>
/// Which element of a page a step means, named the way a macro may name it.
/// </summary>
/// <remarks>
/// More than one selector is how a macro survives a page that has been redesigned: they are written
/// one to a line and tried in that order, and the first that finds anything is the one the step
/// works through. The selectors and <see cref="MatchIndex"/> travel together because the two answer
/// one question — which element — and a step that named no selector at all is a step that means the
/// page itself. <see cref="Conditions"/> belong to the same question: they are what tells one of
/// several elements that a selector names from the others, and they hold whichever of them the step
/// works through.
/// </remarks>
public sealed record PageTarget(
    IReadOnlyList<string> Selectors, int MatchIndex, IReadOnlyList<PageCondition> Conditions)
{
    /// <summary>
    /// The target a step's field holds: one selector to a line, blank lines dropped, and which of
    /// the elements a selector matches is the one meant, counted from 1, with the conditions the
    /// step adds when a selector on its own names more than the one it wants.
    /// </summary>
    public static PageTarget Of(string text, int matchIndex,
        IReadOnlyList<PageCondition>? conditions = null) => new(
            [
                .. text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0),
            ],
            matchIndex,
            conditions ?? []);
}

/// <summary>
/// One more thing the right element has to be, beyond the way it is named, which is what tells it
/// from the others when a selector names several that look alike.
/// </summary>
public sealed record PageCondition(PageFilter How, string Value);

/// <summary>
/// How a step narrows down the elements a selector names, told the way the browser engine itself
/// offers to do it rather than worked out here: every one of these is a question the engine behind
/// the page already answers, so a step is narrowed the same way a person programming the browser
/// would narrow it.
/// </summary>
/// <remarks>
/// All of them hold at once, which is why the words read as conditions rather than as choices. The
/// first of them is not a condition on the element but on where it sits, and the rest are what
/// Playwright calls the element's filters: text it writes, elements it holds, and whether it can be
/// seen. A page shows a thing once and keeps a second copy of it hidden, so "only the visible one"
/// is the condition a person reaches for most.
/// </remarks>
public enum PageFilter
{
    /// <summary>Look for the element inside the container this names, rather than anywhere on the page.</summary>
    Inside,

    /// <summary>The element holds an element this names.</summary>
    Has,

    /// <summary>The element holds nothing this names.</summary>
    HasNot,

    /// <summary>What the element writes holds this text, whichever case and however the spacing runs.</summary>
    HasText,

    /// <summary>What the element writes holds no such text.</summary>
    HasNotText,

    /// <summary>Only elements a person could see, which is what tells a shown thing from its hidden twin.</summary>
    Visible,
}

/// <summary>
/// Which kind of naming a picked selector is, so the interface can say it in the reader's own words
/// and nothing outside the device has to know how a page is asked about its elements.
/// </summary>
public enum PickKind
{
    /// <summary>A data attribute a site keeps for its own tests, which is meant to survive a redesign.</summary>
    DataAttribute,

    /// <summary>A role together with the name read off it, which is how a screen reader finds it.</summary>
    Role,

    /// <summary>
    /// The text of the <c>&lt;label&gt;</c> a form control belongs to, which is the way a person
    /// reads the field rather than the way the form submits it.
    /// </summary>
    Label,

    /// <summary>The text the element shows.</summary>
    Text,

    /// <summary>The grey hint a box shows before anything is typed into it.</summary>
    Placeholder,

    /// <summary>The words a picture is described by, for a reader who cannot see it.</summary>
    AltText,

    /// <summary>The words a tooltip shows, or the name a control offers when nothing else names it.</summary>
    Title,

    /// <summary>The element's id.</summary>
    Id,

    /// <summary>The name a form field is submitted under.</summary>
    NameAttribute,

    /// <summary>The label a screen reader reads out for the element.</summary>
    AriaLabel,

    /// <summary>A path of tags from the top of the page, which is what is left when nothing above fits.</summary>
    Path,
}

/// <summary>One way of naming the element a person picked, and how many elements of the page it names.</summary>
/// <remarks>
/// How many matters as much as which: a selector naming three elements is still usable, because the
/// step says which of them it means, while one that gets there by a path of tags is the first to
/// break when the page is redesigned. Someone writing a macro wants both of those in front of them
/// before choosing what to leave the step holding.
/// </remarks>
public sealed record PickCandidate(PickKind Kind, string Selector, int Matches);

/// <summary>
/// What a pick answered: the selector to write down — the steadiest of them — and every way of
/// naming the same element that the page offered. An empty selector means the person gave up or
/// clicked nothing in time, and then there is nothing to write down either.
/// </summary>
public sealed record PickResult(string Selector, IReadOnlyList<PickCandidate> Candidates);

/// <summary>
/// What outlining a target found: whether anything matched at all, which of the target's selectors
/// was the one that did, and how many elements it names.
/// </summary>
public sealed record HighlightResult(bool Found, int Line, int Matches);

/// <summary>Which tab of the open browser a macro asked to move to.</summary>
public enum TabChoice
{
    /// <summary>
    /// The one that appeared last, which is where a click that opened a tab leaves the person.
    /// </summary>
    Newest,

    /// <summary>By number, counted from the left of the tab strip starting at 1.</summary>
    Index,

    /// <summary>The first tab whose title contains a piece of text.</summary>
    Title,

    /// <summary>The first tab whose address contains a piece of text.</summary>
    Address,
}
