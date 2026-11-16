using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// Capturing a key or a hotstring end, and what the record button reports while it runs.
public partial class MacroEditorViewModel
{
    /// <summary>Applies a new speed factor, keeping it inside the range a run accepts.</summary>
    public void SetDelayScale(double scale)
    {
        var clamped = double.IsFinite(scale) ? Math.Clamp(scale, 0.1, 10) : 1;
        if (Math.Abs(clamped - DelayScale) < 0.001)
        {
            return;
        }

        PushUndo();
        DelayScale = clamped;
        MarkDirty();
    }

    public string BindKeyDisplay => string.IsNullOrEmpty(BindKey) ? Strings.Get("Editor.NoKey") : BindKey;

    public string SetKeyLabel => IsCapturingKey ? Strings.Get("Editor.PressKey") : Strings.Get("Editor.SetKey");

    partial void OnTriggerModeChanged(TriggerKind value)
    {
        if (!_loading && SelectedTrigger is { } selected)
        {
            selected.Kind = value;
        }

        OnPropertyChanged(nameof(IsKeyTrigger));
        OnPropertyChanged(nameof(IsColorTrigger));
        OnPropertyChanged(nameof(IsInterfaceTrigger));
        OnPropertyChanged(nameof(IsHotstringTrigger));
        OnPropertyChanged(nameof(IsMenuTrigger));
        OnPropertyChanged(nameof(IsTimerTrigger));
        OnPropertyChanged(nameof(IsFileTrigger));
        OnPropertyChanged(nameof(IsProcessTrigger));
        OnPropertyChanged(nameof(IsWindowTrigger));
        OnPropertyChanged(nameof(IsWindowRuleTrigger));
        OnPropertyChanged(nameof(IsIdleTrigger));
        OnPropertyChanged(nameof(ShowsTriggerSettings));
        OnPropertyChanged(nameof(ShowsLoopMode));
        OnPropertyChanged(nameof(ShowsTriggerOnce));
        UpdateInterfaceGroupFields();
    }

    /// <summary>Copies the fields of the form into the selected trigger entry.</summary>
    private void CommitTriggerFields()
    {
        if (SelectedTrigger is not { } entry)
        {
            return;
        }

        entry.Kind = TriggerMode;
        entry.MacroName = Name.Trim();
        entry.BindKey = BindKey.Trim();
        entry.LoopMode = LoopMode;
        entry.TriggerOnce = TriggerOnce;
        entry.ScheduleMode = ScheduleMode;
        entry.ScheduleInterval = Math.Max(1, ScheduleInterval ?? 5);
        entry.ScheduleUnit = ScheduleUnit;
        entry.ScheduleTime = ScheduleTime.Trim();
        entry.WatchPath = WatchPath.Trim();
        entry.WatchChange = WatchChange;
        entry.WatchFilter = WatchFilter.Trim().Length == 0 ? "*" : WatchFilter.Trim();
        entry.WatchSubfolders = WatchSubfolders;
        entry.ProcessName = ProcessName.Trim();
        entry.ProcessChange = ProcessChange;
        entry.WindowTarget = WindowTarget;
        entry.WindowChange = WindowChange;
        entry.IdleSeconds = Math.Max(1, IdleSeconds ?? 60);
        entry.ColorMatch = ColorMatch;
        entry.ColorPositionX = ParseCoordinate(ColorPositionX);
        entry.ColorPositionY = ParseCoordinate(ColorPositionY);
        entry.UseCursorPosition = UseCursorPosition;
        entry.HexColor = HexColor;
        entry.ColorTolerance = ParseCoordinate(ColorTolerance);
        entry.Abbreviation = HotstringText;
        entry.HotstringEndChars = HotstringEndChars;
        entry.CaseSensitive = HotstringCaseSensitive;
        entry.MatchInsideWord = HotstringInsideWord;
        entry.RequireEndChar = HotstringRequireEnd;
        entry.EraseTyped = HotstringEraseTyped;
        entry.KeepEndChar = HotstringKeepEnd;
    }

    /// <summary>Brings the trigger form onto the entry selected in the list.</summary>
    private void LoadTriggerFields(TriggerEntry? entry)
    {
        _loading = true;
        try
        {
            TriggerMode = entry?.Kind ?? TriggerKind.Key;
            BindKey = entry?.BindKey ?? string.Empty;
            LoopMode = entry?.LoopMode ?? MacroLoop.Toggle;
            TriggerOnce = entry?.TriggerOnce ?? false;
            ScheduleMode = entry?.ScheduleMode ?? ScheduleMode.Interval;
            ScheduleInterval = entry?.ScheduleInterval ?? 5;
            ScheduleUnit = entry?.ScheduleUnit ?? ScheduleUnit.Seconds;
            ScheduleTime = entry?.ScheduleTime ?? "08:00";
            WatchPath = entry?.WatchPath ?? string.Empty;
            WatchChange = entry?.WatchChange ?? FileChangeKind.Any;
            WatchFilter = entry?.WatchFilter ?? "*";
            WatchSubfolders = entry?.WatchSubfolders ?? false;
            ProcessName = entry?.ProcessName ?? string.Empty;
            ProcessChange = entry?.ProcessChange ?? ProcessChangeKind.Started;
            WindowTarget = entry?.WindowTarget ?? WindowRule.Empty;
            WindowChange = entry?.WindowChange ?? WindowChangeKind.Appeared;
            IdleSeconds = entry?.IdleSeconds ?? 60;
            ColorMatch = entry?.ColorMatch ?? ColorMatchCondition.ColorMatches;
            ColorPositionX = (entry?.ColorPositionX ?? 0).ToString(CultureInfo.InvariantCulture);
            ColorPositionY = (entry?.ColorPositionY ?? 0).ToString(CultureInfo.InvariantCulture);
            UseCursorPosition = entry?.UseCursorPosition ?? false;
            HexColor = entry?.HexColor ?? "#000000";
            ColorTolerance = (entry?.ColorTolerance ?? 5).ToString(CultureInfo.InvariantCulture);
            HotstringText = entry?.Abbreviation ?? string.Empty;
            HotstringEndChars = entry?.HotstringEndChars ?? HotstringCharacters.DefaultEndChars;
            HotstringCaseSensitive = entry?.CaseSensitive ?? false;
            HotstringInsideWord = entry?.MatchInsideWord ?? false;
            HotstringRequireEnd = entry?.RequireEndChar ?? true;
            HotstringEraseTyped = entry?.EraseTyped ?? true;
            HotstringKeepEnd = entry?.KeepEndChar ?? true;
        }
        finally
        {
            _loading = false;
        }
    }

    public string RecordLabel => IsRecording ? Strings.Get("Editor.StopRecording") : Strings.Get("Editor.Record");

    /// <summary>
    /// Hint under the record button, so the stop shortcut stays discoverable. The key is read from
    /// the settings every time it is built: changing it there changes this line, and a line that
    /// named a fixed key would send people to the wrong one.
    /// </summary>
    public string RecordStatusText => IsRecording
        ? Strings.Format("Editor.RecordActive", RecordedEvents, LocalSettings.LoadRecordStopKey())
        : Strings.Format("Editor.RecordIdle", LocalSettings.LoadRecordStopKey());

    /// <summary>The record button's tooltip, naming the same shortcut the line under it names.</summary>
    public string RecordStartTip
        => Strings.Format("Editor.Tip.Record", LocalSettings.LoadRecordStopKey());

    /// <summary>True while the current capture only takes keys, not a mouse button or a pad button.</summary>
    internal bool CaptureKeyboardOnly => _captureKeyboardOnly;

    /// <summary>True while a single key is taken rather than a combination.</summary>
    internal bool CaptureSingleKey => _captureSingleKey;

    /// <summary>
    /// Starts a capture for the inspector's key field. The key is handed to <paramref name="deliver"/>
    /// instead of the trigger, and only the keyboard is listened to: a key field cannot hold a
    /// mouse button. A combination takes the modifiers with the key, the way a trigger does.
    /// </summary>
    internal bool BeginKeyCapture(Action<string> deliver, bool combination, bool keyboardOnly = true)
    {
        if (IsRecording)
        {
            return false;
        }

        _keyCaptureTarget = deliver;
        _captureKeyboardOnly = keyboardOnly;
        _captureSingleKey = !combination;
        IsCapturingKey = true;
        return true;
    }

    /// <summary>Records the key pressed while the editor is waiting for a binding.</summary>
    public void CaptureKey(string keyName)
    {
        if (!IsCapturingKey)
        {
            return;
        }

        var deliver = _keyCaptureTarget;
        StopCapturingKey();
        if (deliver is not null)
        {
            deliver(keyName);
            return;
        }

        BindKey = keyName;
    }

    /// <summary>Ends a capture and forgets whatever it was going to write into.</summary>
    private void StopCapturingKey()
    {
        IsCapturingKey = false;
        IsCapturingInterfaceHotkey = false;
        IsCapturingToggleKey = false;
        _keyCaptureTarget = null;
        _captureKeyboardOnly = false;
        _captureSingleKey = false;
    }
}
