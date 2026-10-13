using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>One window a step would recognise right now, as the preview lists it.</summary>
public sealed record WindowPreviewRow(string Title, string PartLabel, string Part)
{
    /// <summary>True when what is compared is a part of the window other than its title.</summary>
    public bool HasPart => Part.Length > 0;
}

/// <summary>
/// The rule a step recognises windows with, as its fields stand: the text, the part of a window it
/// is held against, how it is compared, and what an empty text means to this step. A step that acts
/// on one window means the one in front by saying nothing; a step that lists them means every window
/// there is, and the list has to say the same thing the run would do.
/// </summary>
public readonly record struct WindowRule(string Text, WindowMatch Match, WindowCompare Compare,
    bool EmptyIsEvery);

/// <summary>
/// The windows a step recognises with the rule it is written with right now, shown under the fields
/// that make up that rule. "Any window whose title matches this" is easy to write and hard to trust:
/// whether it picks up the game window or the debugger beside it is otherwise only visible by running
/// the macro, which is a slow way to find out. The rule is read off the fields as they are typed, so
/// what the list says is what the run would do.
/// </summary>
public partial class WindowPreviewViewModel : ViewModelBase
{
    /// <summary>
    /// How long the rule may keep changing before the windows are read again. Reading them walks the
    /// desktop, which is not something to do once per keystroke.
    /// </summary>
    internal static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How often the frames are put back where their windows are now. Windows get moved while a rule
    /// is being written — that is usually why it is being written — and a frame left where a window
    /// used to be points at nothing.
    /// </summary>
    internal static TimeSpan FollowInterval { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>How many of them are listed: the rest are a line saying how many are left.</summary>
    private const int Shown = 5;

    private DispatcherTimer? _timer;

    private DispatcherTimer? _follow;

    /// <summary>The windows to read. Asked for at every read rather than kept, because the dialog
    /// hands them over once and a check may swap them out under it.</summary>
    private Func<IWindowDevice?>? _windows;

    /// <summary>The rule the list was last read for, so a keystroke in some other field costs nothing.</summary>
    private WindowRule? _rule;

    /// <summary>Whether the list is on the screen at all, which the dialog says: a rule the step does
    /// not read has nothing to frame either.</summary>
    private bool _shown;

    private IReadOnlyList<FramePlace> _places = [];

    /// <summary>The windows the list is showing, in the order it shows them, so that following them
    /// around moves the frames of the listed windows rather than of whatever matches now.</summary>
    private readonly List<long> _framed = [];

    /// <summary>The windows the rule recognises, up to <see cref="Shown"/> of them.</summary>
    public ObservableCollection<WindowPreviewRow> Rows { get; } = [];

    /// <summary>
    /// Where the windows it lists sit on the screen, so that whoever is showing the list can put a
    /// frame around each of them: a title in a list says which window, and a frame on it says where
    /// it is, which is what makes the list worth trusting at a glance. Empty while the list is not
    /// on screen, and for the windows with no place to draw a frame — see <see cref="FramePlace.Of"/>.
    /// </summary>
    public IReadOnlyList<FramePlace> Places
    {
        get => _places;
        private set
        {
            if (_places.SequenceEqual(value))
            {
                return;
            }

            _places = value;
            OnPropertyChanged();
        }
    }

    /// <summary>How many windows there are, or why there are none to show.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>What else is worth knowing: a text left empty, a variable in it, a pattern that will
    /// not read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>How many windows the rule recognises beyond the ones listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    public partial string More { get; set; } = string.Empty;

    public bool HasNote => Note.Length > 0;

    public bool HasMore => More.Length > 0;

    /// <summary>Where the windows come from, which the dialog knows and this does not.</summary>
    public void Watch(Func<IWindowDevice?> windows) => _windows = windows;

    /// <summary>
    /// Hands the preview the rule as the fields now stand. The first one is read at once, because
    /// opening a step should show what it recognises; a rule that changes while it is being written
    /// waits for the typing to pause.
    /// </summary>
    public void Follow(WindowRule rule)
    {
        rule = rule with { Text = rule.Text.Trim() };
        if (_rule == rule)
        {
            return;
        }

        var first = _rule is null;
        _rule = rule;

        if (first)
        {
            Read();
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += (_, _) => Read();
        }

        _timer.Interval = Interval;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Reads the windows again at once, which is what the button beside the list is for: a
    /// window opened since the last look, or a rule edited a moment ago.</summary>
    [RelayCommand]
    private void Refresh() => Read();

    /// <summary>Stops watching, for a dialog that has moved on to another action.</summary>
    public void Stop()
    {
        _timer?.Stop();
        _follow?.Stop();
        _windows = null;
        _rule = null;
        _shown = false;
        Places = [];
    }

    /// <summary>
    /// Says whether the list itself is on the screen. A window named to count numbers from, or to
    /// post input at, is only read while that is what the step does, so the frames come and go with
    /// the list rather than standing over a rule nothing reads.
    /// </summary>
    public void Show(bool shown)
    {
        if (_shown == shown)
        {
            return;
        }

        _shown = shown;
        if (shown && _rule is not null)
        {
            // Coming back to a rule it has already read: the places went with the list, so the
            // windows are read again rather than left unframed.
            Read();
        }
        else if (!shown)
        {
            _follow?.Stop();
            Places = [];
        }
    }

    /// <summary>
    /// Puts the frames back where the listed windows are now. Only where they are: the list itself is
    /// what the 刷新 button reads again, so a line does not jump about under the pointer while the
    /// rule is being written. A window that has gone has nowhere left to be framed.
    /// </summary>
    private void Follow()
    {
        if (_rule is not { } rule || _windows?.Invoke() is not { } windows)
        {
            return;
        }

        IReadOnlyList<WindowInfo> found;
        try
        {
            found = Look(windows, rule);
        }
        catch (Exception)
        {
            // The rule's own read has already said what is wrong with it, and that answer does not
            // change by asking again every moment.
            return;
        }

        var byHandle = new Dictionary<long, WindowInfo>();
        foreach (var window in found)
        {
            byHandle[window.Handle] = window;
        }

        var places = new List<FramePlace>();
        foreach (var handle in _framed)
        {
            if (byHandle.TryGetValue(handle, out var window) && FramePlace.Of(window) is { } place)
            {
                places.Add(place);
            }
        }

        Places = _shown ? places : [];
    }

    private void Read()
    {
        _timer?.Stop();
        Rows.Clear();
        More = string.Empty;
        Note = string.Empty;

        if (_rule is not { } rule || _windows?.Invoke() is not { } windows)
        {
            Summary = Strings.Get("Add.WindowPreview.Unreadable");
            Places = [];
            return;
        }

        IReadOnlyList<WindowInfo> found;
        try
        {
            found = Look(windows, rule);
        }
        catch (ArgumentException)
        {
            // The same words a run reports it with, because it is the same mistake.
            Summary = Strings.Format("Run.BadPattern", rule.Text);
            Places = [];
            return;
        }
        catch (Exception)
        {
            // A machine that will not hand its windows over has none to offer, which is worth
            // saying rather than showing as "no window matches".
            Summary = Strings.Get("Add.WindowPreview.Unreadable");
            Places = [];
            return;
        }

        Summary = found.Count > 0
            ? Strings.Format("Add.WindowPreview.Count", found.Count)
            : Strings.Get("Add.WindowPreview.None");

        Note = rule.Text.Length == 0
            ? Strings.Get(rule.EmptyIsEvery ? "Add.WindowPreview.Every" : "Add.WindowPreview.Front")
            : Expression.ReferencedNames(rule.Text).Count > 0
                ? Strings.Get("Add.WindowPreview.Variable")
                : string.Empty;

        foreach (var window in found.Take(Shown))
        {
            Rows.Add(Row(window, rule.Match, windows));
        }

        var listed = found.Take(Shown).ToList();
        _framed.Clear();
        _framed.AddRange(listed.Select(window => window.Handle));
        Places = _shown ? [.. listed.Select(FramePlace.Of).OfType<FramePlace>()] : [];

        // The frames are worth keeping where the windows are for as long as the list is up: a window
        // that is dragged somewhere else while the rule is being written would otherwise leave the
        // frame pointing at where it used to be.
        if (_shown && Places.Count > 0)
        {
            _follow ??= StartFollowing();
            _follow.Interval = FollowInterval;
            _follow.Stop();
            _follow.Start();
        }

        More = found.Count > Shown
            ? Strings.Format("Add.WindowPreview.More", found.Count - Shown)
            : string.Empty;
    }

    /// <summary>The clock that puts the frames back where their windows are.</summary>
    private DispatcherTimer StartFollowing()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => Follow();
        return timer;
    }

    /// <summary>
    /// The windows the rule picks out right now, read the way the run reads them — the same call,
    /// with the same answer for a text nobody wrote. A pattern that will not read comes back as an
    /// <see cref="ArgumentException"/> for whoever asked to report.
    /// </summary>
    private static IReadOnlyList<WindowInfo> Look(IWindowDevice windows, WindowRule rule)
        => windows.Filter(rule.Text, rule.Match, rule.Compare, rule.EmptyIsEvery);

    /// <summary>One line of the list: the title, and the part compared when that is not the title.</summary>
    private static WindowPreviewRow Row(WindowInfo window, WindowMatch match, IWindowDevice windows)
        => match switch
        {
            WindowMatch.Process => new WindowPreviewRow(window.Title,
                Strings.Get("WindowLookup.Process"), windows.ProcessOf(window.Handle)),
            WindowMatch.ClassName => new WindowPreviewRow(window.Title,
                Strings.Get("WindowLookup.ClassName"), windows.ClassOf(window.Handle)),
            _ => new WindowPreviewRow(window.Title, string.Empty, string.Empty),
        };
}
