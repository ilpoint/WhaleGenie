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
using Viktor.Localization;
using Viktor.Models;

namespace Viktor.ViewModels;

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
    private bool _loading;

    /// <summary>How many step-list changes can be undone.</summary>
    private const int HistoryLimit = 60;

    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];

    /// <summary>The step list and selection as they were before one change.</summary>
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

    public bool HasSteps => Steps.Count > 0;

    public bool HasSelection => _selection.Count > 0;

    /// <summary>The steps highlighted in the action list.</summary>
    public IReadOnlyList<MacroStep> SelectedSteps => _selection;

    /// <summary>True while the editor holds changes that have not been saved yet.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerIndex))]
    [NotifyPropertyChangedFor(nameof(IsColorTrigger))]
    [NotifyPropertyChangedFor(nameof(IsKeyTrigger))]
    [NotifyPropertyChangedFor(nameof(IsTimerTrigger))]
    public partial MacroTrigger TriggerMode { get; set; } = MacroTrigger.KeystrokesButtonInputs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopIndex))]
    public partial MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

    public IReadOnlyList<string> TriggerOptions { get; } =
    [
        Strings.Get("Trigger.Keys"),
        Strings.Get("Trigger.Color"),
        Strings.Get("Trigger.Timer"),
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
        IsDirty = true;
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

    /// <summary>Asks the window to open the "Add Action" dialog.</summary>
    [RelayCommand]
    private void NewStep() => AddStepRequested?.Invoke();

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
        if (selected.Count == 0)
        {
            return;
        }

        PushUndo();

        var index = Steps.IndexOf(selected[0]);
        var group = new MacroStep
        {
            Type = "control.sequence",
            Parameters =
            [
                new StepParameter
                {
                    Name = "steps",
                    Kind = ActionParameterKind.Steps,
                    Steps = [.. selected],
                },
            ],
        };

        foreach (var step in selected)
        {
            Steps.Remove(step);
        }

        Steps.Insert(Math.Clamp(index, 0, Steps.Count), group);
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

        foreach (var step in _selection.ToList())
        {
            Steps.Remove(step);
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

    /// <summary>Appends a step built by the "Add Action" dialog.</summary>
    public void AddStep(MacroStep step)
    {
        PushUndo();
        Steps.Add(step);
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

        NotifyStepsChanged();
        IsDirty = true;
    }

    /// <summary>Swaps a step for the edited version returned by the dialog.</summary>
    public void ReplaceStep(MacroStep original, MacroStep replacement)
    {
        var index = Steps.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        PushUndo();
        Steps[index] = replacement;
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

        var anchor = Ordered(_selection).LastOrDefault() ?? Steps.LastOrDefault();
        var index = anchor is null ? Steps.Count : Steps.IndexOf(anchor) + 1;
        var copies = steps.Select(Clone).ToList();

        for (var offset = 0; offset < copies.Count; offset++)
        {
            Steps.Insert(Math.Clamp(index + offset, 0, Steps.Count), copies[offset]);
        }

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
        _selection.Clear();
        _selection.AddRange(steps);

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        RefreshPaletteState();
    }

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

    private static MacroStep Clone(MacroStep step) => new()
    {
        Type = step.Type,
        Parameters = step.Parameters.Select(Clone).ToList(),
        Meta = step.Meta,
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
        Steps = parameter.Steps.Select(Clone).ToList(),
        Condition = parameter.Condition is null ? null : Clone(parameter.Condition),
    };

    private List<MacroStep> Ordered(IReadOnlyList<MacroStep> steps)
        => steps.OrderBy(Steps.IndexOf).ToList();

    private void MoveSelection(int offset)
    {
        if (_selection.Count == 0)
        {
            return;
        }

        PushUndo();

        // Capture the rows first: moving them makes the list drop its selection,
        // which clears the selection the loop below needs to keep stable.
        var moved = Ordered(_selection);
        var moving = new HashSet<MacroStep>(moved);

        // Walk from the far end so a block of selected steps keeps its order.
        foreach (var step in offset < 0 ? moved : moved.AsEnumerable().Reverse())
        {
            var index = Steps.IndexOf(step);
            var target = index + offset;

            if (index < 0 || target < 0 || target >= Steps.Count || moving.Contains(Steps[target]))
            {
                continue;
            }

            Steps.Move(index, target);
        }

        RestoreSelection(moved);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Puts the selected steps where a drag dropped them. <paramref name="slot"/> is the row
    /// index the block should start at, counted against the list as it stands now.
    /// </summary>
    public void MoveSelectionTo(int slot)
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0)
        {
            return;
        }

        var moving = new HashSet<MacroStep>(selected);

        // The row the block lands in front of. A row being dragged cannot be that anchor, or
        // the block would be measured against itself and could never move past it.
        MacroStep? anchor = null;
        for (var index = Math.Clamp(slot, 0, Steps.Count); index < Steps.Count; index++)
        {
            if (!moving.Contains(Steps[index]))
            {
                anchor = Steps[index];
                break;
            }
        }

        var reordered = Steps.Where(step => !moving.Contains(step)).ToList();
        var insertAt = anchor is null ? reordered.Count : reordered.IndexOf(anchor);
        reordered.InsertRange(insertAt, selected);

        // A drag that ends where it started is not a change, so it leaves no undo entry.
        if (reordered.SequenceEqual(Steps))
        {
            return;
        }

        PushUndo();

        Steps.Clear();
        foreach (var step in reordered)
        {
            Steps.Add(step);
        }

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
        _selection.AddRange(steps.Where(Steps.Contains));

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        SelectionRefreshRequested?.Invoke();
        RefreshPaletteState();
    }

    private void NotifyStepsChanged()
    {
        OnPropertyChanged(nameof(HasSteps));
        RefreshPaletteState();
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
        [.. _selection.Select(Steps.IndexOf).Where(index => index >= 0)],
        DelayScale);

    private void Restore(HistoryEntry entry)
    {
        DelayScale = entry.DelayScale;
        Steps.Clear();
        foreach (var step in entry.Steps)
        {
            Steps.Add(Clone(step));
        }

        var restored = entry.Selection
            .Where(index => index < Steps.Count)
            .Select(index => Steps[index])
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
        // of the macro itself.
        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(HasSelection)
                or nameof(SelectedSteps) or nameof(IsRecording) or nameof(RecordedEvents)))
        {
            IsDirty = true;
        }
    }

    private MacroItem BuildMacro()
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
