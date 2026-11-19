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

// The settings drawer's values: switch key, loop and timing choices, window target and hotstring.
public partial class MacroEditorViewModel
{
    /// <summary>True while the editor holds changes that have not been saved yet.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>
    /// True while the rows are being rebuilt. The list lets go of its selection when the rows
    /// under it are replaced, and that is not the user unpicking anything, so the view has to
    /// know to look past it — otherwise every edit would end with nothing selected.
    /// </summary>
    public bool IsRebuildingRows { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameProblem))]
    [NotifyPropertyChangedFor(nameof(HasNameProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>A sentence shown with a callable macro in pickers and libraries.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>
    /// The folder this macro is filed under, as the author spelled it. Empty means ungrouped; the
    /// main list offers the names that exist as a filter beside the fixed views.
    /// </summary>
    [ObservableProperty]
    public partial string Group { get; set; } = string.Empty;

    /// <summary>
    /// Which half of the drawer is showing. Switching pages is not an edit: the two pages say
    /// different things about the same macro.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSettingsPage))]
    [NotifyPropertyChangedFor(nameof(IsSpecificSettingsPage))]
    public partial MacroSettingsPage SettingsPage { get; set; } = MacroSettingsPage.General;

    public bool IsGeneralSettingsPage => SettingsPage == MacroSettingsPage.General;

    public bool IsSpecificSettingsPage => SettingsPage == MacroSettingsPage.Specific;

    /// <summary>Shows one page of the macro settings drawer.</summary>
    public void ShowSettingsPage(MacroSettingsPage page) => SettingsPage = page;

    /// <summary>
    /// True while this macro uses the program's input limit instead of one of its own. The number
    /// beside it keeps the program's current value, so turning this off starts from something the
    /// person can see rather than from a zero they did not choose.
    /// </summary>
    [ObservableProperty]
    public partial bool FollowProgramInputRate { get; set; } = true;

    /// <summary>Events per second this macro may send; only written to the macro when not following.</summary>
    [ObservableProperty]
    public partial double InputLimitValue { get; set; } = InputLimit.Usual;

    /// <summary>The key that arms or disarms this macro at runtime, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleKeyDisplay))]
    [NotifyPropertyChangedFor(nameof(TriggerProblemSummary))]
    public partial string ToggleKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleKeyButton))]
    public partial bool IsCapturingToggleKey { get; set; }

    /// <summary>Why the last attempt to use a switch key was refused, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToggleKeyProblem))]
    public partial string ToggleKeyProblem { get; set; } = string.Empty;

    public bool HasToggleKeyProblem => ToggleKeyProblem.Length > 0;

    public string ToggleKeyDisplay => ToggleKey.Trim().Length > 0
        ? ToggleKey
        : Strings.Get("Editor.NoKey");

    public string ToggleKeyButton => Strings.Get(IsCapturingToggleKey
        ? "Editor.HotstringEndCaptureWaiting"
        : "Editor.SetKey");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsColorTrigger))]
    [NotifyPropertyChangedFor(nameof(IsKeyTrigger))]
    [NotifyPropertyChangedFor(nameof(IsInterfaceTrigger))]
    [NotifyPropertyChangedFor(nameof(IsHotstringTrigger))]
    [NotifyPropertyChangedFor(nameof(IsMenuTrigger))]
    [NotifyPropertyChangedFor(nameof(IsTimerTrigger))]
    [NotifyPropertyChangedFor(nameof(IsFileTrigger))]
    [NotifyPropertyChangedFor(nameof(IsProcessTrigger))]
    [NotifyPropertyChangedFor(nameof(IsWindowTrigger))]
    [NotifyPropertyChangedFor(nameof(IsWindowRuleTrigger))]
    [NotifyPropertyChangedFor(nameof(IsIdleTrigger))]
    public partial TriggerKind TriggerMode { get; set; } = TriggerKind.Key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopIndex))]
    public partial MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

    public IReadOnlyList<string> LoopOptions =>
    [
        Strings.Get("Loop.Toggle"),
        Strings.Get("Loop.Hold"),
        Strings.Get("Loop.Press"),
        Strings.Get("Loop.Release"),
    ];

    /// <summary>True when the macro is started by a key or a mouse button.</summary>
    public bool IsKeyTrigger => TriggerMode == TriggerKind.Key;

    /// <summary>True when the macro is started by a colour / pixel change rather than by input.</summary>
    public bool IsColorTrigger => TriggerMode == TriggerKind.Color;

    public bool IsInterfaceTrigger => TriggerMode == TriggerKind.Interface;

    public bool IsHotstringTrigger => TriggerMode == TriggerKind.Hotstring;

    public bool IsMenuTrigger => TriggerMode == TriggerKind.Menu;

    /// <summary>True when the macro is started by the clock.</summary>
    public bool IsTimerTrigger => TriggerMode == TriggerKind.Timer;

    /// <summary>True when the macro is started by a file or a folder changing.</summary>
    public bool IsFileTrigger => TriggerMode == TriggerKind.File;

    /// <summary>True when the macro is started by a program starting or finishing.</summary>
    public bool IsProcessTrigger => TriggerMode == TriggerKind.Process;

    /// <summary>True when the macro is started by a window appearing or going away.</summary>
    public bool IsWindowTrigger => TriggerMode == TriggerKind.Window;

    public bool IsWindowRuleTrigger => TriggerMode is TriggerKind.Window or TriggerKind.Interface;

    /// <summary>True when the macro is started by the machine going untouched.</summary>
    public bool IsIdleTrigger => TriggerMode == TriggerKind.Idle;

    /// <summary>How long the machine has to be left alone before an idle trigger runs the macro.</summary>
    [ObservableProperty]
    public partial int? IdleSeconds { get; set; } = 60;

    /// <summary>The captured window and the rule a window trigger uses to recognise it again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowRuleNow))]
    [NotifyPropertyChangedFor(nameof(WindowTargetSummary))]
    public partial WindowRuleModel WindowTarget { get; set; } = WindowRuleModel.Empty;

    /// <summary>
    /// The windows this trigger would fire for right now, listed under the fields that say how the
    /// window is recognised. A window trigger is the easiest thing in a macro to get wrong, since a
    /// rule written a little too wide fires on a window nobody meant, and the list says what the
    /// rule comes to without waiting for it to happen.
    /// </summary>
    public WindowPreviewViewModel WindowPreview { get; } = new();

    /// <summary>The windows the list is read from, handed over by the editor that opened this.</summary>
    public IWindowDevice? Windows
    {
        get => _windows;
        set
        {
            _windows = value;
            WindowPreview.Watch(() => _windows);
        }
    }

    private IWindowDevice? _windows;

    /// <summary>The rule as the fields stand, which is what the preview of open windows follows.</summary>
    public WindowRuleModel WindowRuleNow => WindowTarget;

    public string WindowTargetSummary => WindowTarget.HasTarget
        ? $"{WindowTarget.MatchValue} · {Strings.Get($"WindowRule.Short.{WindowTarget.How}", WindowTarget.How)}"
        : SelectedTrigger?.Kind == TriggerKind.Interface
            ? Strings.Get("WindowRule.AnyWindow")
            : Strings.Get("WindowRule.NotSet");

    /// <summary>Stores the rule the trigger picker captured.</summary>
    public void UseWindowTarget(WindowRuleModel rule)
    {
        WindowTarget = rule;
        OnPropertyChanged(nameof(WindowRuleNow));
        OnPropertyChanged(nameof(WindowTargetSummary));
    }

    partial void OnWindowTargetChanged(WindowRuleModel value)
        => UpdateInterfaceGroupFields();

    partial void OnInterfaceToggleHotkeyChanged(string value)
    {
        if (!_loading)
        {
            RememberInterfaceHotkey();
            MarkDirty();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotstringTextDisplay))]
    [NotifyPropertyChangedFor(nameof(TriggerProblemSummary))]
    [NotifyPropertyChangedFor(nameof(HasTriggerProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string HotstringText { get; set; } = string.Empty;

    /// <summary>The literal abbreviation shown on the typed-string keyboard button.</summary>
    public string HotstringTextDisplay => HotstringText.Length > 0
        ? HotstringCharacters.Display(HotstringText)
        : Strings.Get("Editor.HotstringChoose");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerProblemSummary))]
    [NotifyPropertyChangedFor(nameof(HasTriggerProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string HotstringEndChars { get; set; } = HotstringCharacters.DefaultEndChars;

    public int HotstringMaximumLength => HotstringCharacters.MaximumLength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotstringEndCaptureButton))]
    public partial bool IsCapturingHotstringEnd { get; set; }

    [ObservableProperty]
    public partial string HotstringEndCaptureStatus { get; set; } = string.Empty;

    public string HotstringEndCaptureButton => Strings.Get(IsCapturingHotstringEnd
        ? "Editor.HotstringEndCaptureWaiting"
        : "Editor.HotstringEndCapture");

    partial void OnHotstringEndCharsChanged(string value)
    {
        if (!_updatingHotstringEndKeys)
        {
            RebuildHotstringEndKeys();
        }
    }

    [RelayCommand]
    private void ResetHotstringEndChars()
    {
        HotstringEndChars = HotstringCharacters.DefaultEndChars;
        MarkDirty();
    }

    public void BeginHotstringEndCapture()
    {
        StopCapturingKey();
        HotstringEndCaptureStatus = string.Empty;
        IsCapturingHotstringEnd = true;
    }

    public void StopHotstringEndCapture()
    {
        IsCapturingHotstringEnd = false;
        HotstringEndCaptureStatus = string.Empty;
    }

    public void CaptureHotstringEnd(string? character)
    {
        if (!IsCapturingHotstringEnd)
        {
            return;
        }

        if (character is not { Length: 1 } || !HotstringCharacters.IsAllowed(character[0]))
        {
            HotstringEndCaptureStatus = Strings.Get("Editor.HotstringEndCaptureInvalid");
            return;
        }

        var existing = HotstringEndKeys.FirstOrDefault(option => option.Value == character);
        if (existing is not null)
        {
            existing.IsChecked = true;
        }
        else
        {
            AddHotstringEndKey(character);
        }

        IsCapturingHotstringEnd = false;
        HotstringEndCaptureStatus = string.Empty;
    }

    private void AddHotstringEndKey(string value)
    {
        var values = HotstringCharacters.ParseEndChars(HotstringEndChars)
            .Append(value)
            .Distinct(StringComparer.Ordinal);
        _updatingHotstringEndKeys = true;
        try
        {
            HotstringEndChars = HotstringCharacters.WriteEndChars(values);
        }
        finally
        {
            _updatingHotstringEndKeys = false;
        }
        RebuildHotstringEndKeys();
    }

    private void RebuildHotstringEndKeys()
    {
        foreach (var option in HotstringEndKeys)
        {
            option.PropertyChanged -= OnHotstringEndKeyChanged;
        }
        HotstringEndKeys.Clear();

        var selected = HotstringCharacters.ParseEndChars(HotstringEndChars);
        foreach (var (name, value) in HotstringCharacters.EndKeyChoices)
        {
            AddHotstringEndOption(new HotstringEndKeyOption(value, name, isCustom: false)
            {
                IsChecked = selected.Contains(value),
            });
        }

        var standard = HotstringCharacters.EndKeyChoices
            .Select(choice => choice.Value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var value in selected.Where(value => !standard.Contains(value))
                     .Order(StringComparer.Ordinal))
        {
            AddHotstringEndOption(new HotstringEndKeyOption(
                value, HotstringCharacters.Display(value), isCustom: true)
            {
                IsChecked = true,
            });
        }
    }

    private void AddHotstringEndOption(HotstringEndKeyOption option)
    {
        option.PropertyChanged += OnHotstringEndKeyChanged;
        HotstringEndKeys.Add(option);
    }

    private void OnHotstringEndKeyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingHotstringEndKeys || e.PropertyName != nameof(HotstringEndKeyOption.IsChecked))
        {
            return;
        }

        _updatingHotstringEndKeys = true;
        try
        {
            HotstringEndChars = HotstringCharacters.WriteEndChars(
                HotstringEndKeys.Where(option => option.IsChecked).Select(option => option.Value));
        }
        finally
        {
            _updatingHotstringEndKeys = false;
        }
    }

    [ObservableProperty]
    public partial bool HotstringCaseSensitive { get; set; }

    [ObservableProperty]
    public partial bool HotstringInsideWord { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerProblemSummary))]
    [NotifyPropertyChangedFor(nameof(HasTriggerProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool HotstringRequireEnd { get; set; } = true;

    [ObservableProperty]
    public partial bool HotstringEraseTyped { get; set; } = true;

    [ObservableProperty]
    public partial bool HotstringKeepEnd { get; set; } = true;

    public IReadOnlyList<string> WindowChangeOptions =>
    [
        Strings.Get("WindowChange.Appeared"),
        Strings.Get("WindowChange.Disappeared"),
        Strings.Get("WindowChange.Any"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowChangeIndex))]
    public partial WindowChangeKind WindowChange { get; set; } = WindowChangeKind.Appeared;

    public int WindowChangeIndex
    {
        get => (int)WindowChange;
        set
        {
            if (value >= 0 && value < WindowChangeOptions.Count)
            {
                WindowChange = (WindowChangeKind)value;
            }
        }
    }

    /// <summary>The program a process trigger waits for, without the ".exe".</summary>
    [ObservableProperty]
    public partial string ProcessName { get; set; } = string.Empty;

    public IReadOnlyList<string> ProcessChangeOptions =>
    [
        Strings.Get("ProcessChange.Started"),
        Strings.Get("ProcessChange.Stopped"),
        Strings.Get("ProcessChange.Any"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessChangeIndex))]
    public partial ProcessChangeKind ProcessChange { get; set; } = ProcessChangeKind.Started;

    public int ProcessChangeIndex
    {
        get => (int)ProcessChange;
        set
        {
            if (value >= 0 && value < ProcessChangeOptions.Count)
            {
                ProcessChange = (ProcessChangeKind)value;
            }
        }
    }

    /// <summary>The file or folder a file trigger watches.</summary>
    [ObservableProperty]
    public partial string WatchPath { get; set; } = string.Empty;

    public IReadOnlyList<string> WatchChangeOptions =>
    [
        Strings.Get("FileChange.Any"),
        Strings.Get("FileChange.Created"),
        Strings.Get("FileChange.Changed"),
        Strings.Get("FileChange.Deleted"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchChangeIndex))]
    public partial FileChangeKind WatchChange { get; set; } = FileChangeKind.Any;

    public int WatchChangeIndex
    {
        get => (int)WatchChange;
        set
        {
            if (value >= 0 && value < WatchChangeOptions.Count)
            {
                WatchChange = (FileChangeKind)value;
            }
        }
    }

    /// <summary>The names watched inside a watched folder, such as <c>*.txt</c>.</summary>
    [ObservableProperty]
    public partial string WatchFilter { get; set; } = "*";

    /// <summary>Whether a watched folder also reports what happens in the folders inside it.</summary>
    [ObservableProperty]
    public partial bool WatchSubfolders { get; set; }

    public IReadOnlyList<string> ScheduleOptions =>
    [
        Strings.Get("Schedule.Interval"),
        Strings.Get("Schedule.Daily"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleIndex))]
    [NotifyPropertyChangedFor(nameof(IsIntervalSchedule))]
    public partial ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Interval;

    public int ScheduleIndex
    {
        get => (int)ScheduleMode;
        set
        {
            if (value >= 0 && value < ScheduleOptions.Count)
            {
                ScheduleMode = (ScheduleMode)value;
            }
        }
    }

    /// <summary>True while the schedule waits a fixed gap rather than naming a time of day.</summary>
    public bool IsIntervalSchedule => ScheduleMode == ScheduleMode.Interval;

    /// <summary>How many seconds, minutes or hours an interval schedule waits.</summary>
    [ObservableProperty]
    public partial int? ScheduleInterval { get; set; } = 5;

    public IReadOnlyList<string> ScheduleUnitOptions =>
    [
        Strings.Get("Schedule.Unit.Seconds"),
        Strings.Get("Schedule.Unit.Minutes"),
        Strings.Get("Schedule.Unit.Hours"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleUnitIndex))]
    public partial ScheduleUnit ScheduleUnit { get; set; } = ScheduleUnit.Seconds;

    public int ScheduleUnitIndex
    {
        get => (int)ScheduleUnit;
        set
        {
            if (value >= 0 && value < ScheduleUnitOptions.Count)
            {
                ScheduleUnit = (ScheduleUnit)value;
            }
        }
    }

    /// <summary>The time of day a daily schedule runs at, written <c>08:30</c>.</summary>
    [ObservableProperty]
    public partial string ScheduleTime { get; set; } = "08:00";

    public int LoopIndex
    {
        get => (int)LoopMode;
        set
        {
            if (value >= 0 && value <= (int)MacroLoop.Release)
            {
                LoopMode = (MacroLoop)value;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BindKeyDisplay))]
    public partial string BindKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetKeyLabel))]
    public partial bool IsCapturingKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordLabel))]
    [NotifyPropertyChangedFor(nameof(RecordStatusText))]
    public partial bool IsRecording { get; set; }

    /// <summary>How much has been captured since recording started, for the status line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordStatusText))]
    public partial int RecordedEvents { get; set; }

    /// <summary>The recorder asks for the count to be shown as it grows.</summary>
    public void ReportRecordedEvents(int count) => RecordedEvents = count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionCaptureIndex))]
    public partial MousePositionMode PositionCapture { get; set; } = MousePositionMode.SaveCurrentPosition;

    public IReadOnlyList<string> PositionCaptureOptions =>
    [
        Strings.Get("Position.Current"),
        Strings.Get("Position.Differences"),
    ];

    public int PositionCaptureIndex
    {
        get => (int)PositionCapture;
        set
        {
            if (value >= 0 && value <= (int)MousePositionMode.SavePositionDifferences)
            {
                PositionCapture = (MousePositionMode)value;
            }
        }
    }

    public IReadOnlyList<string> ColorMatchOptions =>
    [
        Strings.Get("ColorMatch.Matches"),
        Strings.Get("ColorMatch.NotMatches"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorMatchIndex))]
    public partial ColorMatchCondition ColorMatch { get; set; } = ColorMatchCondition.ColorMatches;

    public int ColorMatchIndex
    {
        get => (int)ColorMatch;
        set
        {
            if (value >= 0 && value <= (int)ColorMatchCondition.ColorNotMatches)
            {
                ColorMatch = (ColorMatchCondition)value;
            }
        }
    }

    /// <summary>Horizontal screen position watched when a colour / pixel trigger is used.</summary>
    [ObservableProperty]
    public partial string ColorPositionX { get; set; } = "0";

    /// <summary>Vertical screen position watched when a colour / pixel trigger is used.</summary>
    [ObservableProperty]
    public partial string ColorPositionY { get; set; } = "0";

    /// <summary>When set, the watched position follows the cursor instead of the stored coordinates.</summary>
    [ObservableProperty]
    public partial bool UseCursorPosition { get; set; }

    /// <summary>When set, the macro runs only once instead of repeating.</summary>
    [ObservableProperty]
    public partial bool TriggerOnce { get; set; }

    /// <summary>Colour watched for the trigger, as a hex string (for example <c>#000000</c>).</summary>
    [ObservableProperty]
    public partial string HexColor { get; set; } = "#000000";

    /// <summary>Allowed colour difference (percent) before the watched pixel counts as a match.</summary>
    [ObservableProperty]
    public partial string ColorTolerance { get; set; } = "5";

    [ObservableProperty]
    public partial bool RecordMouseMovement { get; set; } = true;

    [ObservableProperty]
    public partial bool RecordMouseButtons { get; set; } = true;

    [ObservableProperty]
    public partial bool RecordKeyboardKeys { get; set; } = true;

    /// <summary>Whether the buttons of the controller in the person's hands are recorded.</summary>
    [ObservableProperty]
    public partial bool RecordGamepadButtons { get; set; } = true;

    [ObservableProperty]
    public partial bool RecordDelays { get; set; } = true;

    [ObservableProperty]
    public partial decimal? IgnoreDelaysBelowMs { get; set; } = 50;

    /// <summary>
    /// When set, input another program sends is left out of the recording. It is not part of the
    /// macro: the panel starts from the program's own answer for ignoring its input, and what is
    /// changed here applies to the next recording made in this window.
    /// </summary>
    [ObservableProperty]
    public partial bool RecordIgnoreInjected { get; set; } = true;

    /// <summary>
    /// How much longer or shorter this macro's waits are made when it runs. 1 leaves everything
    /// as written; it bends the timings only while running, so the steps keep their own numbers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelayScaleDisplay))]
    [NotifyPropertyChangedFor(nameof(HasDelayScale))]
    public partial double DelayScale { get; set; } = 1;

    /// <summary>The speed setting as it is shown, for example <c>×2</c>.</summary>
    public string DelayScaleDisplay => FormatScale(DelayScale);

    /// <summary>True when the macro runs at something other than its written speed.</summary>
    public bool HasDelayScale => Math.Abs(DelayScale - 1) > 0.001;

    /// <summary>Writes a speed factor the way the interface shows it.</summary>
    public static string FormatScale(double scale) => DelayScaleViewModel.FormatScale(scale);
}
