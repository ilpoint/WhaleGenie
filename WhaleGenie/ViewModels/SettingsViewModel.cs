using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

/// <summary>
/// One entry of a sound picker, named the way the same sound is named beside a macro's own sound
/// step: the four moments a sound is offered for all choose from this one list.
/// </summary>
public sealed record SoundOption(SoundKind Kind, string Key)
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

/// <summary>
/// One place something is kept: what it is, what lands there, and where it is. The folder is the one
/// a button opens, which is the library's own folder rather than the file inside it.
/// </summary>
public sealed record StorageRow(string Name, string What, string Where, string Folder);

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
        LoadLastProjectAtStart = LocalSettings.LoadLastProjectAtStart();
        SystemAtStart = LocalSettings.LoadSystemAtStart();
        EnabledFirst = LocalSettings.LoadEnabledFirst();
        PreloadOcr = LocalSettings.LoadPreloadOcr();
        InputRate = LocalSettings.LoadInputRate();
        ExclusiveInput = LocalSettings.LoadExclusiveInput();
        IgnoreOwnInput = LocalSettings.LoadIgnoreOwnInput();
        LogCount = LocalSettings.LoadLogCount();
        Sounds = LocalSettings.LoadSounds();
        SystemOnSound = SoundChoices.First(option =>
            option.Kind == LocalSettings.LoadSound(UiSound.SystemOn));
        SystemOffSound = SoundChoices.First(option =>
            option.Kind == LocalSettings.LoadSound(UiSound.SystemOff));
        MacroOnSound = SoundChoices.First(option =>
            option.Kind == LocalSettings.LoadSound(UiSound.MacroOn));
        MacroOffSound = SoundChoices.First(option =>
            option.Kind == LocalSettings.LoadSound(UiSound.MacroOff));
        AttachDriverAtStart = LocalSettings.LoadDriverAttachAtStart();
        SelectedWindowWay = WindowWays.First(option => option.Way == CaptureChoice.Current.Window);
        SelectedScreenWay = ScreenWays.First(option => option.Way == CaptureChoice.Current.Screen);
        IsElevated = elevated ?? ProcessRights.IsElevated;
        RunAsAdmin = LocalSettings.LoadRunAsAdmin();

        DriverState = driverInput();
        DriverReady = DriverState == DriverInputState.Ready;
        MenuLauncher = LocalSettings.LoadMenuLauncher();
        RefreshMenuRules();
    }

    /// <summary>Re-reads the capture choices, which show their own language when read.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(WindowWays));
        OnPropertyChanged(nameof(ScreenWays));
        OnPropertyChanged(nameof(SoundChoices));
        OnPropertyChanged(nameof(SystemOnSound));
        OnPropertyChanged(nameof(SystemOffSound));
        OnPropertyChanged(nameof(MacroOnSound));
        OnPropertyChanged(nameof(MacroOffSound));
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

    /// <summary>
    /// The same choice, as the number a tab strip speaks in. The three pages are an enum because
    /// the code switches on them; a tab control only knows positions, so this is the one place the
    /// two are converted. The setter is how a click on a tab comes back.
    /// </summary>
    public int RootIndex
    {
        get => (int)SelectedRootPage;
        set
        {
            if (value >= 0 && value <= (int)SettingsRootPage.Appearance)
            {
                SelectedRootPage = (SettingsRootPage)value;
            }
        }
    }
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
        OnPropertyChanged(nameof(RootIndex));
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

    /// <summary>
    /// Where everything the program keeps for the person actually is. It is shown rather than only
    /// documented: "where did my macros.assets go" is a question this window should answer without
    /// anybody opening a manual, and an update that moves these again gets one place to say so.
    /// </summary>
    public IReadOnlyList<StorageRow> StorageRows { get; } =
    [
        new(Strings.Get("Settings.StorageState"), Strings.Get("Settings.StorageStateWhat"),
            AppPaths.State, AppPaths.State),
        new(Strings.Get("Settings.StorageProjects"), Strings.Get("Settings.StorageProjectsWhat"),
            AppPaths.Projects, AppPaths.Projects),
        new(Strings.Get("Settings.StorageImages"), Strings.Get("Settings.StorageImagesWhat"),
            Storage.ImageAssets.DefaultFolder(), Storage.ImageAssets.DefaultFolder()),
        new(Strings.Get("Settings.StorageLibrary"), Strings.Get("Settings.StorageLibraryWhat"),
            AppPaths.PublicMacroLibrary, Path.GetDirectoryName(AppPaths.PublicMacroLibrary) ?? AppPaths.Root),
    ];

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
            if (MenuLauncherMessage.Length > 0)
            {
                return MenuLauncherMessage;
            }

            if (MenuLauncher.Length == 0)
            {
                return Strings.Get("Settings.MenuLauncherMissing");
            }

            if (Project is { IsRunning: false })
            {
                return Strings.Get("Settings.MenuSystemOff");
            }

            var conflicts = ConflictBindings().ToList();
            if (conflicts.Any(binding =>
                    string.Equals(binding, MenuLauncher, StringComparison.OrdinalIgnoreCase)))
            {
                return Strings.Get("Settings.MenuLauncherConflict");
            }

            var menuProblems = Project?.Triggers.MenuProblems([.. Project.Macros],
                MenuIconCatalog.Knows) ?? [];
            if (menuProblems.Count > 0)
            {
                return menuProblems[0];
            }

            return Project is not null && Project.Triggers.Entries.Any(entry =>
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
        LocalSettings.StoreMenuLauncher(MenuLauncher);
        OnPropertyChanged(nameof(MenuLauncherStatus));
        return true;
    }

    public void ClearMenuLauncher()
    {
        IsCapturingMenuLauncher = false;
        MenuLauncherMessage = string.Empty;
        MenuLauncher = string.Empty;
        LocalSettings.StoreMenuLauncher(string.Empty);
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
        if (SystemHotkey.Length > 0)
        {
            yield return SystemHotkey;
        }

        if (Project is null)
        {
            yield break;
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
    /// Whether a start opens the project that was open when the program was last closed. Off starts
    /// empty, which is what keeping several projects apart from one another asks for.
    /// </summary>
    [ObservableProperty]
    public partial bool LoadLastProjectAtStart { get; set; }

    /// <summary>
    /// Whether triggers listen as soon as the program starts. Off leaves the system disabled until it
    /// is turned on, which is the quieter thing to do on a shared machine.
    /// </summary>
    [ObservableProperty]
    public partial bool SystemAtStart { get; set; }

    /// <summary>
    /// Whether the macro list puts enabled macros above disabled ones. Off keeps the order the
    /// project was made in.
    /// </summary>
    [ObservableProperty]
    public partial bool EnabledFirst { get; set; }

    /// <summary>
    /// How many keyboard or mouse events may leave the program in a second, 0 for no limit. Held as
    /// a number field's double because that is what the control speaks; it is a count, so what is
    /// stored and applied is the rounded value.
    /// </summary>
    [ObservableProperty]
    public partial double InputRate { get; set; }

    /// <summary>
    /// Takes effect at once: a macro that is about to type too fast should be held back from the
    /// next event, not from the next run. Zero is a real choice here and not an empty field.
    /// </summary>
    partial void OnInputRateChanged(double value)
    {
        var rate = Math.Clamp((int)Math.Round(value), 0, InputLimit.Most);
        InputLimit.PerSecond = rate;
        if (rate != LocalSettings.LoadInputRate())
        {
            LocalSettings.StoreInputRate(rate);
        }
    }

    /// <summary>
    /// Whether keyboard and mouse input is kept to one macro at a time. It takes effect at the next
    /// run: a macro already running was handed its answer when it started.
    /// </summary>
    [ObservableProperty]
    public partial bool ExclusiveInput { get; set; }

    /// <summary>
    /// How many failure and MCP logs are kept, counted by modification time. Held as a number
    /// field's double because that is what the control speaks; it is a count, so the rounded value
    /// is what is stored.
    /// </summary>
    [ObservableProperty]
    public partial double LogCount { get; set; }

    /// <summary>Stored at once; what it does happens at the next start, which is when the folder is cleaned.</summary>
    partial void OnLogCountChanged(double value)
    {
        var count = Math.Clamp((int)Math.Round(value), LogRetention.Fewest, LogRetention.Most);
        if (count != LocalSettings.LoadLogCount())
        {
            LocalSettings.StoreLogCount(count);
        }
    }

    /// <summary>
    /// Whether what this program sends counts as its own rather than as the person's. It reaches the
    /// triggers at once — they ask this on every key — while a recording already open keeps the value
    /// it started with, because that one can be changed for that recording alone.
    /// </summary>
    [ObservableProperty]
    public partial bool IgnoreOwnInput { get; set; }

    /// <summary>
    /// Whether the interface says out loud that the system switch or a macro changed state. The four
    /// sounds below are only heard while this is on, and keeping quiet also means a machine in a
    /// shared room stays quiet.
    /// </summary>
    [ObservableProperty]
    public partial bool Sounds { get; set; }

    /// <summary>The five sounds a moment can be given, the same list the sound step offers.</summary>
    public IReadOnlyList<SoundOption> SoundChoices { get; } =
    [
        new(SoundKind.Default, "Settings.SoundOption.Default"),
        new(SoundKind.Information, "Settings.SoundOption.Information"),
        new(SoundKind.Warning, "Settings.SoundOption.Warning"),
        new(SoundKind.Error, "Settings.SoundOption.Error"),
        new(SoundKind.Question, "Settings.SoundOption.Question"),
    ];

    /// <summary>The sound the whole system going quiet plays.</summary>
    [ObservableProperty]
    public partial SoundOption SystemOffSound { get; set; }

    /// <summary>The sound the whole system listening again plays.</summary>
    [ObservableProperty]
    public partial SoundOption SystemOnSound { get; set; }

    /// <summary>The sound a macro being armed plays.</summary>
    [ObservableProperty]
    public partial SoundOption MacroOnSound { get; set; }

    /// <summary>The sound a macro being disarmed plays.</summary>
    [ObservableProperty]
    public partial SoundOption MacroOffSound { get; set; }

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
    /// Whether every start asks Windows for the administrator token. It is about the next start:
    /// the rights this copy has were decided when it was launched and cannot be changed now, which
    /// is what the restart button beside it is for.
    /// </summary>
    [ObservableProperty]
    public partial bool RunAsAdmin { get; set; }

    /// <summary>
    /// Whether this copy of the program was started as an administrator, which decides whether a
    /// hotkey still reaches it while a window of a higher privilege is in front.
    /// </summary>
    public bool IsElevated { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemHotkeyDisplay))]
    public partial string SystemHotkey { get; set; } = LocalSettings.LoadSystemHotkey();

    /// <summary>
    /// The shortcut that ends a recording. It is what the recording panel's hint says, so changing
    /// it here changes the words there as well.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordStopDisplay))]
    public partial string RecordStopKey { get; set; } = LocalSettings.LoadRecordStopKey();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordStopButton))]
    public partial bool IsCapturingRecordStop { get; set; }

    [ObservableProperty]
    public partial string RecordStopProblem { get; set; } = string.Empty;

    public string RecordStopDisplay => RecordStopKey.Length == 0
        ? Strings.Get("Common.None")
        : RecordStopKey;

    public string RecordStopButton => Strings.Get(
        IsCapturingRecordStop ? "Settings.HotkeyPress" : "Settings.HotkeySet");

    /// <summary>
    /// Takes the shortcut just pressed. An empty one is refused rather than stored: a recording
    /// whose only way out is the mouse keeps writing while the pointer goes looking for the button.
    /// </summary>
    public bool CaptureRecordStop(string binding)
    {
        var text = binding.Trim();
        if (text.Length == 0 || !HotkeyBinding.IsSupported(text))
        {
            IsCapturingRecordStop = false;
            RecordStopProblem = Strings.Get("Settings.HotkeyUnsupported");
            return false;
        }

        IsCapturingRecordStop = false;
        RecordStopProblem = string.Empty;
        RecordStopKey = text;
        LocalSettings.StoreRecordStopKey(text);
        return true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemHotkeyButton))]
    public partial bool IsCapturingSystemHotkey { get; set; }

    [ObservableProperty]
    public partial string SystemHotkeyProblem { get; set; } = string.Empty;

    public bool IsCapturingHotkey => IsCapturingSystemHotkey || IsCapturingMenuLauncher
        || IsCapturingRecordStop;

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

    partial void OnIsCapturingRecordStopChanged(bool value)
        => OnPropertyChanged(nameof(IsCapturingHotkey));

    /// <summary>Stores whether the next start asks for the administrator token.</summary>
    partial void OnRunAsAdminChanged(bool value)
    {
        if (value != LocalSettings.LoadRunAsAdmin())
        {
            LocalSettings.StoreRunAsAdmin(value);
        }
    }

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
    /// Applies a theme as soon as the picker changes. Choosing Custom starts from the accent in use,
    /// unless one was already saved; the dark and light choices leave a saved accent untouched for
    /// next time.
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
            : current.WithMode(value.Mode);
        ThemeController.Apply(next);
        ThemeController.Save(next);
    }

    /// <summary>Stores whether a start opens the project that was open last time.</summary>
    partial void OnLoadLastProjectAtStartChanged(bool value)
    {
        if (value != LocalSettings.LoadLastProjectAtStart())
        {
            LocalSettings.StoreLastProjectAtStart(value);
        }
    }

    /// <summary>Stores whether triggers listen as soon as the program starts.</summary>
    partial void OnSystemAtStartChanged(bool value)
    {
        if (value != LocalSettings.LoadSystemAtStart())
        {
            LocalSettings.StoreSystemAtStart(value);
        }
    }

    /// <summary>Stores whether enabled macros are listed first.</summary>
    partial void OnEnabledFirstChanged(bool value)
    {
        if (value != LocalSettings.LoadEnabledFirst())
        {
            LocalSettings.StoreEnabledFirst(value);
        }
    }

    /// <summary>Stores whether input is kept to one macro at a time.</summary>
    partial void OnExclusiveInputChanged(bool value)
    {
        if (value != LocalSettings.LoadExclusiveInput())
        {
            LocalSettings.StoreExclusiveInput(value);
        }
    }

    /// <summary>
    /// Tells the engine first: the hook asks this on every key press, so a switch that only saved the
    /// answer would seem to do nothing until the next start.
    /// </summary>
    partial void OnIgnoreOwnInputChanged(bool value)
    {
        WhaleGenieInputGate.IgnoreOwnInput = value;
        if (value != LocalSettings.LoadIgnoreOwnInput())
        {
            LocalSettings.StoreIgnoreOwnInput(value);
        }
    }

    /// <summary>Stores whether the interface plays its state-change sounds.</summary>
    partial void OnSoundsChanged(bool value)
    {
        if (value != LocalSettings.LoadSounds())
        {
            LocalSettings.StoreSounds(value);
        }
    }

    partial void OnSystemOnSoundChanged(SoundOption value)
        => KeepSound(UiSound.SystemOn, value);

    partial void OnSystemOffSoundChanged(SoundOption value)
        => KeepSound(UiSound.SystemOff, value);

    partial void OnMacroOnSoundChanged(SoundOption value)
        => KeepSound(UiSound.MacroOn, value);

    partial void OnMacroOffSoundChanged(SoundOption value)
        => KeepSound(UiSound.MacroOff, value);

    /// <summary>
    /// Writes one choice back, but only when it really changed: opening the window reads the stored
    /// sound into this property, and that must not count as the person asking for something.
    /// </summary>
    private static void KeepSound(UiSound which, SoundOption value)
    {
        if (value is not null && value.Kind != LocalSettings.LoadSound(which))
        {
            LocalSettings.StoreSound(which, value.Kind);
        }
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
