using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Models;

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
    public partial MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

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
                ? loop : MacroLoop.Toggle,
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

/// <summary>Where one window-bound floating group sits inside its target client area.</summary>
public sealed record InterfaceLayout(WindowRule Window, int OffsetX, int OffsetY,
    string ToggleHotkey = "")
{
    public string GroupKey => ProjectTriggers.GroupKey(Window);

    public JsonObject ToJson() => new()
    {
        ["window"] = Window.ToJson(),
        ["x"] = OffsetX,
        ["y"] = OffsetY,
        ["toggleHotkey"] = ToggleHotkey,
    };

    public static InterfaceLayout FromJson(JsonObject node)
        => new(
            node["window"] is JsonObject window ? WindowRule.FromJson(window) : WindowRule.Empty,
            node["x"]?.GetValue<int>() ?? 16,
            node["y"]?.GetValue<int>() ?? 64,
            node["toggleHotkey"]?.GetValue<string>() ?? string.Empty);
}

public enum MenuPageRegion
{
    Common,
    Context,
}

public enum MenuIconKind
{
    Builtin,
    Image,
}

/// <summary>A built-in line icon name or an image stored beside the project.</summary>
public sealed record MenuIconReference(MenuIconKind Kind, string Value)
{
    public bool IsEmpty => Value.Trim().Length == 0;

    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind.ToString().ToLowerInvariant(),
        ["value"] = Value,
    };

    public static MenuIconReference FromJson(JsonObject node)
        => new(Enum.TryParse<MenuIconKind>(node["kind"]?.GetValue<string>(), true, out var kind)
                ? kind
                : MenuIconKind.Builtin,
            node["value"]?.GetValue<string>() ?? string.Empty);
}

/// <summary>
/// One common page or one lower context layout. Common pages are paged horizontally. Context
/// pages are tried in list order against the foreground window; the default context page is used
/// when none matches.
/// </summary>
public sealed record MenuPage(
    string Id,
    MenuPageRegion Region,
    WindowRule Window,
    bool IsDefault = false)
{
    public JsonObject ToJson()
    {
        var node = new JsonObject
        {
            ["id"] = Id,
            ["region"] = Region.ToString().ToLowerInvariant(),
            ["default"] = IsDefault,
        };
        if (Window.HasTarget)
        {
            node["window"] = Window.ToJson();
        }

        return node;
    }

    public static MenuPage FromJson(JsonObject node)
        => new(
            node["id"]?.GetValue<string>() is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N")[..10],
            Enum.TryParse<MenuPageRegion>(node["region"]?.GetValue<string>(), true, out var region)
                ? region
                : MenuPageRegion.Common,
            node["window"] is JsonObject window ? WindowRule.FromJson(window) : WindowRule.Empty,
            node["default"]?.GetValue<bool>() ?? false);
}

/// <summary>One automatic interface group: every entry has the same normalised window rule.</summary>
public sealed record InterfaceGroupView(
    string Key, WindowRule Rule, IReadOnlyList<TriggerEntry> Entries);

public sealed record MenuRuleGroupView(
    string Key, WindowRule Rule, IReadOnlyList<MenuPage> Pages);

/// <summary>
/// Every trigger entry and trigger-surface setting in the project. A macro body can be reached by
/// any number of these entries; none of them changes the macro's steps.
/// </summary>
public sealed class ProjectTriggers
{
    public const int MenuColumns = 4;
    public const int MenuRegionRows = 3;
    public const int MenuRows = MenuRegionRows * 2;
    public const int MaxCommonMenuPages = 8;

    private string _menuLauncher = string.Empty;

    public ProjectTriggers()
    {
        Entries.CollectionChanged += OnCollectionChanged;
        InterfaceLayouts.CollectionChanged += OnSurfaceCollectionChanged;
        MenuPages.CollectionChanged += OnSurfaceCollectionChanged;
        BlockedMenuRules.CollectionChanged += OnSurfaceCollectionChanged;
        EnsureMenuPages();
    }

    public ObservableCollection<TriggerEntry> Entries { get; } = [];

    public ObservableCollection<InterfaceLayout> InterfaceLayouts { get; } = [];

    public ObservableCollection<MenuPage> MenuPages { get; } = [];

    /// <summary>Programs whose foreground windows do not open the macro menu.</summary>
    public ObservableCollection<WindowRule> BlockedMenuRules { get; } = [];

    public event EventHandler? Changed;

    internal void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public string MenuLauncher
    {
        get => _menuLauncher;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (_menuLauncher == next)
            {
                return;
            }

            _menuLauncher = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<TriggerEntry> ForMacro(string macroName)
        => [.. Entries.Where(entry =>
            string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase))];

    public bool HasPage(string macroName, DockPage page)
    {
        var entries = ForMacro(macroName);
        return page switch
        {
            DockPage.Keys => entries.Count == 0 || entries.Any(entry =>
                entry.Kind is TriggerKind.Key or TriggerKind.Color),
            DockPage.Interface => entries.Any(entry => entry.Kind == TriggerKind.Interface),
            DockPage.Hotstring => entries.Any(entry => entry.Kind == TriggerKind.Hotstring),
            DockPage.Menu => entries.Any(entry => entry.Kind == TriggerKind.Menu),
            _ => entries.Any(entry => entry.Kind is TriggerKind.Timer or TriggerKind.File
                or TriggerKind.Process or TriggerKind.Window or TriggerKind.Idle),
        };
    }

    public IReadOnlyList<TriggerEntry> InterfaceEntries()
        => [.. Entries.Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Interface
            && entry.WindowTarget.HasTarget)];

    public IReadOnlyList<TriggerEntry> HotstringEntries()
        => [.. Entries.Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Hotstring
            && entry.Abbreviation.Length > 0)];

    public IReadOnlyList<TriggerEntry> MenuMembers()
        => [.. Entries.Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Menu)];

    public IReadOnlyList<MenuPage> CommonMenuPages()
        => [.. MenuPages.Where(page => page.Region == MenuPageRegion.Common)];

    public IReadOnlyList<MenuPage> ContextMenuPages()
        => [.. MenuPages.Where(page => page.Region == MenuPageRegion.Context && !page.IsDefault)];

    public IReadOnlyList<MenuPage> DefaultContextMenuPages()
        => [.. MenuPages.Where(page => page.Region == MenuPageRegion.Context && page.IsDefault)];

    public IReadOnlyList<MenuPage> ContextPagesFor(WindowRule rule)
    {
        var key = GroupKey(rule);
        return [.. MenuPages.Where(page => page.Region == MenuPageRegion.Context
            && !page.IsDefault && GroupKey(page.Window) == key)];
    }

    public IReadOnlyList<MenuRuleGroupView> ContextRuleGroups()
        => [.. ContextMenuPages()
            .GroupBy(page => GroupKey(page.Window))
            .Select(group =>
            {
                var pages = group.ToList();
                return new MenuRuleGroupView(group.Key, pages[0].Window, [.. pages]);
            })];

    /// <summary>
    /// Finds the ordinary menu rule for one program, creating an empty one first when needed. The
    /// returned page is the one the runtime menu should select for the foreground window.
    /// </summary>
    public MenuPage EnsureProcessMenuRule(WindowInfo foreground, string process, string className)
    {
        var key = process.Trim();
        var existing = ContextRuleGroups().FirstOrDefault(group =>
            group.Rule.How == WindowHow.ProcessEquals
            && string.Equals(group.Rule.Process, key, StringComparison.OrdinalIgnoreCase));
        return existing?.Pages[0]
               ?? AddContextMenuPage(new WindowRule(
                   foreground.Title,
                   key,
                   className,
                   foreground.Handle,
                   WindowHow.ProcessEquals), first: true);
    }

    /// <summary>Adds one program-wide launcher block, answering false when it already exists.</summary>
    public bool BlockMenuForProcess(WindowInfo foreground, string process, string className)
    {
        var key = process.Trim();
        if (BlockedMenuRules.Any(rule =>
                rule.How == WindowHow.ProcessEquals
                && string.Equals(rule.Process, key, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        BlockedMenuRules.Add(new WindowRule(
            foreground.Title,
            key,
            className,
            foreground.Handle,
            WindowHow.ProcessEquals));
        return true;
    }

    public MenuPage DefaultContextMenuPage()
        => MenuPages.FirstOrDefault(page =>
               page.Region == MenuPageRegion.Context && page.IsDefault)
           ?? EnsureDefaultContextPage();

    public MenuPage AddCommonMenuPage()
    {
        var common = CommonMenuPages();
        if (common.Count >= MaxCommonMenuPages)
        {
            return common[^1];
        }

        var page = new MenuPage(NewMenuPageId(), MenuPageRegion.Common, WindowRule.Empty);
        MenuPages.Add(page);
        return page;
    }

    public MenuPage AddContextMenuPage(WindowRule rule, bool first = false)
    {
        var page = new MenuPage(NewMenuPageId(), MenuPageRegion.Context, rule);
        if (first)
        {
            var at = MenuPages
                .Select((candidate, index) => (candidate, index))
                .FirstOrDefault(item => item.candidate.Region == MenuPageRegion.Context
                    && !item.candidate.IsDefault);
            MenuPages.Insert(at.candidate is null ? MenuPages.Count : at.index, page);
            return page;
        }

        var last = MenuPages
            .Select((candidate, index) => (candidate, index))
            .LastOrDefault(item => item.candidate.Region == MenuPageRegion.Context
                && !item.candidate.IsDefault
                && GroupKey(item.candidate.Window) == GroupKey(rule));
        if (last.candidate is null)
        {
            MenuPages.Add(page);
        }
        else
        {
            MenuPages.Insert(last.index + 1, page);
        }
        return page;
    }

    public MenuPage AddDefaultContextPage()
    {
        var page = new MenuPage(NewMenuPageId(), MenuPageRegion.Context,
            WindowRule.Empty, IsDefault: true);
        var last = MenuPages
            .Select((candidate, index) => (candidate, index))
            .LastOrDefault(item => item.candidate.Region == MenuPageRegion.Context
                && item.candidate.IsDefault);
        MenuPages.Insert(last.candidate is null ? MenuPages.Count : last.index + 1, page);
        return page;
    }

    public void RemoveMenuPage(string pageId)
    {
        if (MenuPages.FirstOrDefault(page => page.Id == pageId) is not { } page
            || (page.IsDefault && DefaultContextMenuPages().Count <= 1))
        {
            return;
        }

        foreach (var entry in Entries.Where(entry =>
                     entry.Kind == TriggerKind.Menu && entry.MenuPageId == pageId).ToList())
        {
            Entries.Remove(entry);
        }

        MenuPages.Remove(page);
    }

    /// <summary>
    /// Whether a page may be removed from the menu. Each paging region keeps its first page so the
    /// runtime view always has a stable starting position.
    /// </summary>
    public bool CanRemoveMenuPage(string pageId)
    {
        var page = MenuPages.FirstOrDefault(candidate => candidate.Id == pageId);
        if (page is null)
        {
            return false;
        }

        var siblings = page.Region == MenuPageRegion.Common
            ? CommonMenuPages()
            : page.IsDefault
                ? DefaultContextMenuPages()
                : ContextPagesFor(page.Window);
        return siblings.Count > 1 && siblings[0].Id != pageId;
    }

    public void RemoveContextRule(string ruleKey)
    {
        var pages = ContextMenuPages()
            .Where(page => GroupKey(page.Window) == ruleKey)
            .ToList();
        var pageIds = pages.Select(page => page.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Entries.Where(entry =>
                     entry.Kind == TriggerKind.Menu && pageIds.Contains(entry.MenuPageId)).ToList())
        {
            Entries.Remove(entry);
        }
        foreach (var page in pages)
        {
            MenuPages.Remove(page);
        }
    }

    /// <summary>
    /// Whether the foreground window belongs to a program that must not open the menu. The rule is
    /// checked against the foreground's own process or class instead of the first window the
    /// listing happens to return for that program.
    /// </summary>
    public bool IsMenuBlocked(WindowInfo foreground, IWindowDevice windows)
        => BlockedMenuRules.Any(rule => WindowRuleMatches(rule, foreground, windows));

    public static bool WindowRuleMatches(WindowRule rule, WindowInfo foreground,
        IWindowDevice windows)
    {
        try
        {
            var reading = rule.Reading;
            if (reading.Match is WindowMatch.Handle)
            {
                return rule.Handle != 0 && foreground.Handle == rule.Handle;
            }

            var text = reading.Match switch
            {
                WindowMatch.Process => windows.ProcessOf(foreground.Handle),
                WindowMatch.ClassName => windows.ClassOf(foreground.Handle),
                _ => foreground.Title,
            };
            return WindowHow.Matches(text, rule.MatchValue, reading.Compare, rule.Options);
        }
        catch
        {
            return false;
        }
    }

    public void MoveContextRule(string ruleKey, string targetRuleKey)
    {
        if (ruleKey == targetRuleKey)
        {
            return;
        }

        var groups = ContextRuleGroups().ToList();
        var moving = groups.FirstOrDefault(group => group.Key == ruleKey);
        var target = groups.FirstOrDefault(group => group.Key == targetRuleKey);
        if (moving is null || target is null)
        {
            return;
        }

        var movingIndex = groups.IndexOf(moving);
        var targetIndexBeforeMove = groups.IndexOf(target);
        groups.Remove(moving);
        var insertion = movingIndex < targetIndexBeforeMove
            ? groups.IndexOf(target) + 1
            : groups.IndexOf(target);
        groups.Insert(Math.Clamp(insertion, 0, groups.Count), moving);
        var flattened = groups.SelectMany(group => group.Pages).ToList();
        foreach (var page in ContextMenuPages())
        {
            MenuPages.Remove(page);
        }
        var anchor = MenuPages.LastOrDefault(page =>
            page.Region == MenuPageRegion.Context && page.IsDefault);
        var index = anchor is null ? MenuPages.Count : MenuPages.IndexOf(anchor) + 1;
        foreach (var page in flattened)
        {
            MenuPages.Insert(index++, page);
        }
    }

    public void ResetMenu()
    {
        MenuLauncher = string.Empty;
        BlockedMenuRules.Clear();
        MenuPages.Clear();
        EnsureMenuPages();
    }

    public IReadOnlyList<TriggerEntry> MenuMembers(string pageId)
        => [.. Entries.Where(entry => entry.Kind == TriggerKind.Menu
            && entry.IsEnabled && entry.MenuPageId == pageId)];

    public IReadOnlyList<TriggerEntry> GroupedInterfaceEntries(WindowRule rule)
    {
        var key = GroupKey(rule);
        return [.. InterfaceEntries().Where(entry => GroupKey(entry.WindowTarget) == key)];
    }

    public IReadOnlyList<InterfaceGroupView> InterfaceGroups()
        => [.. InterfaceEntries()
            .GroupBy(entry => GroupKey(entry.WindowTarget))
            .Select(group =>
            {
                var members = group.ToList();
                return new InterfaceGroupView(group.Key, members[0].WindowTarget, [.. members]);
            })];

    public InterfaceLayout LayoutFor(WindowRule rule)
    {
        var key = GroupKey(rule);
        return InterfaceLayouts.FirstOrDefault(layout => layout.GroupKey == key)
            ?? new InterfaceLayout(rule, 16, 64);
    }

    public void StoreLayout(WindowRule rule, int x, int y)
    {
        var key = GroupKey(rule);
        var current = InterfaceLayouts.FirstOrDefault(layout => layout.GroupKey == key);
        var hotkey = current?.ToggleHotkey ?? string.Empty;
        if (current is not null)
        {
            InterfaceLayouts.Remove(current);
        }

        InterfaceLayouts.Add(new InterfaceLayout(rule, x, y, hotkey));
    }

    public void StoreInterfaceHotkey(WindowRule rule, string hotkey)
    {
        var key = GroupKey(rule);
        var current = InterfaceLayouts.FirstOrDefault(layout => layout.GroupKey == key);
        var next = new InterfaceLayout(rule, current?.OffsetX ?? 16, current?.OffsetY ?? 64,
            hotkey.Trim());
        if (current is null)
        {
            InterfaceLayouts.Add(next);
        }
        else
        {
            InterfaceLayouts[InterfaceLayouts.IndexOf(current)] = next;
        }
    }

    public void AssignFirstEmptyMenuCell(TriggerEntry entry, string pageId)
    {
        var occupied = Entries
            .Where(candidate => !ReferenceEquals(candidate, entry)
                && candidate.Kind == TriggerKind.Menu
                && candidate.MenuPageId == pageId
                && candidate.MenuRow >= 0 && candidate.MenuColumn >= 0)
            .Select(candidate => (candidate.MenuRow, candidate.MenuColumn))
            .ToHashSet();
        for (var row = 0; row < MenuRegionRows; row++)
        {
            for (var column = 0; column < MenuColumns; column++)
            {
                if (occupied.Add((row, column)))
                {
                    entry.MenuPageId = pageId;
                    entry.MenuRow = row;
                    entry.MenuColumn = column;
                    return;
                }
            }
        }

        entry.MenuRow = -1;
        entry.MenuColumn = -1;
    }

    public void ReplaceMacro(string macroName, IEnumerable<TriggerEntry> entries)
    {
        var replacements = entries.Select(entry => entry.Copy()).ToList();
        var old = Entries.Where(entry =>
            string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase)).ToList();
        var keepAt = old.Count > 0 ? Entries.IndexOf(old[0]) : Entries.Count;
        foreach (var entry in old)
        {
            Entries.Remove(entry);
        }

        foreach (var entry in replacements)
        {
            entry.MacroName = macroName;
            Entries.Insert(Math.Min(keepAt++, Entries.Count), entry);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RenameMacro(string oldName, string newName)
    {
        foreach (var entry in Entries.Where(entry =>
                     string.Equals(entry.MacroName, oldName, StringComparison.OrdinalIgnoreCase)))
        {
            entry.MacroName = newName;
        }
    }

    public void RemoveMacro(string macroName)
    {
        foreach (var entry in Entries.Where(entry =>
                     string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Entries.Remove(entry);
        }
    }

    public void Attach(IReadOnlyList<MacroItem> macros)
    {
        foreach (var macro in macros)
        {
            macro.Triggers.Clear();
            var entries = ForMacro(macro.Name);
            if (entries.Count == 0)
            {
                entries = [TriggerEntry.FromMacro(macro)];
            }

            macro.Triggers.AddRange(entries.Select(entry => entry.Copy()));
            macro.InterfaceLayouts.Clear();
            macro.InterfaceLayouts.AddRange(InterfaceLayouts.Select(layout => layout with { }));
            macro.RefreshTriggers();
        }
    }

    public void ReplaceAll(ProjectTriggers other)
    {
        Entries.Clear();
        foreach (var entry in other.Entries)
        {
            Entries.Add(entry.Copy());
        }

        InterfaceLayouts.Clear();
        foreach (var layout in other.InterfaceLayouts)
        {
            InterfaceLayouts.Add(layout with { });
        }

        MenuPages.Clear();
        foreach (var page in other.MenuPages)
        {
            MenuPages.Add(page with { });
        }
        BlockedMenuRules.Clear();
        foreach (var rule in other.BlockedMenuRules)
        {
            BlockedMenuRules.Add(rule with { });
        }
        EnsureMenuPages();
        MenuLauncher = other.MenuLauncher;
    }

    /// <summary>Merges menu pages and launcher settings from an imported package.</summary>
    public void MergeMenuFrom(ProjectTriggers imported, IReadOnlyList<MacroItem> importedMacros)
    {
        var importedNames = importedMacros.Select(macro => macro.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultPages = DefaultContextMenuPages().ToList();
        if (defaultPages.Count == 0)
        {
            defaultPages.Add(DefaultContextMenuPage());
        }
        var defaultIndex = 0;
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var page in imported.MenuPages)
        {
            if (page.Region == MenuPageRegion.Context && page.IsDefault)
            {
                if (defaultIndex >= defaultPages.Count)
                {
                    defaultPages.Add(AddDefaultContextPage());
                }
                idMap[page.Id] = defaultPages[defaultIndex++].Id;
                continue;
            }

            if (page.Region == MenuPageRegion.Common && CommonMenuPages().Count >= MaxCommonMenuPages)
            {
                continue;
            }

            var id = MenuPages.Any(existing => existing.Id == page.Id)
                ? NewMenuPageId()
                : page.Id;
            idMap[page.Id] = id;
            MenuPages.Add(page with { Id = id });
        }

        foreach (var entry in Entries.Where(entry => entry.Kind == TriggerKind.Menu
                     && importedNames.Contains(entry.MacroName)
                     && idMap.TryGetValue(entry.MenuPageId, out _)))
        {
            entry.MenuPageId = idMap[entry.MenuPageId];
        }

        if (MenuLauncher.Length == 0)
        {
            MenuLauncher = imported.MenuLauncher;
        }

        foreach (var rule in imported.BlockedMenuRules)
        {
            if (!BlockedMenuRules.Any(existing => GroupKey(existing) == GroupKey(rule)))
            {
                BlockedMenuRules.Add(rule with { });
            }
        }

        EnsureMenuPages();
    }

    public Runtime.MenuRuntimeLayout BuildMenuLayout(WindowInfo? foreground,
        IWindowDevice windows, IReadOnlyList<MacroItem> macros)
    {
        var byName = macros
            .GroupBy(macro => macro.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        MenuRuntimePage Page(MenuPage page)
        {
            var cells = Entries
                .Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Menu
                    && entry.MenuPageId == page.Id
                    && entry.MenuRow is >= 0 and < MenuRegionRows
                    && entry.MenuColumn is >= 0 and < MenuColumns)
                .Select(entry => (Entry: entry,
                    Macro: byName.GetValueOrDefault(entry.MacroName)))
                .Where(item => item.Macro is { IsEnabled: true })
                .GroupBy(item => (item.Entry.MenuRow, item.Entry.MenuColumn))
                .Select(group =>
                {
                    var item = group.First();
                    return new Runtime.MenuRuntimeCell(
                        item.Entry.MenuRow,
                        item.Entry.MenuColumn,
                        item.Entry.MenuIcon,
                        item.Entry,
                        item.Macro!);
                })
                .OrderBy(cell => cell.Row)
                .ThenBy(cell => cell.Column)
                .ToList();
            return new Runtime.MenuRuntimePage(page.Id, page.IsDefault,
                page.IsDefault
                    ? Strings.Get("Menu.DefaultContext")
                    : $"{page.Window.MatchValue} · {Strings.Get($"WindowRule.Short.{page.Window.How}", page.Window.How)}",
                cells);
        }

        var common = CommonMenuPages().Select(Page).ToList();
        if (common.Count == 0)
        {
            common.Add(Page(AddCommonMenuPage()));
        }

        IReadOnlyList<MenuPage>? selectedContext = null;
        if (foreground is not null)
        {
            foreach (var candidate in ContextRuleGroups())
            {
                try
                {
                    var rule = candidate.Pages[0].Window;
                    if (WindowRuleMatches(rule, foreground, windows))
                    {
                        selectedContext = candidate.Pages;
                        break;
                    }
                }
                catch
                {
                    // An unreadable rule leaves the next page to answer.
                }
            }
        }

        var contextPages = selectedContext is null
            ? DefaultContextMenuPages().Select(Page).ToList()
            : selectedContext.Select(Page).ToList();
        if (contextPages.Count == 0)
        {
            contextPages.Add(Page(DefaultContextMenuPage()));
        }
        return new Runtime.MenuRuntimeLayout(common, contextPages);
    }

    public IReadOnlyList<string> MenuProblems(IReadOnlyList<MacroItem> macros)
    {
        var problems = new List<string>();
        if (CommonMenuPages().Count > MaxCommonMenuPages)
        {
            problems.Add(Strings.Get("Menu.Problem.TooManyPages"));
        }

        if (MenuPages.GroupBy(page => page.Id).Any(group => group.Count() > 1))
        {
            problems.Add(Strings.Get("Menu.Problem.PageId"));
        }

        if (ContextMenuPages().Any(page => !page.Window.HasTarget))
        {
            problems.Add(Strings.Get("Menu.Problem.RuleEmpty"));
        }
        if (BlockedMenuRules.Any(rule => !rule.HasTarget))
        {
            problems.Add(Strings.Get("Menu.Problem.BlockedRuleEmpty"));
        }

        var names = macros.Select(macro => macro.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries.Where(entry => entry.Kind == TriggerKind.Menu))
        {
            if (!MenuPages.Any(page => page.Id == entry.MenuPageId))
            {
                problems.Add(Strings.Format("Menu.Problem.PageMissing", entry.MacroName));
            }
            if (entry.MenuRow is < 0 or >= MenuRegionRows
                || entry.MenuColumn is < 0 or >= MenuColumns)
            {
                problems.Add(Strings.Format("Menu.Problem.CellRange", entry.MacroName));
            }
            if (!names.Contains(entry.MacroName))
            {
                problems.Add(Strings.Format("Menu.Problem.MacroMissing", entry.MacroName));
            }
            if (entry.MenuIcon is { IsEmpty: false } icon
                && icon.Kind == MenuIconKind.Builtin
                && MenuIconCatalog.Find(icon.Value) is null)
            {
                problems.Add(Strings.Format("Menu.Problem.Icon", entry.MacroName));
            }
        }

        foreach (var duplicate in Entries
                     .Where(entry => entry.Kind == TriggerKind.Menu && entry.IsEnabled)
                     .GroupBy(entry => (entry.MenuPageId, entry.MenuRow, entry.MenuColumn))
                     .Where(group => group.Count() > 1))
        {
            problems.Add(Strings.Format("Menu.Problem.CellConflict",
                duplicate.Key.MenuRow + 1, duplicate.Key.MenuColumn + 1));
        }

        if (MenuLauncher.Length > 0 && !IsAllowedMenuLauncher(MenuLauncher))
        {
            problems.Add(Strings.Get("Menu.Problem.Launcher"));
        }

        return problems;
    }

    public static bool IsAllowedMenuLauncher(string? binding)
    {
        var text = binding?.Trim() ?? string.Empty;
        if (text.Length == 0
            || text.Contains("Wheel ", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse Left", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse Right", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Equals("Mouse Middle", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse 4", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse 5", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !text.StartsWith("Mouse ", StringComparison.OrdinalIgnoreCase)
               && HotkeyBinding.IsSupported(text);
    }

    public static string GroupKey(WindowRule rule)
        => string.Join('\u001f',
            rule.Title, rule.Process, rule.ClassName, rule.How,
            rule.CaseSensitive, rule.Trim, rule.Multiline);

    public ProjectTriggers Copy()
    {
        var copy = new ProjectTriggers();
        foreach (var entry in Entries)
        {
            copy.Entries.Add(entry.Copy());
        }

        foreach (var layout in InterfaceLayouts)
        {
            copy.InterfaceLayouts.Add(layout with { });
        }

        copy.MenuPages.Clear();
        foreach (var page in MenuPages)
        {
            copy.MenuPages.Add(page with { });
        }
        foreach (var rule in BlockedMenuRules)
        {
            copy.BlockedMenuRules.Add(rule with { });
        }
        copy.MenuLauncher = MenuLauncher;

        return copy;
    }

    public JsonObject ToJson()
    {
        var entries = new JsonArray();
        foreach (var entry in Entries)
        {
            entries.Add(entry.ToJson());
        }

        var layouts = new JsonArray();
        foreach (var layout in InterfaceLayouts)
        {
            layouts.Add(layout.ToJson());
        }

        return new JsonObject
        {
            ["entries"] = entries,
            ["interfaceLayouts"] = layouts,
            ["menu"] = new JsonObject
            {
                ["launcher"] = MenuLauncher,
                ["pages"] = new JsonArray([.. MenuPages.Select(page => page.ToJson())]),
                ["blockedRules"] = new JsonArray(
                    [.. BlockedMenuRules.Select(rule => (JsonNode)rule.ToJson())]),
            },
        };
    }

    public static ProjectTriggers FromJson(JsonObject? node)
    {
        var triggers = new ProjectTriggers();
        foreach (var item in node?["entries"] as JsonArray ?? [])
        {
            if (item is JsonObject entry)
            {
                triggers.Entries.Add(TriggerEntry.FromJson(entry));
            }
        }

        foreach (var item in node?["interfaceLayouts"] as JsonArray ?? [])
        {
            if (item is JsonObject layout)
            {
                triggers.InterfaceLayouts.Add(InterfaceLayout.FromJson(layout));
            }
        }

        if (node?["menu"] is JsonObject menu)
        {
            triggers.MenuPages.Clear();
            foreach (var item in menu["pages"] as JsonArray ?? [])
            {
                if (item is JsonObject page)
                {
                    triggers.MenuPages.Add(MenuPage.FromJson(page));
                }
            }
            triggers.MenuLauncher = menu["launcher"]?.GetValue<string>() ?? string.Empty;
            foreach (var item in menu["blockedRules"] as JsonArray ?? [])
            {
                if (item is JsonObject rule)
                {
                    triggers.BlockedMenuRules.Add(WindowRule.FromJson(rule));
                }
            }
        }

        triggers.EnsureMenuPages();
        return triggers;
    }

    public static ProjectTriggers FromMacros(IEnumerable<MacroItem> macros)
    {
        var triggers = new ProjectTriggers();
        foreach (var macro in macros)
        {
            var entries = macro.Triggers.Count > 0
                ? macro.Triggers.Select(entry => entry.Copy())
                : [TriggerEntry.FromMacro(macro)];
            foreach (var entry in entries)
            {
                entry.MacroName = macro.Name;
                triggers.Entries.Add(entry);
            }
        }

        foreach (var layout in macros.SelectMany(macro => macro.InterfaceLayouts)
                     .DistinctBy(layout => layout.GroupKey))
        {
            triggers.InterfaceLayouts.Add(layout with { });
        }
        return triggers;
    }

    private void EnsureMenuPages()
    {
        if (CommonMenuPages().Count == 0)
        {
            MenuPages.Insert(0,
                new MenuPage("common-1", MenuPageRegion.Common, WindowRule.Empty));
        }

        EnsureDefaultContextPage();
    }

    private MenuPage EnsureDefaultContextPage()
    {
        if (MenuPages.FirstOrDefault(page =>
                page.Region == MenuPageRegion.Context && page.IsDefault) is { } existing)
        {
            return existing;
        }

        var page = new MenuPage("default", MenuPageRegion.Context,
            WindowRule.Empty, IsDefault: true);
        MenuPages.Add(page);
        return page;
    }

    private static string NewMenuPageId() => Guid.NewGuid().ToString("N")[..10];

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TriggerEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= OnEntryChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TriggerEntry entry in e.NewItems)
            {
                entry.PropertyChanged += OnEntryChanged;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);

    private void OnSurfaceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);
}
