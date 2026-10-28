using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

/// <summary>One entry of the language picker, named in its own language.</summary>
public sealed record LanguageOption(Language Language, string Display)
{
    public override string ToString() => Display;
}

/// <summary>One entry of the theme picker; the name is read when the entry is shown.</summary>
public sealed record ThemeOption(ThemeMode Mode, string Key)
{
    public override string ToString() => Strings.Get(Key);
}

/// <summary>
/// One entry of a capture picker. What it reads as comes from the text table rather than being
/// copied here, so the two pickers and the ways named in a failure are named the same way.
/// </summary>
public sealed record CaptureOption(CaptureWay Way, string Key)
{
    public override string ToString() => Strings.Get(Key);
}

public enum SettingsRootPage
{
    System,
    Triggers,
    Appearance,
}

public enum SettingsSubPage
{
    General,
    CaptureInput,
    Devices,
    MenuLauncher,
    WindowRules,
    Theme,
}

public sealed record MenuRuleRow(
    string GroupKey,
    string Summary,
    WhaleGenie.Core.Devices.WindowRule Rule,
    bool IsDefault,
    int PageCount,
    bool IsBlocked = false)
{
    public string PageCountLabel => IsBlocked
        ? Strings.Get("Settings.MenuRuleBlocked")
        : Strings.Format("Settings.MenuRulePageCount", PageCount);
}

/// <summary>Backs the settings window. Changing the language applies immediately.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel()
        : this(SharedDevices.Current.Driver)
    {
    }

    /// <summary>
    /// The same, over whatever stands behind driver-level input: the program's own, or one a check
    /// hands in so that a machine which is not this one can be described.
    /// </summary>
    internal SettingsViewModel(IDriverInput driver, bool? elevated = null,
        MainViewModel? project = null, string? packagePath = null)
        : this(() => driver.State, elevated, project, packagePath)
    {
    }

    /// <summary>
    /// The same, with what this machine has of driver-level input handed in, so a check can describe
    /// a machine that is not this one instead of asking the registry — and with what rights the
    /// program has, which a check cannot give itself either.
    /// </summary>
    internal SettingsViewModel(Func<DriverInputState> driverInput, bool? elevated = null,
        MainViewModel? project = null, string? packagePath = null)
    {
        Project = project;
        PackagePath = packagePath;
        SelectedLanguage = Languages.First(option => option.Language == Strings.Current.Language);
        SelectedTheme = ThemeOptions.First(option => option.Mode == LocalSettings.LoadTheme().Mode);
        _themeReady = true;
        PreloadOcr = LocalSettings.LoadPreloadOcr();
        AttachDriverAtStart = LocalSettings.LoadDriverAttachAtStart();
        SelectedWindowWay = WindowWays.First(option => option.Way == CaptureChoice.Current.Window);
        SelectedScreenWay = ScreenWays.First(option => option.Way == CaptureChoice.Current.Screen);
        IsElevated = elevated ?? ProcessRights.IsElevated;

        DriverState = driverInput();
        DriverReady = DriverState == DriverInputState.Ready;
        MenuLauncher = project?.Triggers.MenuLauncher ?? string.Empty;
        RefreshMenuRules();
    }

    /// <summary>Re-reads the capture choices, which show their own language when read.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(WindowWays));
        OnPropertyChanged(nameof(ScreenWays));
        OnPropertyChanged(nameof(ThemeOptions));
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(MenuLauncherStatus));
        OnPropertyChanged(nameof(MenuRuleProblem));
    }

    public MainViewModel? Project { get; }

    public string? PackagePath { get; }

    public bool HasProject => Project is not null;

    [ObservableProperty]
    public partial SettingsRootPage SelectedRootPage { get; set; } = SettingsRootPage.System;

    [ObservableProperty]
    public partial SettingsSubPage SelectedSubPage { get; set; } = SettingsSubPage.General;

    public bool IsSystemRoot => SelectedRootPage == SettingsRootPage.System;
    public bool IsTriggersRoot => SelectedRootPage == SettingsRootPage.Triggers;
    public bool IsAppearanceRoot => SelectedRootPage == SettingsRootPage.Appearance;
    public bool IsGeneralPage => SelectedSubPage == SettingsSubPage.General;
    public bool IsCapturePage => SelectedSubPage == SettingsSubPage.CaptureInput;
    public bool IsDevicesPage => SelectedSubPage == SettingsSubPage.Devices;
    public bool IsMenuLauncherPage => SelectedSubPage == SettingsSubPage.MenuLauncher;
    public bool IsWindowRulesPage => SelectedSubPage == SettingsSubPage.WindowRules;
    public bool IsThemePage => SelectedSubPage == SettingsSubPage.Theme;

    partial void OnSelectedRootPageChanged(SettingsRootPage value)
    {
        OnPropertyChanged(nameof(IsSystemRoot));
        OnPropertyChanged(nameof(IsTriggersRoot));
        OnPropertyChanged(nameof(IsAppearanceRoot));
    }

    partial void OnSelectedSubPageChanged(SettingsSubPage value)
    {
        OnPropertyChanged(nameof(IsGeneralPage));
        OnPropertyChanged(nameof(IsCapturePage));
        OnPropertyChanged(nameof(IsDevicesPage));
        OnPropertyChanged(nameof(IsMenuLauncherPage));
        OnPropertyChanged(nameof(IsWindowRulesPage));
        OnPropertyChanged(nameof(IsThemePage));
    }

    public ObservableCollection<MenuRuleRow> MenuRules { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MenuLauncherDisplay))]
    public partial string MenuLauncher { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MenuLauncherButton))]
    public partial bool IsCapturingMenuLauncher { get; set; }

    [ObservableProperty]
    public partial string MenuLauncherMessage { get; set; } = string.Empty;

    public string MenuLauncherDisplay => MenuLauncher.Length == 0
        ? Strings.Get("Common.None")
        : MenuLauncher;

    public string MenuLauncherButton => Strings.Get(IsCapturingMenuLauncher
        ? "Settings.HotkeyPress"
        : "Settings.HotkeySet");

    public string MenuLauncherStatus
    {
        get
        {
            if (Project is null)
            {
                return Strings.Get("Settings.MenuNoProject");
            }

            if (MenuLauncherMessage.Length > 0)
            {
                return MenuLauncherMessage;
            }

            if (MenuLauncher.Length == 0)
            {
                return Strings.Get("Settings.MenuLauncherMissing");
            }

            if (!Project.IsRunning)
            {
                return Strings.Get("Settings.MenuSystemOff");
            }

            var conflicts = ConflictBindings().ToList();
            if (conflicts.Any(binding =>
                    string.Equals(binding, MenuLauncher, StringComparison.OrdinalIgnoreCase)))
            {
                return Strings.Get("Settings.MenuLauncherConflict");
            }

            var menuProblems = Project.Triggers.MenuProblems([.. Project.Macros], MenuIconCatalog.Knows);
            if (menuProblems.Count > 0)
            {
                return menuProblems[0];
            }

            return Project.Triggers.Entries.Any(entry =>
                entry.Kind == TriggerKind.Menu && entry.IsEnabled)
                ? Strings.Get("Settings.MenuReady")
                : Strings.Get("Settings.MenuEmpty");
        }
    }

    public string MenuRuleProblem
    {
        get
        {
            if (Project is null)
            {
                return string.Empty;
            }

            if (Project.Triggers.ContextMenuPages()
                .Any(page => !page.Window.HasTarget))
            {
                return Strings.Get("Menu.Problem.RuleEmpty");
            }
            return Project.Triggers.BlockedMenuRules.Any(rule => !rule.HasTarget)
                ? Strings.Get("Menu.Problem.BlockedRuleEmpty")
                : string.Empty;
        }
    }

    public void SetPage(SettingsRootPage root, SettingsSubPage sub)
    {
        SelectedRootPage = root;
        SelectedSubPage = sub;
    }

    public void BeginMenuLauncherCapture()
    {
        MenuLauncherMessage = string.Empty;
        IsCapturingMenuLauncher = true;
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    public void StopMenuLauncherCapture()
    {
        IsCapturingMenuLauncher = false;
        MenuLauncherMessage = string.Empty;
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    public bool CaptureMenuLauncher(string binding)
    {
        if (!AllowedMenuLauncher(binding))
        {
            MenuLauncherMessage = Strings.Get("Settings.MenuLauncherInvalid");
            OnPropertyChanged(nameof(MenuLauncherStatus));
            return false;
        }

        IsCapturingMenuLauncher = false;
        MenuLauncherMessage = string.Empty;
        MenuLauncher = binding.Trim();
        if (Project is not null)
        {
            Project.Triggers.MenuLauncher = MenuLauncher;
            Project.MarkMenuChanged();
        }
        OnPropertyChanged(nameof(MenuLauncherStatus));
        return true;
    }

    public void ClearMenuLauncher()
    {
        IsCapturingMenuLauncher = false;
        MenuLauncherMessage = string.Empty;
        MenuLauncher = string.Empty;
        if (Project is not null)
        {
            Project.Triggers.MenuLauncher = string.Empty;
            Project.MarkMenuChanged();
        }
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    public void RefreshMenuRules()
    {
        MenuRules.Clear();
        if (Project is null)
        {
            return;
        }

        var defaults = Project.Triggers.DefaultContextMenuPages();
        MenuRules.Add(new MenuRuleRow("default",
            Strings.Get("Settings.MenuRuleDefault"),
            WhaleGenie.Core.Devices.WindowRule.Empty, true, defaults.Count));
        foreach (var group in Project.Triggers.ContextRuleGroups())
        {
            MenuRules.Add(new MenuRuleRow(group.Key,
                $"{group.Rule.MatchValue} · {Strings.Get($"WindowRule.Short.{group.Rule.How}", group.Rule.How)}",
                group.Rule, false, group.Pages.Count));
        }
        foreach (var rule in Project.Triggers.BlockedMenuRules)
        {
            MenuRules.Add(new MenuRuleRow(
                ProjectTriggers.GroupKey(rule),
                $"{rule.MatchValue} · {Strings.Get($"WindowRule.Short.{rule.How}", rule.How)}",
                rule,
                false,
                0,
                true));
        }
        OnPropertyChanged(nameof(MenuRuleProblem));
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    public void AddMenuRule(WhaleGenie.Core.Devices.WindowRule rule)
    {
        if (Project is null || !rule.HasTarget)
        {
            return;
        }

        Project.Triggers.AddContextMenuPage(rule);
        Project.MarkMenuChanged();
        RefreshMenuRules();
    }

    public void UpdateMenuRule(MenuRuleRow row, WhaleGenie.Core.Devices.WindowRule rule)
    {
        if (Project is null || row.IsDefault || !rule.HasTarget)
        {
            return;
        }

        if (row.IsBlocked)
        {
            var blocked = Project.Triggers.BlockedMenuRules
                .Select((candidate, index) => (candidate, index))
                .FirstOrDefault(item => ProjectTriggers.GroupKey(item.candidate) == row.GroupKey);
            if (blocked.candidate is null)
            {
                return;
            }
            Project.Triggers.BlockedMenuRules[blocked.index] = rule;
            Project.MarkMenuChanged();
            RefreshMenuRules();
            return;
        }

        var pages = Project.Triggers.ContextPagesFor(row.Rule).ToList();
        if (pages.Count == 0)
        {
            return;
        }

        foreach (var page in pages)
        {
            var index = Project.Triggers.MenuPages.IndexOf(page);
            Project.Triggers.MenuPages[index] = page with { Window = rule };
        }
        Project.MarkMenuChanged();
        RefreshMenuRules();
    }

    public void RemoveMenuRule(MenuRuleRow row)
    {
        if (Project is null || row.IsDefault)
        {
            return;
        }

        if (row.IsBlocked)
        {
            var blocked = Project.Triggers.BlockedMenuRules.FirstOrDefault(rule =>
                ProjectTriggers.GroupKey(rule) == row.GroupKey);
            if (blocked is not null)
            {
                Project.Triggers.BlockedMenuRules.Remove(blocked);
            }
        }
        else
        {
            Project.Triggers.RemoveContextRule(row.GroupKey);
        }
        Project.MarkMenuChanged();
        RefreshMenuRules();
    }

    public void AddMenuPage(MenuRuleRow row)
    {
        if (Project is null || row.IsBlocked)
        {
            return;
        }

        if (row.IsDefault)
        {
            if (Project.Triggers.DefaultContextMenuPages().Count
                < ProjectTriggers.MaxCommonMenuPages)
            {
                Project.Triggers.AddDefaultContextPage();
            }
        }
        else if (Project.Triggers.ContextPagesFor(row.Rule).Count
                 < ProjectTriggers.MaxCommonMenuPages)
        {
            Project.Triggers.AddContextMenuPage(row.Rule);
        }
        Project.MarkMenuChanged();
        RefreshMenuRules();
    }

    public void MoveMenuRule(MenuRuleRow row, int step)
    {
        if (Project is null || row.IsDefault || row.IsBlocked)
        {
            return;
        }

        var groups = Project.Triggers.ContextRuleGroups().ToList();
        var currentGroup = groups.FindIndex(group => group.Key == row.GroupKey);
        if (currentGroup < 0)
        {
            return;
        }
        var target = groups[Math.Clamp(currentGroup + step, 0, groups.Count - 1)];
        Project.Triggers.MoveContextRule(row.GroupKey, target.Key);
        Project.MarkMenuChanged();
        RefreshMenuRules();
    }

    public void MoveMenuRuleTo(MenuRuleRow row, MenuRuleRow target)
    {
        if (Project is null || row.IsDefault || target.IsDefault
            || row.IsBlocked || target.IsBlocked
            || row.GroupKey == target.GroupKey)
        {
            return;
        }
        Project.Triggers.MoveContextRule(row.GroupKey, target.GroupKey);
        Project.MarkMenuChanged();
        RefreshMenuRules();
    }

    private IEnumerable<string> ConflictBindings()
    {
        if (Project is null)
        {
            yield break;
        }

        if (SystemHotkey.Length > 0)
        {
            yield return SystemHotkey;
        }
        foreach (var entry in Project.Triggers.Entries.Where(entry =>
                     entry.IsEnabled && entry.Kind == TriggerKind.Key
                     && entry.BindKey.Trim().Length > 0))
        {
            yield return entry.BindKey.Trim();
        }
        foreach (var layout in Project.Triggers.InterfaceLayouts.Where(layout =>
                     layout.ToggleHotkey.Trim().Length > 0))
        {
            yield return layout.ToggleHotkey.Trim();
        }
    }

    private static bool AllowedMenuLauncher(string binding)
        => ProjectTriggers.IsAllowedMenuLauncher(binding);

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(Language.English, "English"),
        new(Language.Chinese, "中文"),
    ];

    /// <summary>The three theme choices: two built-in palettes and the custom one.</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemeMode.Dark, "Theme.Mode.Dark"),
        new(ThemeMode.Light, "Theme.Mode.Light"),
        new(ThemeMode.Custom, "Theme.Mode.Custom"),
    ];

    private bool _themeReady;

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; } = null!;

    /// <summary>Whether the custom palette button belongs in the settings window.</summary>
    public bool IsCustomTheme => SelectedTheme?.Mode is ThemeMode.Custom;

    /// <summary>
    /// The ways a window's picture can be taken, in the order they are offered: automatic first, then
    /// the two the machine can be told to use on a window.
    /// </summary>
    public IReadOnlyList<CaptureOption> WindowWays { get; } =
    [
        new(CaptureWay.Auto, "Settings.Capture.auto"),
        new(CaptureWay.PrintWindow, "Settings.Capture.printWindow"),
        new(CaptureWay.GraphicsCapture, "Settings.Capture.graphicsCapture"),
    ];

    /// <summary>
    /// The ways the desktop's picture can be taken, in the order they are offered: automatic first,
    /// then the ways that read the display, cheapest and most reliable last.
    /// </summary>
    public IReadOnlyList<CaptureOption> ScreenWays { get; } =
    [
        new(CaptureWay.Auto, "Settings.Capture.auto"),
        new(CaptureWay.DesktopDuplication, "Settings.Capture.desktopDuplication"),
        new(CaptureWay.Gdi, "Settings.Capture.gdi"),
        new(CaptureWay.GraphicsCapture, "Settings.Capture.graphicsCapture"),
    ];

    /// <summary>How this machine reads a picture of a window.</summary>
    [ObservableProperty]
    public partial CaptureOption SelectedWindowWay { get; set; }

    /// <summary>How this machine reads a picture of the desktop.</summary>
    [ObservableProperty]
    public partial CaptureOption SelectedScreenWay { get; set; }

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    /// <summary>
    /// Whether the text recognition models are loaded in the background as the program starts.
    /// </summary>
    [ObservableProperty]
    public partial bool PreloadOcr { get; set; }

    /// <summary>
    /// What this machine has of the two pieces driver-level input needs. Read once when the window
    /// opens: installing a driver is not something that happens while the window sits there.
    /// </summary>
    public DriverInputState DriverState { get; }

    /// <summary>Whether both pieces are in place, which is what the panel's colour says.</summary>
    public bool DriverReady { get; }

    /// <summary>The panel's sentence about this machine, in the chosen language.</summary>
    public string DriverStatus => Strings.Get(DriverState switch
    {
        DriverInputState.DriverMissing => "Settings.DriverMissing",
        DriverInputState.LibraryMissing => "Settings.DriverLibraryMissing",
        _ => "Settings.DriverReady",
    });

    /// <summary>
    /// Whether the virtual keyboard and mouse are put on the machine as the program starts. The
    /// switch is about the next start rather than this one: the devices go on while the program is
    /// starting up, and flipping it here would not put them on any sooner.
    /// </summary>
    [ObservableProperty]
    public partial bool AttachDriverAtStart { get; set; }

    /// <summary>
    /// Whether this copy of the program was started as an administrator, which decides whether a
    /// hotkey still reaches it while a window of a higher privilege is in front.
    /// </summary>
    public bool IsElevated { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemHotkeyDisplay))]
    public partial string SystemHotkey { get; set; } = LocalSettings.LoadSystemHotkey();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemHotkeyButton))]
    public partial bool IsCapturingSystemHotkey { get; set; }

    [ObservableProperty]
    public partial string SystemHotkeyProblem { get; set; } = string.Empty;

    public bool IsCapturingHotkey => IsCapturingSystemHotkey || IsCapturingMenuLauncher;

    public string SystemHotkeyDisplay => SystemHotkey.Length == 0
        ? Strings.Get("Common.None")
        : SystemHotkey;

    public string SystemHotkeyButton => Strings.Get(
        IsCapturingSystemHotkey ? "Settings.HotkeyPress" : "Settings.HotkeySet");

    partial void OnSystemHotkeyChanged(string value)
    {
        LocalSettings.StoreSystemHotkey(value);
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    partial void OnIsCapturingSystemHotkeyChanged(bool value)
        => OnPropertyChanged(nameof(IsCapturingHotkey));

    partial void OnIsCapturingMenuLauncherChanged(bool value)
        => OnPropertyChanged(nameof(IsCapturingHotkey));

    public bool CaptureSystemHotkey(string binding)
    {
        var text = binding.Trim();
        if (text.Length > 0 && !HotkeyBinding.IsSupported(text))
        {
            IsCapturingSystemHotkey = false;
            SystemHotkeyProblem = Strings.Get("Settings.HotkeyUnsupported");
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

    /// <summary>The line above the button that restarts the program with that token.</summary>
    public string AdminStatus
        => Strings.Get(IsElevated ? "Settings.AdminYes" : "Settings.AdminNo");

    partial void OnSelectedLanguageChanged(LanguageOption value)
        => Strings.Current.Language = value.Language;

    /// <summary>
    /// Applies a theme as soon as the picker changes. Choosing Custom starts from the palette in
    /// use, unless one was already saved; the dark and light choices leave a saved custom palette
    /// untouched for next time.
    /// </summary>
    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        OnPropertyChanged(nameof(IsCustomTheme));
        if (!_themeReady)
        {
            return;
        }

        var current = ThemeController.Load();
        var next = value.Mode is ThemeMode.Custom
            ? ThemeController.StartCustom(current)
            : current with { Mode = value.Mode };
        ThemeController.Apply(next);
        ThemeController.Save(next);
    }

    /// <summary>The same, for the choice that holds the text recognition models in memory.</summary>
    partial void OnPreloadOcrChanged(bool value)
    {
        if (value != LocalSettings.LoadPreloadOcr())
        {
            LocalSettings.StorePreloadOcr(value);
        }
    }

    /// <summary>
    /// The same, for the choice that puts the virtual keyboard and mouse on the machine at startup.
    /// The engine hears about it when the program starts, which is also when it acts on it.
    /// </summary>
    partial void OnAttachDriverAtStartChanged(bool value)
    {
        if (value != LocalSettings.LoadDriverAttachAtStart())
        {
            LocalSettings.StoreDriverAttachAtStart(value);
        }
    }

    /// <summary>
    /// Gives the engine the new way, but only when it really changed: opening the window reads what
    /// the engine is using into this property, and that must not count as the user asking for
    /// something.
    /// </summary>
    partial void OnSelectedWindowWayChanged(CaptureOption value)
    {
        if (value.Way != CaptureChoice.Current.Window)
        {
            CaptureSetup.StoreWindow(value.Way);
        }
    }

    /// <summary>The same for the way the desktop is read.</summary>
    partial void OnSelectedScreenWayChanged(CaptureOption value)
    {
        if (value.Way != CaptureChoice.Current.Screen)
        {
            CaptureSetup.StoreScreen(value.Way);
        }
    }
}
