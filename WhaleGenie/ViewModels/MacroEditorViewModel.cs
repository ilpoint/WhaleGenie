using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

public partial class MacroEditorViewModel : ViewModelBase
{
    /// <summary>Raised when the editor should close, with the macro to save or <c>null</c> when cancelled.</summary>
    public event Action<MacroItem?>? CloseRequested;

    /// <summary>Parses the SVG-style path data used by the action palette icons.</summary>
    private static Geometry G(string path) => StreamGeometry.Parse(path);

    /// <summary>Action types offered by the Action Editor palette.</summary>
    public IReadOnlyList<MacroAction> ActionPalette { get; } =
    [
        new() { Icon = G("M12,5 L12,19 M5,12 L19,12"),
                LabelKey = "Palette.Add", Key = "add" },

        new() { Icon = G("M9,5 L5,9 L9,13 M5,9 H14 A5,5 0 0 1 14,19 H9"),
                LabelKey = "Palette.Undo", Key = "undo", NeedsUndo = true },

        new() { Icon = G("M15,5 L19,9 L15,13 M19,9 H10 A5,5 0 0 0 10,19 H15"),
                LabelKey = "Palette.Redo", Key = "redo", NeedsRedo = true },

        new() { Icon = G("M4,20 L4,16 L16,4 L20,8 L8,20 Z M14,6 L18,10"),
                LabelKey = "Palette.Edit", Key = "edit", NeedsSingleSelection = true },

        new() { Icon = G("M12,3 L12,11 M7.2,6.2 A7,7 0 1 0 16.8,6.2"),
                LabelKey = "Palette.ToggleEnabled", Key = "toggleEnabled", NeedsSelection = true },

        // Copy borrows the two-sheets glyph: with the duplicate entry gone there is one copy
        // button, and this reads as "a copy of the steps" more plainly than a single sheet.
        new() { Icon = G("M8,8 L20,8 L20,20 L8,20 Z M4,16 L4,4 L16,4 L16,6"),
                LabelKey = "Palette.Copy", Key = "copy", NeedsSelection = true },

        new() { Icon = G("M6.5,4 L16,15 M17.5,4 L8,15 M3.5,18.5 A2.5,2.5 0 1 1 8.5,18.5 A2.5,2.5 0 1 1 3.5,18.5 M15.5,18.5 A2.5,2.5 0 1 1 20.5,18.5 A2.5,2.5 0 1 1 15.5,18.5"),
                LabelKey = "Palette.Cut", Key = "cut", NeedsSelection = true },

        // A plain clipboard, the usual "paste" glyph. The old one was a clipboard with a
        // downward arrow, which reads as "download" and had nothing to do with the step list.
        new() { Icon = G("M9,3 L15,3 L15,5 L9,5 Z M5,5 L19,5 L19,21 L5,21 Z"),
                LabelKey = "Palette.Paste", Key = "paste" },

        new() { Icon = G("M4,7 L20,7 M10,11 L10,17 M14,11 L14,17 M6,7 L7,21 L17,21 L18,7 M9,7 L9,4 L15,4 L15,7"),
                LabelKey = "Palette.Delete", Key = "delete", NeedsSelection = true },

        new() { Icon = G("M3,6 L9,6 L11,8 L21,8 L21,19 L3,19 Z"),
                LabelKey = "Palette.Group",
                Key = "group", NeedsSelection = true },

        new() { Icon = G("M12,19 L12,5 M6,11 L12,5 L18,11"),
                LabelKey = "Palette.MoveUp", Key = "moveUp", NeedsSelection = true },

        new() { Icon = G("M12,5 L12,19 M6,13 L12,19 L18,13"),
                LabelKey = "Palette.MoveDown", Key = "moveDown", NeedsSelection = true },

        new() { Icon = G("M5,5 L19,19 M19,5 L5,19"),
                LabelKey = "Palette.Clear", Key = "clear", NeedsSteps = true },

        // A bug, because this is the entry that starts debugging the macro.
        new() { Icon = G("M12,6.5 A5.5,5.5 0 1 0 12,17.5 A5.5,5.5 0 1 0 12,6.5 "
                         + "M12,6.5 L12,17.5 "
                         + "M9,7.4 L8,4.6 M15,7.4 L16,4.6 "
                         + "M6.7,10.4 L4.3,9.7 M6.7,13.6 L4.3,14.3 "
                         + "M17.3,10.4 L19.7,9.7 M17.3,13.6 L19.7,14.3"),
                LabelKey = "Palette.Run", Key = "run", NeedsSteps = true, IsPrimary = true },

        new() { Icon = G("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,7 L12,12 L15,14"),
                LabelKey = "Palette.AdjustDelays", Key = "adjustDelays",
                NeedsSteps = true },

        new() { Icon = G("M4,5 H20 V19 H4 Z M10,9 L15,12 L10,15 Z"),
                LabelKey = "Palette.RunMacro", Key = "runMacro" },
    ];

    /// <summary>Palette id of the entry that opens the add-action dialog.</summary>
    private const string AddStepPaletteKey = "add";

    private readonly List<MacroStep> _selection = [];

    /// <summary>
    /// Where the next step the dialog builds is to land, remembered between the press that asked
    /// for it and the dialog coming back with an answer. The step that owns the list comes along
    /// because a list may only take certain kinds of step — a switch's branches, or a condition.
    /// Null means the end of the macro.
    /// </summary>
    private (MacroStep Owner, StepParameter Parameter, int Index)? _insertAt;

    private bool _loading;

    /// <summary>How many step-list changes can be undone.</summary>
    private const int HistoryLimit = 60;

    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];

    /// <summary>
    /// The steps and the selection as they were before one change. The selection is kept as the
    /// positions the steps are written in, counting nested ones in the order the list shows them,
    /// because the history holds copies of the steps rather than the steps themselves.
    /// </summary>
    private sealed record HistoryEntry(
        IReadOnlyList<MacroStep> Steps, IReadOnlyList<int> Selection, double DelayScale);

    public MacroEditorViewModel()
    {
        RefreshPaletteState();
    }

    /// <summary>Raised when the editor should open the "Add Action" dialog.</summary>
    public event Action? AddStepRequested;

    /// <summary>Raised when the editor should open the edit dialog for an existing step.</summary>
    public event Action<MacroStep>? EditStepRequested;

    /// <summary>Raised when the palette asks to clear the list, so the window can confirm it.</summary>
    public event Action? ClearRequested;

    /// <summary>Raised when the palette or a shortcut asks to copy the selected steps.</summary>
    public event Action? CopyRequested;

    /// <summary>Raised when the palette or a shortcut asks to cut the selected steps.</summary>
    public event Action? CutRequested;

    /// <summary>Raised when the palette or a shortcut asks to paste below the selection.</summary>
    public event Action? PasteRequested;

    /// <summary>Raised when the editor should open the run window for the current steps.</summary>
    public event Action? RunRequested;

    /// <summary>Raised when the palette asks to open the run-speed dialog.</summary>
    public event Action? AdjustDelaysRequested;

    /// <summary>Raised when the palette asks for a step that runs another macro.</summary>
    public event Action? RunMacroRequested;

    /// <summary>
    /// Raised when recording starts or stops, so the window can put the input hook up or take
    /// it down. <c>true</c> means a recording is beginning.
    /// </summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>
    /// Raised when the list should re-apply the current selection, for example after
    /// a move or a duplicate, where the items change identity or position.
    /// </summary>
    public event Action? SelectionRefreshRequested;

    /// <summary>Steps built so far; saved as the macro's JSON tree.</summary>
    public ObservableCollection<MacroStep> Steps { get; } = [];

    /// <summary>
    /// The same steps as the editor shows them: a block that is open shows the steps inside it
    /// indented underneath, with a line where it starts and a line where it ends. The list is
    /// flat because that is what the control and every command work on, and a row carries the
    /// depth and the list it belongs to, so the shape of the macro can be read off one list.
    /// </summary>
    public ObservableCollection<StepRow> Rows { get; } = [];

    public bool HasSteps => Steps.Count > 0;

    /// <summary>
    /// Names of the other macros in the project. A step that calls a macro is checked against
    /// these, so a call left pointing at a name that has gone is called out here rather than
    /// waiting for the trigger to fire. The macro being edited is added by the check itself,
    /// because a macro may call itself.
    /// </summary>
    private IReadOnlyList<string> _projectMacros = [];

    /// <summary>What the design-time check has to say about the steps as they stand now.</summary>
    public IReadOnlyList<MacroProblem> Problems { get; private set; } = [];

    public bool HasProblems => Problems.Count > 0;

    /// <summary>One line above the list saying how much the editor has to say.</summary>
    public string ProblemSummary => Strings.Format("Editor.ProblemSummary",
        Problems.Select(problem => problem.Step).Distinct().Count());

    /// <summary>Which steps the check is unhappy about, and what it says about each one.</summary>
    private Dictionary<MacroStep, string> _problemByStep = [];

    /// <summary>Hands in the macro names a call may reach, and checks the steps again.</summary>
    public void SetProjectMacros(IEnumerable<string> names)
    {
        _projectMacros = [.. names];
        RebuildRows();
    }

    public bool HasSelection => _selection.Count > 0;

    /// <summary>The steps highlighted in the action list.</summary>
    public IReadOnlyList<MacroStep> SelectedSteps => _selection;

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
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerIndex))]
    [NotifyPropertyChangedFor(nameof(IsColorTrigger))]
    [NotifyPropertyChangedFor(nameof(IsKeyTrigger))]
    [NotifyPropertyChangedFor(nameof(IsTimerTrigger))]
    [NotifyPropertyChangedFor(nameof(IsFileTrigger))]
    [NotifyPropertyChangedFor(nameof(IsProcessTrigger))]
    [NotifyPropertyChangedFor(nameof(IsWindowTrigger))]
    [NotifyPropertyChangedFor(nameof(IsIdleTrigger))]
    public partial MacroTrigger TriggerMode { get; set; } = MacroTrigger.KeystrokesButtonInputs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopIndex))]
    public partial MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

    public IReadOnlyList<string> TriggerOptions { get; } =
    [
        Strings.Get("Trigger.Keys"),
        Strings.Get("Trigger.Color"),
        Strings.Get("Trigger.Timer"),
        Strings.Get("Trigger.File"),
        Strings.Get("Trigger.Process"),
        Strings.Get("Trigger.Window"),
        Strings.Get("Trigger.Idle"),
    ];

    public IReadOnlyList<string> LoopOptions { get; } =
    [
        Strings.Get("Loop.Toggle"),
        Strings.Get("Loop.Hold"),
        Strings.Get("Loop.Press"),
        Strings.Get("Loop.Release"),
    ];

    public int TriggerIndex
    {
        get => (int)TriggerMode;
        set
        {
            if (value >= 0 && value < TriggerOptions.Count)
            {
                TriggerMode = (MacroTrigger)value;
            }
        }
    }

    /// <summary>True when the macro is started by a key or a mouse button.</summary>
    public bool IsKeyTrigger => TriggerMode == MacroTrigger.KeystrokesButtonInputs;

    /// <summary>True when the macro is started by a colour / pixel change rather than by input.</summary>
    public bool IsColorTrigger => TriggerMode == MacroTrigger.ColorPixelChanges;

    /// <summary>True when the macro is started by the clock.</summary>
    public bool IsTimerTrigger => TriggerMode == MacroTrigger.Timer;

    /// <summary>True when the macro is started by a file or a folder changing.</summary>
    public bool IsFileTrigger => TriggerMode == MacroTrigger.FileChanges;

    /// <summary>True when the macro is started by a program starting or finishing.</summary>
    public bool IsProcessTrigger => TriggerMode == MacroTrigger.Process;

    /// <summary>True when the macro is started by a window appearing or going away.</summary>
    public bool IsWindowTrigger => TriggerMode == MacroTrigger.Window;

    /// <summary>True when the macro is started by the machine going untouched.</summary>
    public bool IsIdleTrigger => TriggerMode == MacroTrigger.Idle;

    /// <summary>How long the machine has to be left alone before an idle trigger runs the macro.</summary>
    [ObservableProperty]
    public partial int? IdleSeconds { get; set; } = 60;

    /// <summary>What a window trigger compares against the open windows.</summary>
    [ObservableProperty]
    public partial string WindowValue { get; set; } = string.Empty;

    public IReadOnlyList<string> WindowLookupOptions { get; } =
    [
        Strings.Get("WindowLookup.Title"),
        Strings.Get("WindowLookup.Process"),
        Strings.Get("WindowLookup.ClassName"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowLookupIndex))]
    public partial WindowMatch WindowLookup { get; set; } = WindowMatch.Title;

    public int WindowLookupIndex
    {
        get => (int)WindowLookup;
        set
        {
            if (value >= 0 && value < WindowLookupOptions.Count)
            {
                WindowLookup = (WindowMatch)value;
            }
        }
    }

    public IReadOnlyList<string> WindowChangeOptions { get; } =
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

    public IReadOnlyList<string> ProcessChangeOptions { get; } =
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

    public IReadOnlyList<string> WatchChangeOptions { get; } =
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

    public IReadOnlyList<string> ScheduleOptions { get; } =
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

    public IReadOnlyList<string> ScheduleUnitOptions { get; } =
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

    public IReadOnlyList<string> PositionCaptureOptions { get; } =
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

    public IReadOnlyList<string> ColorMatchOptions { get; } =
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

    [ObservableProperty]
    public partial bool RecordDelays { get; set; } = true;

    [ObservableProperty]
    public partial decimal? IgnoreDelaysBelowMs { get; set; } = 50;

    /// <summary>When set, input another program sends is left out of the recording.</summary>
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

    public string RecordLabel => IsRecording ? Strings.Get("Editor.StopRecording") : Strings.Get("Editor.Record");

    /// <summary>Hint under the record button, so the stop shortcuts stay discoverable.</summary>
    public string RecordStatusText => IsRecording
        ? Strings.Format("Editor.RecordActive", RecordedEvents)
        : Strings.Get("Editor.RecordIdle");

    /// <summary>Records the key pressed while the editor is waiting for a binding.</summary>
    public void CaptureKey(string keyName)
    {
        if (!IsCapturingKey)
        {
            return;
        }

        BindKey = keyName;
        IsCapturingKey = false;
    }

    [RelayCommand]
    private void AddAction(MacroAction? action)
    {
        if (action is null || !action.IsEnabled)
        {
            return;
        }

        switch (action.Key)
        {
            case AddStepPaletteKey:
                NewStepCommand.Execute(null);
                break;
            case "edit":
                EditSelectedCommand.Execute(null);
                break;
            case "toggleEnabled":
                ToggleStepEnabledCommand.Execute(null);
                break;
            case "undo":
                UndoCommand.Execute(null);
                break;
            case "redo":
                RedoCommand.Execute(null);
                break;
            case "copy":
                CopyRequested?.Invoke();
                break;
            case "cut":
                CutRequested?.Invoke();
                break;
            case "paste":
                PasteRequested?.Invoke();
                break;
            case "delete":
                DeleteSelectedCommand.Execute(null);
                break;
            case "group":
                GroupSelectedCommand.Execute(null);
                break;
            case "moveUp":
                MoveSelectedUpCommand.Execute(null);
                break;
            case "moveDown":
                MoveSelectedDownCommand.Execute(null);
                break;
            case "clear":
                ClearStepsCommand.Execute(null);
                break;
            case "run":
                RunRequested?.Invoke();
                break;
            case "adjustDelays":
                AdjustDelaysRequested?.Invoke();
                break;
            case "runMacro":
                RunMacroRequested?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Asks the window to open the "Add Action" dialog. The new step goes below whatever is
    /// picked, inside the block it sits in, which is where a step added while reading a block
    /// is wanted: with nothing picked it goes at the end of the macro.
    /// </summary>
    [RelayCommand]
    private void NewStep()
    {
        _insertAt = InsertionPoint();
        AddStepRequested?.Invoke();
    }

    /// <summary>
    /// Asks the window to open the dialog for a step to go at the end of one of a block's lists,
    /// which is what the "add" button on a head row does.
    /// </summary>
    [RelayCommand]
    private void AddInside(StepRow? row)
    {
        if (row?.List is not { } parameter)
        {
            return;
        }

        _insertAt = (row.Step, parameter, parameter.Steps.Count);
        AddStepRequested?.Invoke();
    }

    /// <summary>
    /// What the "Add Action" dialog may offer for the step being built: a switch's branch list
    /// only takes branches, a condition list only takes conditions, everything else takes
    /// anything. Read by the window when it opens the dialog.
    /// </summary>
    public IReadOnlyList<ActionDefinition>? InsertChoices => _insertAt is { } at
        ? ChoicesFor(at.Owner, at.Parameter)
        : null;

    private static IReadOnlyList<ActionDefinition>? ChoicesFor(MacroStep owner, StepParameter parameter)
    {
        if (parameter.Kind is ActionParameterKind.Condition)
        {
            return ActionCatalog.Conditions;
        }

        var definition = owner.Definition?.Parameters
            .FirstOrDefault(candidate => candidate.Name == parameter.Name);

        return definition is { ChildKeys.Count: > 0 } ? ActionCatalog.ForKeys(definition.ChildKeys) : null;
    }

    /// <summary>Folds a block open or shut, so a long macro can be read at the level wanted.</summary>
    [RelayCommand]
    private void ToggleFold(StepRow? row)
    {
        if (row is null)
        {
            return;
        }

        SetExpanded(row.Step, !row.Step.IsExpanded);
    }

    /// <summary>
    /// Shows or hides the steps inside a block. Folding a block the selection was inside takes
    /// the selection with it — onto the block itself — because steps that are not on screen
    /// must not be the ones a delete or a cut acts on.
    /// </summary>
    public void SetExpanded(MacroStep step, bool expanded)
    {
        step.IsExpanded = expanded;
        RebuildRows();

        var visible = Rows.Where(row => row.IsStep).Select(row => row.Step).ToHashSet();
        if (_selection.Any(selected => !visible.Contains(selected)))
        {
            var kept = _selection.Where(visible.Contains).ToList();
            _selection.Clear();
            _selection.AddRange(kept.Count > 0 ? kept : [step]);
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedSteps));
            RefreshPaletteState();
        }

        SelectionRefreshRequested?.Invoke();
    }

    /// <summary>Asks the window to open the run window for the steps as they are now.</summary>
    [RelayCommand]
    private void Run() => RunRequested?.Invoke();

    /// <summary>Asks the window to open the edit dialog for the selected step.</summary>
    [RelayCommand]
    private void EditSelected()
    {
        if (_selection.Count == 1)
        {
            EditStepRequested?.Invoke(_selection[0]);
        }
    }

    /// <summary>
    /// Turns the selected steps on or off. When any of them is already off they all come
    /// back on, so one press always changes something.
    /// </summary>
    [RelayCommand]
    private void ToggleStepEnabled()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0)
        {
            return;
        }

        PushUndo();

        var enable = selected.Any(step => !step.Meta.IsEnabled);
        foreach (var step in selected)
        {
            step.Meta = step.Meta.WithEnabled(enable);
        }

        NotifyStepsChanged();
    }

    /// <summary>
    /// Wraps the selected steps in a <c>control.sequence</c> step, which is the macro
    /// editor's way of grouping: a sequence runs its children in order, so grouping and
    /// running in order are the same thing.
    /// </summary>
    [RelayCommand]
    private void GroupSelected()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0 || ListOf(selected[0]) is not { } list)
        {
            return;
        }

        // Grouping wraps steps that sit side by side. Steps picked out of different blocks have
        // no one place to be wrapped into, so the block the first of them is in is the one that
        // gets the group.
        var grouped = selected.Where(step => ReferenceEquals(ListOf(step), list)).ToList();
        PushUndo();

        var index = list.IndexOf(grouped[0]);
        var group = new MacroStep
        {
            Type = "control.sequence",
            Parameters =
            [
                new StepParameter
                {
                    Name = "steps",
                    Kind = ActionParameterKind.Steps,
                    Steps = [.. grouped],
                },
            ],
        };

        foreach (var step in grouped)
        {
            list.Remove(step);
        }

        list.Insert(Math.Clamp(index, 0, list.Count), group);
        StepIds.Settle(Steps);
        RestoreSelection([group]);
        NotifyStepsChanged();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        PushUndo();

        // A block that is dropped takes the steps inside it with it, and a step that is already
        // gone has no list to be taken from, so any order but this one would trip over itself.
        foreach (var step in Ordered(_selection))
        {
            ListOf(step)?.Remove(step);
        }

        _selection.Clear();
        NotifyStepsChanged();
    }

    [RelayCommand]
    private void MoveSelectedUp() => MoveSelection(-1);

    [RelayCommand]
    private void MoveSelectedDown() => MoveSelection(1);

    /// <summary>Asks the window to confirm, because clearing cannot be undone.</summary>
    [RelayCommand]
    private void ClearSteps()
    {
        if (Steps.Count == 0)
        {
            return;
        }

        ClearRequested?.Invoke();
    }

    /// <summary>Removes every step once the window has confirmed the request.</summary>
    public void ClearStepsNow()
    {
        PushUndo();
        Steps.Clear();
        _selection.Clear();
        NotifyStepsChanged();
    }

    /// <summary>
    /// Puts a step built by the "Add Action" dialog where the editor said it was to go, which is
    /// the end of the macro unless a step or a block was picked first.
    /// </summary>
    public void AddStep(MacroStep step)
    {
        PushUndo();

        var (list, index) = _insertAt is { } at
            ? ((IList<MacroStep>)at.Parameter.Steps, at.Index)
            : ((IList<MacroStep>)Steps, Steps.Count);
        _insertAt = null;
        list.Insert(Math.Clamp(index, 0, list.Count), step);

        StepIds.Settle(Steps);
        ShowInside(list);
        RestoreSelection([step]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Appends the steps a recording produced. They arrive together, so one undo puts the
    /// list back the way it was before the recording started.
    /// </summary>
    public void AppendSteps(IReadOnlyList<MacroStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        PushUndo();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }

        StepIds.Settle(Steps);
        NotifyStepsChanged();
        MarkDirty();
    }

    /// <summary>Swaps a step for the edited version returned by the dialog.</summary>
    public void ReplaceStep(MacroStep original, MacroStep replacement)
    {
        if (ListOf(original) is not { } list)
        {
            return;
        }

        var index = list.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        PushUndo();

        // A block the user had folded open stays folded open while it is being edited.
        replacement.IsExpanded = original.IsExpanded;
        list[index] = replacement;
        RestoreSelection([replacement]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Drops the given steps in below the selection, which is where a paste is expected
    /// to land. With nothing selected the steps go to the end of the list.
    /// </summary>
    public void PasteSteps(IReadOnlyList<MacroStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        PushUndo();

        var anchor = Ordered(_selection).LastOrDefault();
        var list = anchor is null ? Steps : ListOf(anchor) ?? Steps;
        var index = anchor is null ? list.Count : list.IndexOf(anchor) + 1;
        var copies = steps.Select(Clone).ToList();

        for (var offset = 0; offset < copies.Count; offset++)
        {
            list.Insert(Math.Clamp(index + offset, 0, list.Count), copies[offset]);
        }

        // A paste is a copy, and two steps sharing a name share the variable it fills in and the
        // row a run lights up; a name that is free in this macro is kept, so pasting from another
        // macro leaves the steps as they were.
        StepIds.Settle(Steps);
        ShowInside(list);
        RestoreSelection(copies);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Writes the given steps as the JSON array that copy and cut place on the clipboard.
    /// </summary>
    public static string SerializeSteps(IEnumerable<MacroStep> steps)
    {
        var array = new JsonArray();
        foreach (var step in steps)
        {
            array.Add(step.ToJson());
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// Rebuilds steps from clipboard text, or returns <c>null</c> when the text is not a
    /// step array, so pasting an unrelated phrase leaves the list untouched.
    /// </summary>
    public static List<MacroStep>? DeserializeSteps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonArray array)
            {
                return null;
            }

            var steps = new List<MacroStep>();
            foreach (var node in array)
            {
                if (node is JsonObject obj)
                {
                    steps.Add(MacroStep.FromJson(obj));
                }
            }

            return steps.Count > 0 ? steps : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Records what the action list has highlighted, so the palette can react.</summary>
    public void SetSelection(IEnumerable<MacroStep> steps)
    {
        // A step asked for before this one is no longer the step being pointed at.
        _insertAt = null;
        _selection.Clear();
        _selection.AddRange(steps);

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        RefreshPaletteState();
    }

    /// <summary>
    /// Asks the view to put the highlight back on the rows the selection sits on. A block's title
    /// and its closing line stand for the block itself, so picking one of those moves the
    /// highlight onto the block's own row rather than leaving it on the line that was clicked.
    /// </summary>
    public void RefreshSelection() => SelectionRefreshRequested?.Invoke();

    /// <summary>Fills the editor from an existing macro so it can be edited.</summary>
    public void LoadFrom(MacroItem macro)
    {
        _loading = true;
        try
        {
            Name = macro.Name;
            TriggerMode = macro.TriggerMode;
            LoopMode = macro.LoopMode;
            BindKey = macro.BindKey;
            ScheduleMode = macro.ScheduleMode;
            ScheduleInterval = macro.ScheduleInterval;
            ScheduleUnit = macro.ScheduleUnit;
            ScheduleTime = macro.ScheduleTime;
            WatchPath = macro.WatchPath;
            WatchChange = macro.WatchChange;
            WatchFilter = macro.WatchFilter;
            WatchSubfolders = macro.WatchSubfolders;
            ProcessName = macro.ProcessName;
            ProcessChange = macro.ProcessChange;
            WindowValue = macro.WindowValue;
            WindowLookup = macro.WindowLookup;
            WindowChange = macro.WindowChange;
            IdleSeconds = macro.IdleSeconds;
            PositionCapture = macro.PositionCapture;
            ColorMatch = macro.ColorMatch;
            ColorPositionX = macro.ColorPositionX.ToString(CultureInfo.InvariantCulture);
            ColorPositionY = macro.ColorPositionY.ToString(CultureInfo.InvariantCulture);
            UseCursorPosition = macro.UseCursorPosition;
            TriggerOnce = macro.TriggerOnce;
            HexColor = macro.HexColor;
            ColorTolerance = macro.ColorTolerance.ToString(CultureInfo.InvariantCulture);
            RecordMouseMovement = macro.RecordMouseMovement;
            RecordMouseButtons = macro.RecordMouseButtons;
            RecordKeyboardKeys = macro.RecordKeyboardKeys;
            RecordDelays = macro.RecordDelays;
            IgnoreDelaysBelowMs = macro.IgnoreDelaysBelowMs;
            RecordIgnoreInjected = macro.RecordIgnoreInjected;
            DelayScale = macro.DelayScale;

            Steps.Clear();
            foreach (var step in macro.Steps)
            {
                Steps.Add(step);
            }

            // A macro written before steps had names, or one edited by hand outside the program,
            // arrives with none or with the same one twice; both are settled here, once, rather
            // than defended against in every place that reads a step's name.
            StepIds.Settle(Steps);
        }
        finally
        {
            _loading = false;
        }

        NotifyStepsChanged();
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
    }

    /// <summary>Variables created by the steps built so far, offered by variable pickers.</summary>
    public IReadOnlyList<string> CollectVariables()
    {
        var locals = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps)
        {
            step.CollectVariables(locals);
        }

        // System and global variables can be read here too, so the pickers offer them.
        return VariableCatalog.Names(locals);
    }

    /// <summary>
    /// The steps of the macro being written, which is what a step that has to point at another one
    /// picks from. Conditions are left out: they are asked by the step they belong to rather than
    /// run, so they never have an ending of their own to ask about.
    /// </summary>
    public IReadOnlyList<ActionParameterOption> StepChoices()
    {
        var choices = new List<ActionParameterOption>();
        foreach (var step in Steps)
        {
            Collect(choices, step);
        }

        return choices;

        static void Collect(List<ActionParameterOption> choices, MacroStep step)
        {
            if (!step.IsCondition && step.Id.Length > 0)
            {
                choices.Add(new ActionParameterOption(step.Id, step.PickerLabel));
            }

            foreach (var parameter in step.Parameters)
            {
                foreach (var child in parameter.Steps)
                {
                    Collect(choices, child);
                }

                if (parameter.Condition is not null)
                {
                    Collect(choices, parameter.Condition);
                }
            }
        }
    }

    private static MacroStep Clone(MacroStep step) => new()
    {
        Type = step.Type,
        Id = step.Id,
        Parameters = step.Parameters.Select(Clone).ToList(),
        Meta = step.Meta,
        IsExpanded = step.IsExpanded,
    };

    /// <summary>
    /// Copies a parameter together with the steps or condition nested inside it,
    /// so duplicating a loop or an if keeps its body.
    /// </summary>
    private static StepParameter Clone(StepParameter parameter) => new()
    {
        Name = parameter.Name,
        Kind = parameter.Kind,
        Value = parameter.Value,
        Jitter = parameter.Jitter,
        Steps = parameter.Steps.Select(Clone).ToList(),
        Condition = parameter.Condition is null ? null : Clone(parameter.Condition),
    };

    /// <summary>
    /// The given steps in the order they are written, nested ones included, so a run of them
    /// keeps its order whichever block each came from.
    /// </summary>
    private List<MacroStep> Ordered(IReadOnlyList<MacroStep> steps)
    {
        var order = DocumentOrder();
        return [.. steps.Where(order.Contains).OrderBy(order.IndexOf)];
    }

    /// <summary>Every step of the macro in the order it is written, deepest steps included.</summary>
    private List<MacroStep> DocumentOrder()
    {
        var ordered = new List<MacroStep>();
        Walk(Steps);
        return ordered;

        void Walk(IEnumerable<MacroStep> steps)
        {
            foreach (var step in steps)
            {
                ordered.Add(step);
                foreach (var list in step.StepLists)
                {
                    Walk(list.Steps);
                }
            }
        }
    }

    /// <summary>Every list of steps in the macro: the top level first, then the ones in blocks.</summary>
    private IEnumerable<IList<MacroStep>> AllLists()
    {
        yield return Steps;

        foreach (var list in Descend(Steps))
        {
            yield return list;
        }
    }

    private static IEnumerable<IList<MacroStep>> Descend(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            foreach (var list in step.StepLists)
            {
                yield return list.Steps;

                foreach (var nested in Descend(list.Steps))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>The list of steps a step sits in, or null when the macro no longer holds it.</summary>
    private IList<MacroStep>? ListOf(MacroStep step)
        => AllLists().FirstOrDefault(list => list.Contains(step));

    private bool InTree(MacroStep step) => ListOf(step) is not null;

    /// <summary>
    /// True when a list of steps lives inside the given step, so moving steps into it would put
    /// the step inside itself.
    /// </summary>
    private static bool IsInside(IList<MacroStep> list, MacroStep step)
        => step.StepLists.Any(own => ReferenceEquals(own.Steps, list)
            || own.Steps.Any(child => IsInside(list, child)));

    /// <summary>
    /// Opens every block on the way to a list of steps, so a step put in there is not put in
    /// a block the user cannot see.
    /// </summary>
    private void ShowInside(IList<MacroStep> list) => Open(list, Steps);

    private static bool Open(IList<MacroStep> list, IEnumerable<MacroStep> from)
    {
        foreach (var step in from)
        {
            foreach (var own in step.StepLists)
            {
                if (ReferenceEquals(own.Steps, list) || Open(list, own.Steps))
                {
                    step.IsExpanded = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The list a drop in front of this row lands in, and where in that list. The rows are what
    /// is read rather than the steps, because a head row means "the top of this block's steps"
    /// and a foot row means "the end of them", and neither of those is a step.
    /// </summary>
    private (IList<MacroStep> List, int Index) DropTarget(int slot)
    {
        if (slot < 0 || slot >= Rows.Count)
        {
            return (Steps, Steps.Count);
        }

        var row = Rows[slot];

        // A head row is the top of the steps it names, a foot row is the end of the block it
        // closes, and any other row is the step itself, dropped in front of.
        if (row.Kind is StepRowKind.Head && row.List is { } head)
        {
            return (head.Steps, 0);
        }

        if (row.Kind is StepRowKind.Foot && row.Step.StepLists.LastOrDefault() is { } last)
        {
            return (last.Steps, last.Steps.Count);
        }

        return (row.List?.Steps ?? (IList<MacroStep>)Steps, Math.Max(0, row.Index));
    }

    /// <summary>Where a step asked for now should go: below the picked one, in its own block.</summary>
    private (MacroStep Owner, StepParameter Parameter, int Index)? InsertionPoint()
    {
        if (Ordered(_selection).LastOrDefault() is not { } anchor || OwnerOf(anchor) is not { } at)
        {
            return null;
        }

        return (at.Owner, at.Parameter, at.Parameter.Steps.IndexOf(anchor) + 1);
    }

    /// <summary>
    /// The step that holds the list a step is written in, and the parameter carrying that list.
    /// </summary>
    private (MacroStep Owner, StepParameter Parameter)? OwnerOf(MacroStep step)
    {
        foreach (var owner in DocumentOrder())
        {
            foreach (var parameter in owner.StepLists)
            {
                if (parameter.Steps.Contains(step))
                {
                    return (owner, parameter);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Puts the picked steps inside a block, at the end of the steps it runs. This is what a drop
    /// onto the block itself asks for, as opposed to a drop between two rows.
    /// </summary>
    public void MoveSelectionInto(MacroStep block)
    {
        if (block.StepLists.FirstOrDefault() is not { } parameter || _selection.Count == 0)
        {
            return;
        }

        block.IsExpanded = true;
        MoveSelectionToTarget(parameter.Steps, parameter.Steps.Count);
    }

    private void MoveSelection(int offset)
    {
        if (_selection.Count == 0)
        {
            return;
        }

        PushUndo();

        // Capture the steps first: moving them makes the list drop its selection,
        // which clears the selection the loop below needs to keep stable.
        var moved = Ordered(_selection);
        var moving = new HashSet<MacroStep>(moved);

        // Walk from the far end so a block of selected steps keeps its order.
        foreach (var step in offset < 0 ? moved : moved.AsEnumerable().Reverse())
        {
            if (ListOf(step) is not { } list)
            {
                continue;
            }

            var index = list.IndexOf(step);
            var target = index + offset;

            if (index < 0 || target < 0 || target >= list.Count || moving.Contains(list[target]))
            {
                continue;
            }

            list.RemoveAt(index);
            list.Insert(target, step);
        }

        RestoreSelection(moved);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Puts the selected steps where a drag dropped them. <paramref name="slot"/> is the row the
    /// block would land in front of, counted against the list as it stands now. The row is what
    /// says which block the steps land in, so one drag can carry a step out of a loop, into one,
    /// or past the line that closes a block.
    /// </summary>
    public void MoveSelectionTo(int slot)
    {
        var (target, index) = DropTarget(slot);
        MoveSelectionToTarget(target, index);
    }

    /// <summary>
    /// Moves the picked steps into a list of steps at a position. A step is never moved into
    /// itself, and a move that would change nothing leaves no undo entry behind.
    /// </summary>
    private void MoveSelectionToTarget(IList<MacroStep> target, int index)
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0)
        {
            return;
        }

        if (selected.Any(step => IsInside(target, step)))
        {
            return;
        }

        var moving = new HashSet<MacroStep>(selected);

        // The step the block lands in front of. A step being dragged cannot be that anchor, or
        // the block would be measured against itself and could never move past it.
        MacroStep? anchor = null;
        for (var at = Math.Clamp(index, 0, target.Count); at < target.Count; at++)
        {
            if (!moving.Contains(target[at]))
            {
                anchor = target[at];
                break;
            }
        }

        var reordered = target.Where(step => !moving.Contains(step)).ToList();
        var insertAt = anchor is null ? reordered.Count : reordered.IndexOf(anchor);
        reordered.InsertRange(insertAt, selected);

        // A drag that ends where it started is not a change, so it leaves no undo entry.
        if (reordered.SequenceEqual(target))
        {
            return;
        }

        PushUndo();

        foreach (var step in selected)
        {
            ListOf(step)?.Remove(step);
        }

        target.Clear();
        foreach (var step in reordered)
        {
            target.Add(step);
        }

        ShowInside(target);
        RestoreSelection(selected);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Re-applies a selection after the step list dropped it, and asks the view to
    /// highlight the same rows again so repeated moves do not need another click.
    /// </summary>
    private void RestoreSelection(IEnumerable<MacroStep> steps)
    {
        _selection.Clear();
        _selection.AddRange(steps.Where(InTree));

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        RefreshPaletteState();
    }

    /// <summary>
    /// Rebuilds the rows the list shows and puts the selection back on them, in that order: the
    /// view can only highlight a row that is there.
    /// </summary>
    private void NotifyStepsChanged()
    {
        RebuildRows();
        OnPropertyChanged(nameof(HasSteps));
        RefreshPaletteState();
        SelectionRefreshRequested?.Invoke();
    }

    /// <summary>
    /// Lays the steps out as rows, opening the blocks that are not folded away. Every row is
    /// rebuilt rather than patched, so what the list shows can never drift from the steps.
    /// </summary>
    private void RebuildRows()
    {
        RefreshProblems();

        IsRebuildingRows = true;
        try
        {
            Rows.Clear();
            AddRows(Steps, null, 0);
        }
        finally
        {
            IsRebuildingRows = false;
        }
    }

    /// <summary>
    /// Runs the design-time check over the steps as they stand and puts the answers where the
    /// rows and the line above the list can read them.
    /// </summary>
    private void RefreshProblems()
    {
        Problems = MacroCheck.Inspect(Steps, _projectMacros, Name);
        _problemByStep = Problems
            .GroupBy(problem => problem.Step)
            .ToDictionary(group => group.Key,
                group => string.Join("\n", group.Select(problem => problem.Message).Distinct()));

        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemSummary));
    }

    private void AddRows(IList<MacroStep> steps, StepParameter? owner, int depth)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            Rows.Add(new StepRow
            {
                Kind = StepRowKind.Step,
                Step = step,
                List = owner,
                Index = index,
                RowIndex = Rows.Count,
                Depth = depth,
                Problem = _problemByStep.GetValueOrDefault(step) ?? string.Empty,
            });

            var lists = step.StepLists.ToList();
            if (lists.Count == 0 || !step.IsExpanded)
            {
                continue;
            }

            foreach (var list in lists)
            {
                Rows.Add(new StepRow
                {
                    Kind = StepRowKind.Head,
                    Step = step,
                    List = list,
                    RowIndex = Rows.Count,
                    Depth = depth + 1,
                });

                AddRows(list.Steps, list, depth + 2);
            }

            Rows.Add(new StepRow
            {
                Kind = StepRowKind.Foot,
                Step = step,
                RowIndex = Rows.Count,
                Depth = depth,
            });
        }
    }

    /// <summary>True while the step list can be rolled back.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True while an undone change can be put back.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Remembers the list as it is now, so the next change can be rolled back.</summary>
    private void PushUndo()
    {
        _undo.Add(Capture());
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        RefreshPaletteState();
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Capture());
        Restore(entry);
        RefreshPaletteState();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Capture());
        Restore(entry);
        RefreshPaletteState();
    }

    private HistoryEntry Capture() => new(
        [.. Steps.Select(Clone)],
        [.. _selection.Select(step => DocumentOrder().IndexOf(step)).Where(index => index >= 0)],
        DelayScale);

    private void Restore(HistoryEntry entry)
    {
        DelayScale = entry.DelayScale;
        Steps.Clear();
        foreach (var step in entry.Steps)
        {
            Steps.Add(Clone(step));
        }

        var written = DocumentOrder();
        var restored = entry.Selection
            .Where(index => index < written.Count)
            .Select(index => written[index])
            .ToList();

        RestoreSelection(restored);
        NotifyStepsChanged();
    }

    /// <summary>Greys out palette entries that have nothing to act on.</summary>
    private void RefreshPaletteState()
    {
        foreach (var entry in ActionPalette)
        {
            var enabled = entry.IsAvailable;

            if (enabled && entry.NeedsSelection)
            {
                enabled = _selection.Count > 0;
            }

            if (enabled && entry.NeedsSingleSelection)
            {
                enabled = _selection.Count == 1;
            }

            if (enabled && entry.NeedsSteps)
            {
                enabled = Steps.Count > 0;
            }

            if (enabled && entry.NeedsUndo)
            {
                enabled = CanUndo;
            }

            if (enabled && entry.NeedsRedo)
            {
                enabled = CanRedo;
            }

            entry.IsEnabled = enabled;
        }
    }

    [RelayCommand]
    private void SetKey()
    {
        // The button is disabled while recording; this guards the keyboard path too.
        if (IsRecording)
        {
            return;
        }

        IsCapturingKey = true;
    }

    [RelayCommand]
    private void ToggleRecord()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        // A pending bind-key capture would swallow the shortcut and the first recorded keystroke.
        IsCapturingKey = false;
        IsRecording = true;
    }

    /// <summary>Ends an active recording without closing the editor.</summary>
    public void StopRecording() => IsRecording = false;

    /// <summary>
    /// Tells the window to put the input hook up or take it down, so the recording is tied to
    /// the button and the shortcut rather than to either of them alone.
    /// </summary>
    partial void OnIsRecordingChanged(bool value)
    {
        RecordedEvents = 0;
        RecordingChanged?.Invoke(value);
    }

    [RelayCommand]
    private void Save()
    {
        StopRecording();
        IsDirty = false;
        CloseRequested?.Invoke(BuildMacro());
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Highlighting rows in the list, and starting or stopping a recording, are not edits
        // of the macro itself, and neither is what the design-time check made of it.
        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(HasSelection)
                or nameof(SelectedSteps) or nameof(IsRecording) or nameof(RecordedEvents)
                or nameof(Problems) or nameof(HasProblems) or nameof(ProblemSummary)))
        {
            MarkDirty();
        }
    }

    /// <summary>
    /// Raised for every change to the macro being written, where <see cref="IsDirty"/> only says
    /// whether there is any unsaved work at all: the snapshot has to hear about the changes that
    /// arrive while the macro is already unsaved, and the dirty flag does not move for those.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Notes that the macro being written has moved on.</summary>
    private void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Renaming a macro changes which calls point at it, so the check has to be run again —
    /// including when the new name is the one a self-call was waiting for.
    /// </summary>
    partial void OnNameChanged(string value)
    {
        if (!_loading)
        {
            NotifyStepsChanged();
        }
    }

    /// <summary>
    /// Reads the editor into a macro. Internal rather than private so the window can keep the
    /// work in the recovery snapshot while it is still being written.
    /// </summary>
    internal MacroItem BuildMacro()
    {
        // The loop a key trigger uses has nothing to hold on to when the clock does the
        // starting, so a timed macro just says when it runs.
        var action = TriggerMode == MacroTrigger.Timer ? string.Empty : LoopName(LoopMode);

        var macro = new MacroItem
        {
            Name = Name.Trim(),
            Trigger = TriggerMode switch
            {
                MacroTrigger.ColorPixelChanges => Strings.Get("Trigger.Color"),
                MacroTrigger.Timer => Strings.Get("Trigger.Timer"),
                MacroTrigger.FileChanges => Strings.Get("Trigger.File"),
                MacroTrigger.Process => Strings.Get("Trigger.Process"),
                MacroTrigger.Window => Strings.Get("Trigger.Window"),
                MacroTrigger.Idle => Strings.Get("Trigger.Idle"),
                _ => Strings.Get("Trigger.Keys"),
            },
            Action = action,
            TriggerMode = TriggerMode,
            LoopMode = LoopMode,
            BindKey = BindKey,
            ScheduleMode = ScheduleMode,
            ScheduleInterval = Math.Max(1, ScheduleInterval ?? 5),
            ScheduleUnit = ScheduleUnit,
            ScheduleTime = ScheduleTime.Trim(),
            WatchPath = WatchPath.Trim(),
            WatchChange = WatchChange,
            WatchFilter = WatchFilter.Trim().Length == 0 ? "*" : WatchFilter.Trim(),
            WatchSubfolders = WatchSubfolders,
            ProcessName = ProcessName.Trim(),
            ProcessChange = ProcessChange,
            WindowValue = WindowValue.Trim(),
            WindowLookup = WindowLookup,
            WindowChange = WindowChange,
            IdleSeconds = Math.Max(1, IdleSeconds ?? 60),
            PositionCapture = PositionCapture,
            ColorMatch = ColorMatch,
            ColorPositionX = ParseCoordinate(ColorPositionX),
            ColorPositionY = ParseCoordinate(ColorPositionY),
            UseCursorPosition = UseCursorPosition,
            TriggerOnce = TriggerOnce,
            HexColor = HexColor,
            ColorTolerance = ParseCoordinate(ColorTolerance),
            RecordMouseMovement = RecordMouseMovement,
            RecordMouseButtons = RecordMouseButtons,
            RecordKeyboardKeys = RecordKeyboardKeys,
            RecordDelays = RecordDelays,
            IgnoreDelaysBelowMs = (int)(IgnoreDelaysBelowMs ?? 0),
            RecordIgnoreInjected = RecordIgnoreInjected,
            DelayScale = DelayScale,
        };

        foreach (var step in Steps)
        {
            macro.Steps.Add(step);
        }

        return macro;
    }

    /// <summary>Reads a numeric parameter typed into a plain input box, falling back to zero.</summary>
    private static int ParseCoordinate(string? value)
        => int.TryParse(value?.Trim(), out var result) ? result : 0;

    /// <summary>The loop a key trigger uses, said the way the interface says it.</summary>
    private static string LoopName(MacroLoop loop) => loop switch
    {
        MacroLoop.Hold => Strings.Get("Loop.Hold"),
        MacroLoop.Press => Strings.Get("Loop.Press"),
        MacroLoop.Release => Strings.Get("Loop.Release"),
        _ => Strings.Get("Loop.Toggle"),
    };
}
