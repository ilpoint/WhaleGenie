using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// The pages of the window, what a change of page does, and the storage rows shown beside them.
public partial class SettingsViewModel
{
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
        OnPropertyChanged(nameof(FrameRateNote));
        OnPropertyChanged(nameof(WheelLauncherStatus));
        OnPropertyChanged(nameof(SelectedWheelLauncher));
        OnPropertyChanged(nameof(WheelProblem));
        OnPropertyChanged(nameof(WheelRepeatButton));
        RefreshWheelScenes();
    }

    public MainViewModel? Project { get; }

    public string? PackagePath { get; }

    public bool HasProject => Project is not null;

    /// <summary>
    /// The page the window is showing. It is the only place a page is named: the group at the top of
    /// the window is derived from it rather than stored beside it, so the two cannot disagree.
    /// </summary>
    [ObservableProperty]
    public partial SettingsSubPage SelectedPage { get; set; } = SettingsSubPage.General;

    public bool IsGeneralPage => SelectedPage == SettingsSubPage.General;
    public bool IsHotkeysPage => SelectedPage == SettingsSubPage.Hotkeys;
    public bool IsScreenPage => SelectedPage == SettingsSubPage.Screen;
    public bool IsInputPage => SelectedPage == SettingsSubPage.Input;
    public bool IsDevicesPage => SelectedPage == SettingsSubPage.Devices;
    public bool IsMenuLauncherPage => SelectedPage == SettingsSubPage.MenuLauncher;
    public bool IsWindowRulesPage => SelectedPage == SettingsSubPage.WindowRules;
    public bool IsWheelMenuPage => SelectedPage == SettingsSubPage.WheelMenu;
    public bool IsWheelActionsPage => SelectedPage == SettingsSubPage.WheelActions;
    public bool IsThemePage => SelectedPage == SettingsSubPage.Theme;

    /// <summary>The group the open page belongs to; the three tabs at the top of the window read it.</summary>
    public SettingsRoot SelectedRoot => RootOf(SelectedPage);

    public bool IsSystemRoot => SelectedRoot == SettingsRoot.System;
    public bool IsTriggersRoot => SelectedRoot == SettingsRoot.Triggers;
    public bool IsAppearanceRoot => SelectedRoot == SettingsRoot.Appearance;

    /// <summary>Which group a page is listed under.</summary>
    public static SettingsRoot RootOf(SettingsSubPage page) => page switch
    {
        SettingsSubPage.MenuLauncher or SettingsSubPage.WindowRules
            or SettingsSubPage.WheelMenu or SettingsSubPage.WheelActions
            => SettingsRoot.Triggers,
        SettingsSubPage.Theme => SettingsRoot.Appearance,
        _ => SettingsRoot.System,
    };

    /// <summary>The page a group opens on, which is also the one its tab lands on.</summary>
    public static SettingsSubPage FirstPageOf(SettingsRoot root) => root switch
    {
        SettingsRoot.Triggers => SettingsSubPage.MenuLauncher,
        SettingsRoot.Appearance => SettingsSubPage.Theme,
        _ => SettingsSubPage.General,
    };

    /// <summary>Opens a group at its first page; a group has no page of its own to remember.</summary>
    public void SetRoot(SettingsRoot root) => SelectedPage = FirstPageOf(root);

    partial void OnSelectedPageChanged(SettingsSubPage value)
    {
        OnPropertyChanged(nameof(IsGeneralPage));
        OnPropertyChanged(nameof(IsHotkeysPage));
        OnPropertyChanged(nameof(IsScreenPage));
        OnPropertyChanged(nameof(IsInputPage));
        OnPropertyChanged(nameof(IsDevicesPage));
        OnPropertyChanged(nameof(IsMenuLauncherPage));
        OnPropertyChanged(nameof(IsWindowRulesPage));
        OnPropertyChanged(nameof(IsWheelMenuPage));
        OnPropertyChanged(nameof(IsWheelActionsPage));
        OnPropertyChanged(nameof(IsThemePage));
        OnPropertyChanged(nameof(SelectedRoot));
        OnPropertyChanged(nameof(IsSystemRoot));
        OnPropertyChanged(nameof(IsTriggersRoot));
        OnPropertyChanged(nameof(IsAppearanceRoot));
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
    [NotifyPropertyChangedFor(nameof(MenuLauncherMiddle))]
    [NotifyPropertyChangedFor(nameof(MenuLauncherCustom))]
    [NotifyPropertyChangedFor(nameof(WheelLauncherStatus))]
    public partial string MenuLauncher { get; set; } = string.Empty;
}
