using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// One project entry pointing at a macro, with the settings that decide when it fires.
/// <summary>
/// One project entry that points at a macro. Trigger settings live here rather than on the macro
/// body, so the same macro can be reached by several inputs without copying its steps.
/// </summary>
public partial class TriggerEntry : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindLabel))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(IsRemovable))]
    public partial TriggerKind Kind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string MacroName { get; set; } = string.Empty;

    /// <summary>Stable identifier of the common or context page this menu entry belongs to.</summary>
    [ObservableProperty]
    public partial string MenuPageId { get; set; } = string.Empty;

    /// <summary>Zero-based row in the three-row region; -1 means not assigned.</summary>
    [ObservableProperty]
    public partial int MenuRow { get; set; } = -1;

    /// <summary>Zero-based column in the four-column menu; -1 means not assigned.</summary>
    [ObservableProperty]
    public partial int MenuColumn { get; set; } = -1;

    /// <summary>The built-in line icon or imported image shown instead of the menu label.</summary>
    [ObservableProperty]
    public partial MenuIconReference? MenuIcon { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string BindKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MacroLoop LoopMode { get; set; } = MacroLoop.Press;

    [ObservableProperty]
    public partial bool TriggerOnce { get; set; }

    [ObservableProperty]
    public partial string ScheduleTime { get; set; } = "08:00";

    [ObservableProperty]
    public partial ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Interval;

    [ObservableProperty]
    public partial int ScheduleInterval { get; set; } = 5;

    [ObservableProperty]
    public partial ScheduleUnit ScheduleUnit { get; set; } = ScheduleUnit.Seconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string WatchPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial FileChangeKind WatchChange { get; set; } = FileChangeKind.Any;

    [ObservableProperty]
    public partial string WatchFilter { get; set; } = "*";

    [ObservableProperty]
    public partial bool WatchSubfolders { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string ProcessName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProcessChangeKind ProcessChange { get; set; } = ProcessChangeKind.Started;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial WindowRule WindowTarget { get; set; } = WindowRule.Empty;

    [ObservableProperty]
    public partial WindowChangeKind WindowChange { get; set; } = WindowChangeKind.Appeared;

    [ObservableProperty]
    public partial int IdleSeconds { get; set; } = 60;

    [ObservableProperty]
    public partial ColorMatchCondition ColorMatch { get; set; } = ColorMatchCondition.ColorMatches;

    [ObservableProperty]
    public partial int ColorPositionX { get; set; }

    [ObservableProperty]
    public partial int ColorPositionY { get; set; }

    [ObservableProperty]
    public partial bool UseCursorPosition { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string HexColor { get; set; } = "#000000";

    [ObservableProperty]
    public partial int ColorTolerance { get; set; } = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Abbreviation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotstringEndChars { get; set; } = HotstringCharacters.DefaultEndChars;

    [ObservableProperty]
    public partial bool CaseSensitive { get; set; }

    [ObservableProperty]
    public partial bool MatchInsideWord { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool RequireEndChar { get; set; } = true;

    [ObservableProperty]
    public partial bool EraseTyped { get; set; } = true;

    [ObservableProperty]
    public partial bool KeepEndChar { get; set; } = true;

    public string KindLabel => Strings.Get(Kind switch
    {
        TriggerKind.Key => "Trigger.Keys",
        TriggerKind.Color => "Trigger.Color",
        TriggerKind.Interface => "Trigger.Interface",
        TriggerKind.Hotstring => "Trigger.Hotstring",
        TriggerKind.Menu => "Trigger.Menu",
        TriggerKind.Timer => "Trigger.Timer",
        TriggerKind.File => "Trigger.File",
        TriggerKind.Process => "Trigger.Process",
        TriggerKind.Window => "Trigger.Window",
        _ => "Trigger.Idle",
    });

    /// <summary>Menu entries are managed on the project's menu pages rather than removed here.</summary>
    public bool IsRemovable => Kind != TriggerKind.Menu;

    public string Summary => Kind switch
    {
        TriggerKind.Key => string.IsNullOrWhiteSpace(BindKey) ? Strings.Get("Common.None") : BindKey,
        TriggerKind.Color => HexColor.ToUpperInvariant(),
        TriggerKind.Interface => WindowTarget.HasTarget ? WindowTarget.MatchValue : Strings.Get("WindowRule.NotSet"),
        TriggerKind.Hotstring => Abbreviation.Length == 0
            ? Strings.Get("Common.None")
            : Strings.Format("Trigger.HotstringPreview", Abbreviation, RequireEndChar
                ? Strings.Get("Trigger.HotstringEndRequired")
                : Strings.Get("Trigger.HotstringEndNotRequired")),
        TriggerKind.Menu => Strings.Get("Trigger.MenuMember"),
        TriggerKind.Timer => MacroSchedule.Describe(this),
        TriggerKind.File => WatchPath.Length == 0 ? Strings.Get("Common.None") : WatchPath,
        TriggerKind.Process => ProcessName.Length == 0 ? Strings.Get("Common.None") : ProcessName,
        TriggerKind.Window => WindowTarget.HasTarget
            ? $"{WindowTarget.MatchValue} · {Strings.Get($"WindowChange.{WindowChange}")}"
            : Strings.Get("WindowRule.NotSet"),
        _ => Strings.Format("Idle.Summary", Math.Max(1, IdleSeconds)),
    };

    public override string ToString() => $"{KindLabel}: {Summary}";

    public static TriggerEntry FromMacro(MacroItem macro)
    {
        var kind = macro.TriggerMode switch
        {
            MacroTrigger.ColorPixelChanges => TriggerKind.Color,
            MacroTrigger.Timer => TriggerKind.Timer,
            MacroTrigger.FileChanges => TriggerKind.File,
            MacroTrigger.Process => TriggerKind.Process,
            MacroTrigger.Window => TriggerKind.Window,
            MacroTrigger.Idle => TriggerKind.Idle,
            _ => TriggerKind.Key,
        };

        return new TriggerEntry
        {
            Kind = kind,
            MacroName = macro.Name,
            BindKey = macro.BindKey,
            LoopMode = macro.LoopMode,
            TriggerOnce = macro.TriggerOnce,
            ScheduleTime = macro.ScheduleTime,
            ScheduleMode = macro.ScheduleMode,
            ScheduleInterval = macro.ScheduleInterval,
            ScheduleUnit = macro.ScheduleUnit,
            WatchPath = macro.WatchPath,
            WatchChange = macro.WatchChange,
            WatchFilter = macro.WatchFilter,
            WatchSubfolders = macro.WatchSubfolders,
            ProcessName = macro.ProcessName,
            ProcessChange = macro.ProcessChange,
            WindowTarget = macro.WindowTarget,
            WindowChange = macro.WindowChange,
            IdleSeconds = macro.IdleSeconds,
            ColorMatch = macro.ColorMatch,
            ColorPositionX = macro.ColorPositionX,
            ColorPositionY = macro.ColorPositionY,
            UseCursorPosition = macro.UseCursorPosition,
            HexColor = macro.HexColor,
            ColorTolerance = macro.ColorTolerance,
        };
    }

    public TriggerEntry Copy() => new()
    {
        Kind = Kind,
        MacroName = MacroName,
        MenuPageId = MenuPageId,
        MenuRow = MenuRow,
        MenuColumn = MenuColumn,
        MenuIcon = MenuIcon,
        IsEnabled = IsEnabled,
        BindKey = BindKey,
        LoopMode = LoopMode,
        TriggerOnce = TriggerOnce,
        ScheduleTime = ScheduleTime,
        ScheduleMode = ScheduleMode,
        ScheduleInterval = ScheduleInterval,
        ScheduleUnit = ScheduleUnit,
        WatchPath = WatchPath,
        WatchChange = WatchChange,
        WatchFilter = WatchFilter,
        WatchSubfolders = WatchSubfolders,
        ProcessName = ProcessName,
        ProcessChange = ProcessChange,
        WindowTarget = WindowTarget,
        WindowChange = WindowChange,
        IdleSeconds = IdleSeconds,
        ColorMatch = ColorMatch,
        ColorPositionX = ColorPositionX,
        ColorPositionY = ColorPositionY,
        UseCursorPosition = UseCursorPosition,
        HexColor = HexColor,
        ColorTolerance = ColorTolerance,
        Abbreviation = Abbreviation,
        HotstringEndChars = HotstringEndChars,
        CaseSensitive = CaseSensitive,
        MatchInsideWord = MatchInsideWord,
        RequireEndChar = RequireEndChar,
        EraseTyped = EraseTyped,
        KeepEndChar = KeepEndChar,
    };

    public void SyncFromMacro(MacroItem macro)
    {
        Kind = macro.TriggerMode switch
        {
            MacroTrigger.ColorPixelChanges => TriggerKind.Color,
            MacroTrigger.Timer => TriggerKind.Timer,
            MacroTrigger.FileChanges => TriggerKind.File,
            MacroTrigger.Process => TriggerKind.Process,
            MacroTrigger.Window => TriggerKind.Window,
            MacroTrigger.Idle => TriggerKind.Idle,
            _ => TriggerKind.Key,
        };
        MacroName = macro.Name;
        BindKey = macro.BindKey;
        LoopMode = macro.LoopMode;
        TriggerOnce = macro.TriggerOnce;
        ScheduleTime = macro.ScheduleTime;
        ScheduleMode = macro.ScheduleMode;
        ScheduleInterval = macro.ScheduleInterval;
        ScheduleUnit = macro.ScheduleUnit;
        WatchPath = macro.WatchPath;
        WatchChange = macro.WatchChange;
        WatchFilter = macro.WatchFilter;
        WatchSubfolders = macro.WatchSubfolders;
        ProcessName = macro.ProcessName;
        ProcessChange = macro.ProcessChange;
        WindowTarget = macro.WindowTarget;
        WindowChange = macro.WindowChange;
        IdleSeconds = macro.IdleSeconds;
        ColorMatch = macro.ColorMatch;
        ColorPositionX = macro.ColorPositionX;
        ColorPositionY = macro.ColorPositionY;
        UseCursorPosition = macro.UseCursorPosition;
        HexColor = macro.HexColor;
        ColorTolerance = macro.ColorTolerance;
    }

    public JsonObject ToJson()
    {
        var node = new JsonObject
        {
            ["kind"] = Kind.ToString().ToLowerInvariant(),
            ["macro"] = MacroName,
            ["enabled"] = IsEnabled,
        };

        switch (Kind)
        {
            case TriggerKind.Key:
                node["bindKey"] = BindKey;
                node["loopMode"] = LoopMode.ToString();
                node["triggerOnce"] = TriggerOnce;
                break;
            case TriggerKind.Color:
                node["colorMatch"] = ColorMatch.ToString();
                node["colorPosition"] = new JsonObject { ["x"] = ColorPositionX, ["y"] = ColorPositionY };
                node["useCursorPosition"] = UseCursorPosition;
                node["hexColor"] = HexColor;
                node["colorTolerance"] = ColorTolerance;
                node["triggerOnce"] = TriggerOnce;
                break;
            case TriggerKind.Interface:
                node["window"] = WindowTarget.ToJson();
                break;
            case TriggerKind.Hotstring:
                node["abbreviation"] = Abbreviation;
                node["endChars"] = HotstringEndChars;
                node["caseSensitive"] = CaseSensitive;
                node["matchInsideWord"] = MatchInsideWord;
                node["requireEndChar"] = RequireEndChar;
                node["eraseTyped"] = EraseTyped;
                node["keepEndChar"] = KeepEndChar;
                break;
            case TriggerKind.Menu:
                node["page"] = MenuPageId;
                node["row"] = MenuRow;
                node["column"] = MenuColumn;
                if (MenuIcon is { IsEmpty: false } icon)
                {
                    node["icon"] = icon.ToJson();
                }
                break;
            case TriggerKind.Timer:
                node["schedule"] = new JsonObject
                {
                    ["mode"] = ScheduleMode.ToString(),
                    ["interval"] = ScheduleInterval,
                    ["unit"] = ScheduleUnit.ToString(),
                    ["time"] = ScheduleTime,
                };
                node["triggerOnce"] = TriggerOnce;
                break;
            case TriggerKind.File:
                node["watch"] = new JsonObject
                {
                    ["path"] = WatchPath,
                    ["change"] = WatchChange.ToString(),
                    ["filter"] = WatchFilter,
                    ["subfolders"] = WatchSubfolders,
                };
                node["triggerOnce"] = TriggerOnce;
                break;
            case TriggerKind.Process:
                node["process"] = new JsonObject
                {
                    ["name"] = ProcessName,
                    ["change"] = ProcessChange.ToString(),
                };
                node["triggerOnce"] = TriggerOnce;
                break;
            case TriggerKind.Window:
                node["window"] = WindowTarget.ToJson();
                node["windowChange"] = WindowChange.ToString();
                node["triggerOnce"] = TriggerOnce;
                break;
            case TriggerKind.Idle:
                node["idleSeconds"] = IdleSeconds;
                node["triggerOnce"] = TriggerOnce;
                break;
        }

        return node;
    }

    public static TriggerEntry FromJson(JsonObject node)
    {
        var kindText = Text(node, "kind", "key");
        var entry = new TriggerEntry
        {
            Kind = Enum.TryParse<TriggerKind>(kindText, true, out var kind) ? kind : TriggerKind.Key,
            MacroName = Text(node, "macro"),
            MenuPageId = Text(node, "page"),
            MenuRow = Number(node, "row", -1),
            MenuColumn = Number(node, "column", -1),
            MenuIcon = node["icon"] is JsonObject icon ? MenuIconReference.FromJson(icon) : null,
            IsEnabled = Bool(node, "enabled", true),
            BindKey = Text(node, "bindKey"),
            LoopMode = Enum.TryParse<MacroLoop>(Text(node, "loopMode"), true, out var loop)
                ? loop : MacroLoop.Press,
            TriggerOnce = Bool(node, "triggerOnce"),
            ColorMatch = Enum.TryParse<ColorMatchCondition>(Text(node, "colorMatch"), true, out var match)
                ? match : ColorMatchCondition.ColorMatches,
            ColorPositionX = Number(node["colorPosition"] as JsonObject, "x"),
            ColorPositionY = Number(node["colorPosition"] as JsonObject, "y"),
            UseCursorPosition = Bool(node, "useCursorPosition"),
            HexColor = Text(node, "hexColor", "#000000"),
            ColorTolerance = Number(node, "colorTolerance", 5),
            Abbreviation = Text(node, "abbreviation"),
            HotstringEndChars = Text(node, "endChars", HotstringCharacters.DefaultEndChars),
            CaseSensitive = Bool(node, "caseSensitive"),
            MatchInsideWord = Bool(node, "matchInsideWord"),
            RequireEndChar = Bool(node, "requireEndChar", true),
            EraseTyped = Bool(node, "eraseTyped", true),
            KeepEndChar = Bool(node, "keepEndChar", true),
            ProcessName = Text(node["process"] as JsonObject, "name"),
            ProcessChange = Enum.TryParse<ProcessChangeKind>(
                Text(node["process"] as JsonObject, "change"), true, out var process)
                ? process : ProcessChangeKind.Started,
            WatchPath = Text(node["watch"] as JsonObject, "path"),
            WatchChange = Enum.TryParse<FileChangeKind>(
                Text(node["watch"] as JsonObject, "change"), true, out var file)
                ? file : FileChangeKind.Any,
            WatchFilter = Text(node["watch"] as JsonObject, "filter", "*"),
            WatchSubfolders = Bool(node["watch"] as JsonObject, "subfolders"),
            WindowTarget = node["window"] is JsonObject window ? WindowRule.FromJson(window) : WindowRule.Empty,
            WindowChange = Enum.TryParse<WindowChangeKind>(Text(node, "windowChange"), true, out var win)
                ? win : WindowChangeKind.Appeared,
            IdleSeconds = Number(node, "idleSeconds", 60),
        };

        var schedule = node["schedule"] as JsonObject;
        if (Enum.TryParse<ScheduleMode>(Text(schedule, "mode"), true, out var mode))
        {
            entry.ScheduleMode = mode;
        }

        entry.ScheduleInterval = Number(schedule, "interval", 5);
        if (Enum.TryParse<ScheduleUnit>(Text(schedule, "unit"), true, out var unit))
        {
            entry.ScheduleUnit = unit;
        }

        entry.ScheduleTime = Text(schedule, "time", "08:00");
        return entry;
    }

    private static string Text(JsonObject? node, string name, string fallback = "")
        => node?[name]?.GetValue<string>() ?? fallback;

    private static bool Bool(JsonObject? node, string name, bool fallback = false)
        => node?[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static int Number(JsonObject? node, string name, int fallback = 0)
        => node?[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;
}
