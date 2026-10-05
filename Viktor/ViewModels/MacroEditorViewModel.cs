using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                Label = "Add new action (Insert)" },

        new() { Icon = G("M4,20 L4,16 L16,4 L20,8 L8,20 Z M14,6 L18,10"),
                Label = "Edit selected action (F2)" },

        new() { Icon = G("M8,8 L20,8 L20,20 L8,20 Z M4,16 L4,4 L16,4 L16,6"),
                Label = "Duplicate selected actions (Ctrl + D)" },

        new() { Icon = G("M4,7 L20,7 M10,11 L10,17 M14,11 L14,17 M6,7 L7,21 L17,21 L18,7 M9,7 L9,4 L15,4 L15,7"),
                Label = "Delete selected actions (Del)" },

        new() { Icon = G("M3,6 L9,6 L11,8 L21,8 L21,19 L3,19 Z"),
                Label = "Turn selected actions into a group action" },

        new() { Icon = G("M12,19 L12,5 M6,11 L12,5 L18,11"),
                Label = "Move selected actions up (Ctrl + Up)" },

        new() { Icon = G("M12,5 L12,19 M6,13 L12,19 L18,13"),
                Label = "Move selected actions down (Ctrl + Down)" },

        new() { Icon = G("M5,5 L19,19 M19,5 L5,19"),
                Label = "Clear all actions" },

        new() { Icon = G("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,7 L12,12 L15,14"),
                Label = "Adjust Wait/Delay times" },

        new() { Icon = G("M12,3 L12,15 M7,10 L12,15 L17,10 M4,19 L20,19"),
                Label = "Import Action groups" },
    ];

    public ObservableCollection<MacroAction> Actions { get; } = [];

    public bool HasActions => Actions.Count > 0;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerIndex))]
    [NotifyPropertyChangedFor(nameof(IsColorTrigger))]
    public partial MacroTrigger TriggerMode { get; set; } = MacroTrigger.KeystrokesButtonInputs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopIndex))]
    public partial MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

    public IReadOnlyList<string> TriggerOptions { get; } =
    [
        "Keystrokes / Button Inputs",
        "Color / Pixel Changes",
    ];

    public IReadOnlyList<string> LoopOptions { get; } =
    [
        "Until Key Pressed Again (Toggle)",
        "While Holding Key (Hold)",
        "Once, When Key Pressed (Press)",
        "Once, When Key Released (Release)",
    ];

    public int TriggerIndex
    {
        get => (int)TriggerMode;
        set
        {
            if (value >= 0 && value <= (int)MacroTrigger.ColorPixelChanges)
            {
                TriggerMode = (MacroTrigger)value;
            }
        }
    }

    /// <summary>True when the macro is started by a colour / pixel change rather than by input.</summary>
    public bool IsColorTrigger => TriggerMode == MacroTrigger.ColorPixelChanges;

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
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionCaptureIndex))]
    public partial MousePositionMode PositionCapture { get; set; } = MousePositionMode.SaveCurrentPosition;

    public IReadOnlyList<string> PositionCaptureOptions { get; } =
    [
        "Save Current Position",
        "Save Position Differences",
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
        "Color Matches",
        "Color Not Matches",
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

    public string BindKeyDisplay => string.IsNullOrEmpty(BindKey) ? "No Key Selected" : BindKey;

    public string SetKeyLabel => IsCapturingKey ? "Press a key…" : "Set Key";

    public string RecordLabel => IsRecording ? "Stop Recording" : "Record";

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
        if (action is null)
        {
            return;
        }

        Actions.Add(new MacroAction { Icon = action.Icon, Label = action.Label });
        OnPropertyChanged(nameof(HasActions));
    }

    [RelayCommand]
    private void SetKey() => IsCapturingKey = true;

    [RelayCommand]
    private void ToggleRecord() => IsRecording = !IsRecording;

    [RelayCommand]
    private void Save() => CloseRequested?.Invoke(BuildMacro());

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    private MacroItem BuildMacro()
    {
        var macro = new MacroItem
        {
            Name = Name.Trim(),
            Trigger = TriggerMode == MacroTrigger.KeystrokesButtonInputs
                ? "Keystrokes / Button Inputs"
                : "Color / Pixel Changes",
            Action = LoopMode switch
            {
                MacroLoop.Hold => "while holding key",
                MacroLoop.Press => "once, when key pressed",
                MacroLoop.Release => "once, when key released",
                _ => "until key pressed again",
            },
            TriggerMode = TriggerMode,
            LoopMode = LoopMode,
            BindKey = BindKey,
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
        };

        foreach (var action in Actions)
        {
            macro.Actions.Add(action);
        }

        return macro;
    }

    /// <summary>Reads a numeric parameter typed into a plain input box, falling back to zero.</summary>
    private static int ParseCoordinate(string? value)
        => int.TryParse(value?.Trim(), out var result) ? result : 0;
}
