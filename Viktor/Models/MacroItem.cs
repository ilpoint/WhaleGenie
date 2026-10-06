using System.Collections.Generic;

using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Viktor.Models;

public partial class MacroItem : ObservableObject
{
    /// <summary>Whether the macro is armed; the macro list shows this as a coloured dot.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether a trigger is running this macro right now. It is shown on the macro list and is
    /// never saved: a macro that was running when the project was written is not running when it
    /// is read back.
    /// </summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Short description of what starts the macro, shown in the macro list.</summary>
    public string Trigger { get; set; } = string.Empty;

    /// <summary>Short description of how the macro loops, shown in the macro list.</summary>
    public string Action { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MacroTrigger TriggerMode { get; set; } = MacroTrigger.KeystrokesButtonInputs;

    public MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

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

    /// <summary>Colour watched for the trigger, as a hex string (for example <c>#000000</c>).</summary>
    [ObservableProperty]
    public partial string HexColor { get; set; } = "#000000";

    /// <summary>Shown on the card when a trigger has nothing to preview yet.</summary>
    private const string NoPreview = "—";

    /// <summary>True when the macro is started by a colour / pixel change.</summary>
    public bool IsColorTrigger => TriggerMode == MacroTrigger.ColorPixelChanges;

    /// <summary>True when the macro is started by the clock.</summary>
    public bool IsTimerTrigger => TriggerMode == MacroTrigger.Timer;

    /// <summary>True when the macro is started by a file or a folder changing.</summary>
    public bool IsFileTrigger => TriggerMode == MacroTrigger.FileChanges;

    /// <summary>True when the macro is started by input and the binding is a mouse button.</summary>
    public bool IsMouseTrigger => IsKeyTrigger && LooksLikeMouseButton(BindKey);

    /// <summary>True when the macro is started by input and the binding is anything else.</summary>
    public bool IsKeyboardTrigger => IsKeyTrigger && !IsMouseTrigger;

    /// <summary>
    /// What the macro card previews next to the trigger icon: the bound key (for example
    /// <c>NumPad7</c>), the watched colour, the schedule or the watched path, so the list says
    /// what starts each macro.
    /// </summary>
    public string TriggerPreview => TriggerMode switch
    {
        MacroTrigger.ColorPixelChanges =>
            string.IsNullOrWhiteSpace(HexColor) ? NoPreview : HexColor.ToUpperInvariant(),
        MacroTrigger.Timer => MacroSchedule.Describe(this),
        MacroTrigger.FileChanges =>
            string.IsNullOrWhiteSpace(WatchPath) ? NoPreview : WatchPath,
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

    /// <summary>Keeps the card's icon and preview in step with the settings behind them.</summary>
    partial void OnTriggerModeChanged(MacroTrigger value) => RefreshTriggerDisplay();

    partial void OnBindKeyChanged(string value) => RefreshTriggerDisplay();

    partial void OnHexColorChanged(string value) => RefreshTriggerDisplay();

    private void RefreshTriggerDisplay()
    {
        OnPropertyChanged(nameof(IsColorTrigger));
        OnPropertyChanged(nameof(IsTimerTrigger));
        OnPropertyChanged(nameof(IsFileTrigger));
        OnPropertyChanged(nameof(IsMouseTrigger));
        OnPropertyChanged(nameof(IsKeyboardTrigger));
        OnPropertyChanged(nameof(TriggerPreview));
    }

    /// <summary>Allowed colour difference (percent) before the watched pixel counts as a match.</summary>
    public int ColorTolerance { get; set; } = 5;

    public bool RecordMouseMovement { get; set; } = true;

    public bool RecordMouseButtons { get; set; } = true;

    public bool RecordKeyboardKeys { get; set; } = true;

    public bool RecordDelays { get; set; } = true;

    /// <summary>
    /// When set, input the system says another program sent is left out of the recording, so a
    /// macro never picks up its own playback. Turn it off to record through a remote session or
    /// a virtual machine, where everything arrives looking injected.
    /// </summary>
    public bool RecordIgnoreInjected { get; set; } = true;

    /// <summary>Delays shorter than this are dropped from the recording.</summary>
    public int IgnoreDelaysBelowMs { get; set; } = 50;

    /// <summary>
    /// How much longer or shorter this macro's waits are made when it runs. 1 keeps every wait
    /// as it was written; 2 waits twice as long. It is applied while running, so the steps
    /// themselves stay exactly as the author left them.
    /// </summary>
    public double DelayScale { get; set; } = 1;

    /// <summary>The recorded or hand-built steps, saved as the macro's JSON tree.</summary>
    public List<MacroStep> Steps { get; } = [];

    /// <summary>Names of the variables this macro creates for itself.</summary>
    public IReadOnlyList<string> LocalVariableNames()
    {
        var names = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps)
        {
            step.CollectVariables(names);
        }

        return [.. names];
    }

    /// <summary>Serialises the macro into the JSON document the runtime consumes.</summary>
    public JsonObject ToJson()
    {
        var steps = new JsonArray();
        foreach (var step in Steps)
        {
            steps.Add(step.ToJson());
        }

        return new JsonObject
        {
            ["name"] = Name,
            ["triggerMode"] = TriggerMode.ToString(),
            ["loopMode"] = LoopMode.ToString(),
            ["bindKey"] = BindKey,
            ["schedule"] = new JsonObject
            {
                ["mode"] = ScheduleMode.ToString(),
                ["interval"] = ScheduleInterval,
                ["unit"] = ScheduleUnit.ToString(),
                ["time"] = ScheduleTime,
            },
            ["watch"] = new JsonObject
            {
                ["path"] = WatchPath,
                ["change"] = WatchChange.ToString(),
                ["filter"] = WatchFilter,
                ["subfolders"] = WatchSubfolders,
            },
            ["positionCapture"] = PositionCapture.ToString(),
            ["colorMatch"] = ColorMatch.ToString(),
            ["colorPosition"] = new JsonObject
            {
                ["x"] = ColorPositionX,
                ["y"] = ColorPositionY,
            },
            ["useCursorPosition"] = UseCursorPosition,
            ["triggerOnce"] = TriggerOnce,
            ["hexColor"] = HexColor,
            ["colorTolerance"] = ColorTolerance,
            ["delayScale"] = DelayScale,
            ["record"] = new JsonObject
            {
                ["mouseMovement"] = RecordMouseMovement,
                ["mouseButtons"] = RecordMouseButtons,
                ["keyboardKeys"] = RecordKeyboardKeys,
                ["delays"] = RecordDelays,
                ["ignoreDelaysBelowMs"] = IgnoreDelaysBelowMs,
                ["ignoreInjected"] = RecordIgnoreInjected,
            },
            ["steps"] = steps,
        };
    }

    /// <summary>Rebuilds a macro from the JSON document written by <see cref="ToJson"/>.</summary>
    public static MacroItem FromJson(JsonObject node)
    {
        var position = node["colorPosition"] as JsonObject;
        var record = node["record"] as JsonObject;
        var schedule = node["schedule"] as JsonObject;
        var watch = node["watch"] as JsonObject;

        var macro = new MacroItem
        {
            Name = node["name"]?.GetValue<string>() ?? string.Empty,
            Trigger = node["trigger"]?.GetValue<string>() ?? string.Empty,
            Action = node["loop"]?.GetValue<string>() ?? string.Empty,
            TriggerMode = ReadEnum(node, "triggerMode", MacroTrigger.KeystrokesButtonInputs),
            LoopMode = ReadEnum(node, "loopMode", MacroLoop.Toggle),
            BindKey = node["bindKey"]?.GetValue<string>() ?? string.Empty,
            ScheduleMode = ReadEnum(schedule, "mode", ScheduleMode.Interval),
            ScheduleInterval = ReadInt(schedule?["interval"], 5),
            ScheduleUnit = ReadEnum(schedule, "unit", ScheduleUnit.Seconds),
            ScheduleTime = schedule?["time"]?.GetValue<string>() ?? "08:00",
            WatchPath = watch?["path"]?.GetValue<string>() ?? string.Empty,
            WatchChange = ReadEnum(watch, "change", FileChangeKind.Any),
            WatchFilter = watch?["filter"]?.GetValue<string>() ?? "*",
            WatchSubfolders = ReadBool(watch?["subfolders"]),
            PositionCapture = ReadEnum(node, "positionCapture", MousePositionMode.SaveCurrentPosition),
            ColorMatch = ReadEnum(node, "colorMatch", ColorMatchCondition.ColorMatches),
            ColorPositionX = ReadInt(position?["x"]),
            ColorPositionY = ReadInt(position?["y"]),
            UseCursorPosition = ReadFlag(node, "useCursorPosition"),
            TriggerOnce = ReadFlag(node, "triggerOnce"),
            HexColor = node["hexColor"]?.GetValue<string>() ?? "#000000",
            ColorTolerance = ReadInt(node["colorTolerance"]),
            RecordMouseMovement = ReadBool(record?["mouseMovement"], true),
            RecordMouseButtons = ReadBool(record?["mouseButtons"], true),
            RecordKeyboardKeys = ReadBool(record?["keyboardKeys"], true),
            RecordDelays = ReadBool(record?["delays"], true),
            IgnoreDelaysBelowMs = ReadInt(record?["ignoreDelaysBelowMs"], 50),
            RecordIgnoreInjected = ReadBool(record?["ignoreInjected"], true),
            DelayScale = ReadDouble(node["delayScale"], 1),
        };

        if (node["steps"] is JsonArray steps)
        {
            foreach (var step in steps)
            {
                if (step is JsonObject child)
                {
                    macro.Steps.Add(MacroStep.FromJson(child));
                }
            }
        }

        return macro;
    }

    private static T ReadEnum<T>(JsonObject? node, string name, T fallback) where T : struct, System.Enum
        => System.Enum.TryParse<T>(node?[name]?.GetValue<string>(), out var parsed) ? parsed : fallback;

    private static bool ReadBool(JsonNode? node, bool fallback = false)
        => node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static bool ReadFlag(JsonObject node, string name, bool fallback = false)
        => ReadBool(node[name], fallback);

    private static int ReadInt(JsonNode? node, int fallback = 0)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static double ReadDouble(JsonNode? node, double fallback)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
               && double.TryParse(text, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
