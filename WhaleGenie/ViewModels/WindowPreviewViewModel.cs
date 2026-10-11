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

    /// <summary>How many of them are listed: the rest are a line saying how many are left.</summary>
    private const int Shown = 5;

    private DispatcherTimer? _timer;

    /// <summary>The windows to read. Asked for at every read rather than kept, because the dialog
    /// hands them over once and a check may swap them out under it.</summary>
    private Func<IWindowDevice?>? _windows;

    /// <summary>The rule the list was last read for, so a keystroke in some other field costs nothing.</summary>
    private (string Text, WindowMatch Match, WindowCompare Compare)? _rule;

    /// <summary>The windows the rule recognises, up to <see cref="Shown"/> of them.</summary>
    public ObservableCollection<WindowPreviewRow> Rows { get; } = [];

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
    public void Follow(string text, WindowMatch match, WindowCompare compare)
    {
        var rule = ((text ?? string.Empty).Trim(), match, compare);
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
        _windows = null;
        _rule = null;
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
            return;
        }

        IReadOnlyList<WindowInfo> found;
        try
        {
            found = windows.Filter(rule.Text, rule.Match, rule.Compare);
        }
        catch (ArgumentException)
        {
            // The same words a run reports it with, because it is the same mistake.
            Summary = Strings.Format("Run.BadPattern", rule.Text);
            return;
        }
        catch (Exception)
        {
            // A machine that will not hand its windows over has none to offer, which is worth
            // saying rather than showing as "no window matches".
            Summary = Strings.Get("Add.WindowPreview.Unreadable");
            return;
        }

        Summary = found.Count > 0
            ? Strings.Format("Add.WindowPreview.Count", found.Count)
            : Strings.Get("Add.WindowPreview.None");

        Note = rule.Text.Length == 0
            ? Strings.Get("Add.WindowPreview.Front")
            : Expression.ReferencedNames(rule.Text).Count > 0
                ? Strings.Get("Add.WindowPreview.Variable")
                : string.Empty;

        foreach (var window in found.Take(Shown))
        {
            Rows.Add(Row(window, rule.Match, windows));
        }

        More = found.Count > Shown
            ? Strings.Format("Add.WindowPreview.More", found.Count - Shown)
            : string.Empty;
    }

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
