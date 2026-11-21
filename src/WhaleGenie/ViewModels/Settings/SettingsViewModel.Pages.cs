using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// The values the settings bridge reads: the project, the rows the page shows, and the two things
// every page shares (the language and the menu launcher).
public partial class SettingsViewModel
{
    /// <summary>Re-reads the choices whose own text follows the language.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(WindowWays));
        OnPropertyChanged(nameof(ScreenWays));
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

    public ObservableCollection<MenuRuleRow> MenuRules { get; } = [];

    /// <summary>
    /// Where everything the program keeps for the person actually is. It is shown rather than only
    /// documented: "where did my macros.assets go" is a question the page answers without anybody
    /// opening a manual, and an update that moves these again gets one place to say so.
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
