using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// The keys the program listens for: record stop, the system hotkey and what is being captured.
public partial class SettingsViewModel
{
    /// <summary>
    /// The shortcut that ends a recording. It is what the recording panel's hint says, so changing
    /// it here changes the words there as well.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordStopDisplay))]
    public partial string RecordStopKey { get; set; } = AppSettings.Shared.LoadRecordStopKey();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordStopButton))]
    public partial bool IsCapturingRecordStop { get; set; }

    [ObservableProperty]
    public partial string RecordStopProblem { get; set; } = string.Empty;

    public string RecordStopDisplay => RecordStopKey.Length == 0
        ? Strings.Get("Common.None")
        : RecordStopKey;

    public string RecordStopButton => Strings.Get(
        IsCapturingRecordStop ? "Settings.HotkeyPress" : "Settings.HotkeySet");

    /// <summary>
    /// Takes the shortcut just pressed. An empty one is refused rather than stored: a recording
    /// whose only way out is the mouse keeps writing while the pointer goes looking for the button.
    /// </summary>
    public bool CaptureRecordStop(string binding)
    {
        var text = binding.Trim();
        if (text.Length == 0 || !HotkeyBinding.IsSupported(text))
        {
            IsCapturingRecordStop = false;
            RecordStopProblem = Strings.Get("Settings.HotkeyUnsupported");
            return false;
        }

        IsCapturingRecordStop = false;
        RecordStopProblem = string.Empty;
        RecordStopKey = text;
        _settings.StoreRecordStopKey(text);
        return true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemHotkeyButton))]
    public partial bool IsCapturingSystemHotkey { get; set; }

    [ObservableProperty]
    public partial string SystemHotkeyProblem { get; set; } = string.Empty;

    public bool IsCapturingHotkey => IsCapturingSystemHotkey || IsCapturingMenuLauncher
        || IsCapturingRecordStop || IsCapturingWheelRepeat;

    public string SystemHotkeyDisplay => SystemHotkey.Length == 0
        ? Strings.Get("Common.None")
        : SystemHotkey;

    public string SystemHotkeyButton => Strings.Get(
        IsCapturingSystemHotkey ? "Settings.HotkeyPress" : "Settings.HotkeySet");

    partial void OnSystemHotkeyChanged(string value)
    {
        _settings.StoreSystemHotkey(value);
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    partial void OnIsCapturingSystemHotkeyChanged(bool value)
        => OnPropertyChanged(nameof(IsCapturingHotkey));

    partial void OnIsCapturingMenuLauncherChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCapturingHotkey));
        OnPropertyChanged(nameof(MenuLauncherCustom));
    }

    partial void OnIsCapturingRecordStopChanged(bool value)
        => OnPropertyChanged(nameof(IsCapturingHotkey));

    /// <summary>Stores whether the next start asks for the administrator token.</summary>
    partial void OnRunAsAdminChanged(bool value)
    {
        if (value != _settings.LoadRunAsAdmin())
        {
            _settings.StoreRunAsAdmin(value);
        }
    }

    public bool CaptureSystemHotkey(string binding)
    {
        var text = binding.Trim();
        if (text.Length > 0 && !HotkeyBinding.IsSupported(text))
        {
            IsCapturingSystemHotkey = false;
            SystemHotkeyProblem = Strings.Get("Settings.HotkeyUnsupported");
            return false;
        }

        // The menu can be opened with a keyboard chord as well as a mouse button, and a key that
        // is both the system switch and the menu trigger would do one of the two jobs only.
        if (text.Length > 0 && MenuLauncher.Length > 0
            && string.Equals(text, MenuLauncher, StringComparison.OrdinalIgnoreCase))
        {
            IsCapturingSystemHotkey = false;
            SystemHotkeyProblem = Strings.Get("Settings.HotkeyConflictMenu");
            return false;
        }

        if (text.Length > 0 && WheelLauncher.Length > 0
            && string.Equals(text, WheelLauncher, StringComparison.OrdinalIgnoreCase))
        {
            IsCapturingSystemHotkey = false;
            SystemHotkeyProblem = Strings.Get("Settings.HotkeyConflictWheel");
            return false;
        }

        IsCapturingSystemHotkey = false;
        SystemHotkeyProblem = string.Empty;
        SystemHotkey = text;
        return true;
    }

    public void ClearSystemHotkey()
    {
        IsCapturingSystemHotkey = false;
        SystemHotkeyProblem = string.Empty;
        SystemHotkey = string.Empty;
    }
}
