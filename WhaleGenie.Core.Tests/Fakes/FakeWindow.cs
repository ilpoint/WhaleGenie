using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend window list and its window commands: what is open, what each window answers to, and
/// where each client area sits.
/// </summary>
internal sealed class FakeWindow(FakeDesk desk) : IWindowDevice
{
    /// <summary>The windows the fake reports as open.</summary>
    public List<WindowInfo> Open { get; } = [];

    /// <summary>
    /// The process and class names the fake windows answer to. A real device looks these up from
    /// the machine, so a test that matches that way has to say what should come back.
    /// </summary>
    public Dictionary<long, (string Process, string ClassName)> Traits { get; } = [];

    /// <summary>Whether the next window operation reports that it worked.</summary>
    public bool ActionWorks { get; set; } = true;

    /// <summary>A window title that only shows up once the window list has been read a few times.</summary>
    public string? AppearsLater { get; set; }

    /// <summary>How many reads happen before <see cref="AppearsLater"/> shows up.</summary>
    public int AppearsAfter { get; set; } = 1;

    private int _reads;

    /// <summary>
    /// Where each window's client area starts, by handle. A handle that was not set up answers
    /// the window's own corner, which is what a window with no border looks like.
    /// </summary>
    public Dictionary<long, ScreenPoint> ClientOrigins { get; } = [];

    /// <summary>
    /// How big each window's client area is, by handle. A handle that was not set up answers the
    /// window's own size, which is what a window with no border looks like.
    /// </summary>
    public Dictionary<long, ScreenSize> ClientSizes { get; } = [];

    public IReadOnlyList<WindowInfo> List()
    {
        desk.Note("listWindows");
        return [.. Open];
    }

    public WindowInfo? Find(string value, WindowMatch match, string compare,
        TextCompareOptions options)
    {
        desk.Note($"findWindow {value}");
        _reads++;
        if (AppearsLater is not null
            && _reads > AppearsAfter
            && Open.All(window => window.Title != AppearsLater))
        {
            Open.Add(new WindowInfo(99, AppearsLater,
                new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));
        }

        return Matching(value, match, compare, options: options).FirstOrDefault();
    }

    public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match,
        string compare, bool everyWhenEmpty, TextCompareOptions options)
    {
        desk.Note($"filterWindows {value}");
        return Matching(value, match, compare, everyWhenEmpty, options);
    }

    public string ProcessOf(long handle)
    {
        desk.Note($"processOf {handle}");
        return Open.FirstOrDefault(window => window.Handle == handle) is { } window
            ? FactsOf(window).Process
            : string.Empty;
    }

    public string ClassOf(long handle)
    {
        desk.Note($"classOf {handle}");
        return Open.FirstOrDefault(window => window.Handle == handle) is { } window
            ? FactsOf(window).ClassName
            : string.Empty;
    }

    public bool Activate(long handle)
    {
        desk.Note($"activateWindow {handle}");
        return ActionWorks;
    }

    public bool Minimize(long handle)
    {
        desk.Note($"minimizeWindow {handle}");
        return ActionWorks;
    }

    public bool Maximize(long handle)
    {
        desk.Note($"maximizeWindow {handle}");
        return ActionWorks;
    }

    public bool Restore(long handle)
    {
        desk.Note($"restoreWindow {handle}");
        return ActionWorks;
    }

    public bool Close(long handle)
    {
        desk.Note($"closeWindow {handle}");
        return ActionWorks;
    }

    public bool Move(long handle, int x, int y, int width, int height)
    {
        desk.Note($"moveWindow {handle} {x} {y} {width} {height}");
        return ActionWorks;
    }

    public ScreenPoint ClientOrigin(long handle)
    {
        desk.Note($"clientOrigin {handle}");
        return ClientOrigins.TryGetValue(handle, out var origin)
            ? origin
            : Open.FirstOrDefault(window => window.Handle == handle)?.Location ?? default;
    }

    public ScreenSize ClientSize(long handle)
    {
        desk.Note($"clientSize {handle}");
        var window = Open.FirstOrDefault(candidate => candidate.Handle == handle);
        return ClientSizes.TryGetValue(handle, out var size)
            ? size
            : window?.Size ?? default;
    }

    /// <summary>
    /// The windows that answer to a value, in the order the fake lists them. A value nobody wrote
    /// means the window in front, which is what a step that names none acts on; a step that lists
    /// windows means every one of them by it.
    /// </summary>
    private List<WindowInfo> Matching(string value, WindowMatch match, string compare,
        bool everyWhenEmpty = false, TextCompareOptions options = default)
    {
        var wanted = (value ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return everyWhenEmpty || Open.Count == 0 ? [.. Open] : [Open[0]];
        }

        return [.. Open.Where(window => Answers(window, wanted, match, compare, options))];
    }

    /// <summary>
    /// Whether a fake window answers to a value. A title is always there; the process name and the
    /// class name have to be handed in through <see cref="Traits"/>, the way the real device has to
    /// look them up. How the two are held up against each other is the device's job, so it is the
    /// same reading the real device does rather than a second one that could disagree.
    /// </summary>
    private bool Answers(WindowInfo window, string wanted, WindowMatch match, string compare,
        TextCompareOptions options)
    {
        if (match is WindowMatch.Handle)
        {
            return long.TryParse(wanted, out var handle) && window.Handle == handle;
        }

        var text = match switch
        {
            WindowMatch.Process => FactsOf(window).Process,
            WindowMatch.ClassName => FactsOf(window).ClassName,
            _ => window.Title,
        };

        return WindowHow.Matches(text, wanted, compare, options);
    }

    private (string Process, string ClassName) FactsOf(WindowInfo window)
        => Traits.TryGetValue(window.Handle, out var facts)
            ? facts
            : (string.Empty, string.Empty);
}
