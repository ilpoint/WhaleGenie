using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// What starts this macro: the summary, the sentence it reads as, and the entry it is bound to.
public partial class MacroItem : ObservableObject, ILanguageAware
{

    /// <summary>
    /// Trigger entries attached to this macro while the project, editor or recovery snapshot is
    /// being read. The package's trigger collection remains the project-wide source of truth.
    /// </summary>
    public List<TriggerEntry> Triggers { get; } = [];

    /// <summary>Interface layouts held with an editor draft; positions are merged at the window.</summary>
    public List<InterfaceLayout> InterfaceLayouts { get; } = [];

    public void RefreshTriggers()
    {
        RefreshTriggerDisplay();
        OnPropertyChanged(nameof(TriggerSummaries));
    }

    public string TriggerSummaries => Triggers.Count == 0
        ? Strings.Get("Trigger.Unset")
        : string.Join(" · ", Triggers.Select(entry => entry.Summary));

    /// <summary>Whether the macro is armed; the macro list shows this as a coloured dot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    public partial bool IsEnabled { get; set; } = true;

    /// <summary>
    /// What the card's menu calls the switch for this macro: arming one that is off, or disarming
    /// one that is on. It is read when the menu opens, so the word always matches the dot.
    /// </summary>
    public string ToggleLabel => Strings.Get(IsEnabled ? "Main.DisableMacro" : "Main.EnableMacro");

    /// <summary>
    /// Whether a trigger is running this macro right now. It is shown on the macro list and is
    /// never saved: a macro that was running when the project was written is not running when it
    /// is read back.
    /// </summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The folder this macro was put in, as the author spelled it. Empty means ungrouped. It is a
    /// second layer beside the fixed views: the views say how a macro starts, a group says which
    /// part of someone's work it belongs to, and the two filter together.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// A sentence for the macro library or for a call made to this macro. It does not describe a
    /// particular trigger, so it belongs to the callable part shared by projects and submacros.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MacroTrigger TriggerMode { get; set; } = MacroTrigger.KeystrokesButtonInputs;

    public MacroLoop LoopMode { get; set; } = MacroLoop.Press;

    /// <summary>What starts the macro, said the way the macro list says it.</summary>
    public string Trigger => TriggerMode switch
    {
        MacroTrigger.ColorPixelChanges => Strings.Get("Trigger.Color"),
        MacroTrigger.Timer => Strings.Get("Trigger.Timer"),
        MacroTrigger.FileChanges => Strings.Get("Trigger.File"),
        MacroTrigger.Process => Strings.Get("Trigger.Process"),
        MacroTrigger.Window => Strings.Get("Trigger.Window"),
        MacroTrigger.Idle => Strings.Get("Trigger.Idle"),
        _ => Strings.Get("Trigger.Keys"),
    };

    /// <summary>Loop wording for key/colour entries; one-shot entries contribute no action text.</summary>
    public string Action
    {
        get
        {
            IEnumerable<MacroLoop> loops = Triggers.Count > 0
                ? Triggers.Where(entry => entry.Kind is TriggerKind.Key or TriggerKind.Color)
                    .Select(entry => entry.LoopMode)
                : TriggerMode == MacroTrigger.Timer
                    ? []
                    : [LoopMode];
            return string.Join(" · ", loops.Distinct().Select(loop => loop switch
            {
                MacroLoop.Hold => Strings.Get("Loop.Hold"),
                MacroLoop.Press => Strings.Get("Loop.Press"),
                MacroLoop.Release => Strings.Get("Loop.Release"),
                _ => Strings.Get("Loop.Toggle"),
            }));
        }
    }

    /// <summary>Key bound to the macro, or empty when no key has been selected.</summary>
    [ObservableProperty]
    public partial string BindKey { get; set; } = string.Empty;

    public MousePositionMode PositionCapture { get; set; } = MousePositionMode.SaveCurrentPosition;

    /// <summary>How the watched colour is compared to the trigger position.</summary>
    public ColorMatchCondition ColorMatch { get; set; } = ColorMatchCondition.ColorMatches;

    /// <summary>Horizontal screen position watched for the trigger colour.</summary>
    public int ColorPositionX { get; set; }

    /// <summary>Vertical screen position watched for the trigger colour.</summary>
    public int ColorPositionY { get; set; }

    /// <summary>When set, the watched position follows the cursor instead of the stored coordinates.</summary>
    public bool UseCursorPosition { get; set; }

    /// <summary>When set, the macro runs only once instead of repeating.</summary>
    public bool TriggerOnce { get; set; }

    /// <summary>Whether a timer trigger repeats by the clock or at a set time of day.</summary>
    public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Interval;

    /// <summary>How many seconds, minutes or hours an interval schedule waits.</summary>
    public int ScheduleInterval { get; set; } = 5;

    /// <summary>The unit <see cref="ScheduleInterval"/> counts in.</summary>
    public ScheduleUnit ScheduleUnit { get; set; } = ScheduleUnit.Seconds;

    /// <summary>The time of day a daily schedule runs at, written <c>08:30</c>.</summary>
    public string ScheduleTime { get; set; } = "08:00";

    /// <summary>The file or folder a file trigger watches, as the author wrote it.</summary>
    public string WatchPath { get; set; } = string.Empty;

    /// <summary>Which change of the watched path starts the macro.</summary>
    public FileChangeKind WatchChange { get; set; } = FileChangeKind.Any;

    /// <summary>The names watched inside a watched folder, such as <c>*.txt</c>.</summary>
    public string WatchFilter { get; set; } = "*";

    /// <summary>Whether a watched folder also reports what happens in the folders inside it.</summary>
    public bool WatchSubfolders { get; set; }

    /// <summary>The program a process trigger waits for, without the ".exe".</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Whether the program starting or the program finishing starts the macro.</summary>
    public ProcessChangeKind ProcessChange { get; set; } = ProcessChangeKind.Started;

    /// <summary>
    /// The window rule a window trigger watches. It is the same record an action uses, so the
    /// picker and every runtime lookup recognise a window in one way.
    /// </summary>
    public WindowRule WindowTarget { get; set; } = WindowRule.Empty;

    /// <summary>Whether the window appearing or going away starts the macro.</summary>
    public WindowChangeKind WindowChange { get; set; } = WindowChangeKind.Appeared;

    /// <summary>
    /// How long the machine has to go without a key press, a mouse move or a click before an idle
    /// trigger runs the macro.
    /// </summary>
    public int IdleSeconds { get; set; } = 60;

    /// <summary>Colour watched for the trigger, as a hex string (for example <c>#000000</c>).</summary>
    [ObservableProperty]
    public partial string HexColor { get; set; } = "#000000";

    /// <summary>Shown on the card when a trigger has nothing to preview yet.</summary>
    private static string NoPreview => Strings.Get("Common.None");

    /// <summary>True when the macro is started by a colour / pixel change.</summary>
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

    /// <summary>True when the macro is started by input and the binding is a controller button.</summary>
    public bool IsGamepadTrigger => IsKeyTrigger && LooksLikeGamepadButton(BindKey);

    /// <summary>True when the macro is started by input and the binding is a mouse button.</summary>
    public bool IsMouseTrigger => IsKeyTrigger && !IsGamepadTrigger && LooksLikeMouseButton(BindKey);

    /// <summary>True when the macro is started by input and the binding is anything else.</summary>
    public bool IsKeyboardTrigger => IsKeyTrigger && !IsGamepadTrigger && !IsMouseTrigger;

    /// <summary>
    /// What the macro card previews next to the trigger icon: the bound key (for example
    /// <c>NumPad7</c>), the watched colour, the schedule, the watched path, the program or the
    /// window, so the list says what starts each macro.
    /// </summary>
    public string TriggerPreview => TriggerMode switch
    {
        MacroTrigger.ColorPixelChanges =>
            string.IsNullOrWhiteSpace(HexColor) ? NoPreview : HexColor.ToUpperInvariant(),
        MacroTrigger.Timer => MacroSchedule.Describe(this),
        MacroTrigger.FileChanges =>
            string.IsNullOrWhiteSpace(WatchPath) ? NoPreview : WatchPath,
        MacroTrigger.Process =>
            string.IsNullOrWhiteSpace(ProcessName) ? NoPreview : ProcessName,
        MacroTrigger.Window =>
            WindowTarget.MatchValue is { Length: > 0 } value
                ? value
                : NoPreview,
        MacroTrigger.Idle => Strings.Format("Idle.Summary", Math.Max(1, IdleSeconds)),
        _ => string.IsNullOrWhiteSpace(BindKey) ? NoPreview : BindKey,
    };

    private bool IsKeyTrigger => TriggerMode == MacroTrigger.KeystrokesButtonInputs;

    /// <summary>
    /// Whether a binding names a mouse button. The names the input library and the interface use
    /// all carry one of these words, so the card can pick the mouse icon over the keyboard one.
    /// </summary>
    private static bool LooksLikeMouseButton(string binding)
    {
        var text = (binding ?? string.Empty).Trim();
        return text.Length > 0
               && (text.Contains("mouse", System.StringComparison.OrdinalIgnoreCase)
                   || text.Contains("wheel", System.StringComparison.OrdinalIgnoreCase)
                   || text.Contains("xbutton", System.StringComparison.OrdinalIgnoreCase)
                   || text.Contains("button", System.StringComparison.OrdinalIgnoreCase));
    }
}
