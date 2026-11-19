using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// A browser driven through Playwright. Edge is used by default, because Windows already has it:
/// a macro that just wants to fill a form in does not have to fetch a browser first. The engines
/// Playwright fetches for itself are still offered, and the device says whether they are there
/// before a macro tries one, so the user gets the command that installs it rather than a failure
/// from somewhere inside the driver.
///
/// Playwright's own calls are asynchronous and come back on the context they were started from,
/// which is the interface thread while a macro is running. Every call is therefore handed to the
/// thread pool and waited on there, the same way the VIIPER client is, so a blocking wait can
/// never keep the answer from arriving.
/// </summary>
public sealed partial class PlaywrightBrowserDevice : IBrowserDevice, IDisposable
{
    /// <summary>
    /// How a macro writes the way in to a frame: the selector of the frame element itself, then the
    /// rest of the line. A line may say it several times over, outermost frame first.
    /// </summary>
    private const string FrameStep = "frame=";

    /// <summary>
    /// What is written between one part of a line and the next. Playwright reads it as a chain of
    /// its own, which is why a line a macro wrote by hand keeps whichever chain it asked for.
    /// </summary>
    private const string Chain = " >> ";

    /// <summary>The four ways of naming an element that are written rather than read as a selector.</summary>
    private static readonly string[] Semantic = ["label=", "placeholder=", "alt=", "title="];

    /// <summary>
    /// How long a check waits for the element it was handed. It is a question about a page that is
    /// already open, so waiting the half minute a macro's own step waits would leave the person
    /// staring at a dialog for a selector that is simply not there.
    /// </summary>
    private const float HighlightTimeoutMs = 3000;

    /// <summary>
    /// How long the page's own default is left standing while an element is looked at once rather
    /// than acted on. A look asks the page what it holds right now, so the number only has to be
    /// long enough for the question to be answered at all; anything longer would be a wait written
    /// into a place that promised not to wait.
    /// </summary>
    private const float LookTimeoutMs = 2000;

    /// <summary>
    /// How often a target written as several selectors is tried while none of them has found
    /// anything yet. It is a look at a page that is already in memory, so it costs next to nothing;
    /// waiting a moment between two looks is what keeps a page that never shows the element from
    /// being asked hundreds of times a second.
    /// </summary>
    private const int CandidateIntervalMs = 100;

    /// <summary>
    /// How long a switch to the newest tab waits for one to turn up. A click that opens a tab has
    /// answered by the time it comes back, but the tab itself reaches Playwright a moment later —
    /// tens of milliseconds on this machine — and a site that opens its tab after fetching
    /// something takes longer still.
    /// </summary>
    /// <remarks>
    /// A check sets it: waiting the whole way to see what happens when nothing opens would be five
    /// seconds of the test run for an answer that is already known.
    /// </remarks>
    internal static TimeSpan NewTabWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often that wait looks whether the tab has turned up.</summary>
    private const int NewTabIntervalMs = 50;

    /// <summary>
    /// How long a wait for an element waits when the step hands over no length of its own. It is
    /// Playwright's own default, kept here so a wait written into the device rather than into a step
    /// does not quietly become a wait of no length at all.
    /// </summary>
    private const int DefaultWaitMs = 30000;

    /// <summary>
    /// The keys whose spelling differs between a macro and the page's own keyboard. Everything else
    /// a macro can write — the letters, the digits, the function keys, and the keys both sides spell
    /// the same way ("Enter", "PageUp") — is handed over as it stands, so only the differences are
    /// written down here.
    /// </summary>
    private static readonly Dictionary<string, string> PageKeyNames = new(StringComparer.Ordinal)
    {
        ["Ctrl"] = "Control",
        ["Win"] = "Meta",
        ["Esc"] = "Escape",
        ["Up"] = "ArrowUp",
        ["Down"] = "ArrowDown",
        ["Left"] = "ArrowLeft",
        ["Right"] = "ArrowRight",
        ["-"] = "Minus",
        ["="] = "Equal",
        [","] = "Comma",
        ["."] = "Period",
        ["/"] = "Slash",
        [";"] = "Semicolon",
        ["'"] = "Quote",
        ["`"] = "Backquote",
        ["["] = "BracketLeft",
        ["]"] = "BracketRight",
        ["\\"] = "Backslash",
    };

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    /// <summary>
    /// The tabs this browser has, oldest first, as they were written down when they turned up.
    /// </summary>
    /// <remarks>
    /// Playwright hands over the pages that are open, but "which of these is the newest" is what a
    /// macro needs after a click opened a tab, and the order of that list is not something
    /// Playwright promises. Written down here as they turn up, the last one added is the newest
    /// whatever order they come in; the closed ones are dropped on every look.
    /// </remarks>
    private readonly List<IPage> _tabs = [];

    /// <summary>Handed the selector the person clicked, by the page's own script.</summary>
    private TaskCompletionSource<string>? _picked;

    /// <summary>Handed the frame that answered, so the frame can be written into the selector.</summary>
    private IFrame? _answeredFrame;

    /// <summary>Whether the picker's bindings and init script have been installed on this page.</summary>
    private bool _listening;

    /// <summary>
    /// The files the page has handed over that no step has taken yet, oldest first.
    /// </summary>
    /// <remarks>
    /// A download nobody catches is thrown away by the driver, so what turns up is kept here whether
    /// or not a step is waiting: a page often starts one from a click, and the step that wants it is
    /// written after that click. A lock, because the event arrives on whichever thread the driver
    /// dispatches on while the step that takes it waits on another.
    /// </remarks>
    private readonly Queue<IDownload> _caught = new();

    /// <summary>
    /// The files a step has already taken, so that one is never handed over twice.
    /// </summary>
    /// <remarks>
    /// A download is seen twice: once by the listener that catches everything, and once by the wait
    /// a step put up for the file its own click started. Without this, the file that wait took would
    /// still be sitting in the caught list, and the step after it would be handed a file somebody
    /// else already saved.
    /// </remarks>
    private readonly HashSet<IDownload> _taken = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Wakes a step that is waiting for the next file as soon as the listener catches one.
    /// </summary>
    /// <remarks>
    /// The wait is on this rather than on the driver's next download event, because a file the page
    /// started a moment ago reaches the listener a few milliseconds after the call that started it
    /// returned. Waiting only for the next event would then time out while the file was already in
    /// hand — which is exactly what a step that presses "export" in one action and waits in the
    /// next one looks like under load.
    /// </remarks>
    private readonly SemaphoreSlim _arrivals = new(0);

    /// <summary>Everything the dialog listener and the steps that arm and take answers share.</summary>
    /// <remarks>
    /// A dialog arrives on the driver's own reader while the step that armed the answer, and the one
    /// that takes what it said, are on other threads: one lock rather than four pieces of state that
    /// could be read half-written.
    /// </remarks>
    private readonly object _dialogs = new();

    /// <summary>Whether the next dialog coming up is being answered by the running step.</summary>
    private bool _dialogArmed;

    /// <summary>Whether that answer accepts the dialog rather than dismissing it.</summary>
    private bool _dialogAccepted;

    /// <summary>What to type into a dialog that asks for some, while one is armed.</summary>
    private string _dialogText = string.Empty;

    /// <summary>What the answered dialog said, until the step that armed the answer takes it.</summary>
    private string? _dialogMessage;

    /// <summary>Whether the browser was opened without a window.</summary>
    private bool _headless;
}
