using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>One open window, as the picker lists it.</summary>
public sealed record WindowEntry(long Handle, string Title, string Process, string Detail)
{
    /// <summary>What the row shows underneath the title: the program, then how big the window is.</summary>
    public string Summary => Process.Length == 0 ? Detail : $"{Process} · {Detail}";
}

/// <summary>
/// Backs the window picker: what is open right now, narrowed down by what the user types.
/// A macro matches a window by part of its title, so picking one writes the title out and
/// leaves it editable.
/// </summary>
public partial class WindowPickerViewModel : ViewModelBase
{
    private readonly List<WindowEntry> _all;

    /// <summary>Raised with the window title that was picked, or null when nothing was.</summary>
    public event Action<string?>? CloseRequested;

    public WindowPickerViewModel()
        : this(null)
    {
    }

    /// <summary>Creates the picker over a given list, or over the windows the desktop has open.</summary>
    public WindowPickerViewModel(IReadOnlyList<WindowEntry>? windows)
    {
        _all = [.. windows ?? ReadWindows()];
        Reapply();
    }

    /// <summary>The windows the filter lets through, in the order the desktop listed them.</summary>
    public ObservableCollection<WindowEntry> Windows { get; } = [];

    /// <summary>What the user has typed to narrow the list down.</summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>The window that would be taken if the user confirmed now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    public partial WindowEntry? Selected { get; set; }

    public string Header => Strings.Get("WindowPicker.Title");

    public bool HasWindows => Windows.Count > 0;

    /// <summary>What an empty list means: nothing matched the filter, or nothing was found at all.</summary>
    public string EmptyMessage => Strings.Get(_all.Count == 0 ? "WindowPicker.Empty" : "WindowPicker.None");

    /// <summary>Re-reads the windows that are open, for the case where one was opened meanwhile.</summary>
    [RelayCommand]
    private void Reload()
    {
        _all.Clear();
        _all.AddRange(ReadWindows());
        Reapply();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Choose() => CloseRequested?.Invoke(Selected?.Title);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    private bool HasSelection => Selected is not null;

    partial void OnFilterChanged(string value) => Reapply();

    /// <summary>Rebuilds the visible list, keeping the chosen row if it is still there.</summary>
    private void Reapply()
    {
        var wanted = Filter.Trim();
        var keep = Selected;

        Windows.Clear();
        foreach (var entry in _all.Where(entry => wanted.Length == 0 || Matches(entry, wanted)))
        {
            Windows.Add(entry);
        }

        Selected = keep is not null && Windows.Contains(keep) ? keep : Windows.FirstOrDefault();

        OnPropertyChanged(nameof(HasWindows));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private static bool Matches(WindowEntry entry, string text)
        => entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
           || entry.Process.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every visible top-level window, with the program behind it as a label.</summary>
    private static List<WindowEntry> ReadWindows()
    {
        IReadOnlyList<WindowInfo> windows;
        try
        {
            windows = new WindowsWindowDevice().List();
        }
        catch (Exception)
        {
            // A machine that will not hand its windows over simply has nothing to offer.
            return [];
        }

        return [.. windows.Select(window => new WindowEntry(
            window.Handle,
            window.Title,
            WindowsWindowDevice.ProcessName(window.Handle),
            Describe(window)))];
    }

    private static string Describe(WindowInfo window)
    {
        var state = window.Minimized
            ? "WindowPicker.Minimized"
            : window.Maximized
                ? "WindowPicker.Maximized"
                : "WindowPicker.Normal";

        return $"{Strings.Format("WindowPicker.Size", window.Size.Width, window.Size.Height)}"
               + $" · {Strings.Get(state)}";
    }
}
