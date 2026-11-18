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
        FrameRateFollow = LocalSettings.LoadFrameRateFollow();
        FrameRate = FrameRateFollow
            ? FrameSetup.FastestRate
            : Math.Clamp(LocalSettings.LoadFrameRate(), 1, FrameSetup.FastestRate);
        FrameCache = LocalSettings.LoadFrameCache();
        FrameCacheAge = Math.Clamp(LocalSettings.LoadFrameCacheAge(), 0, LocalSettings.FrameCacheOldest);
        _frameReady = true;
        IgnoreOwnInput = LocalSettings.LoadIgnoreOwnInput();
        SoundSystem = LocalSettings.LoadSoundSystem();
        SoundMacro = LocalSettings.LoadSoundMacro();
        LogCount = LocalSettings.LoadLogCount();
        AttachDriverAtStart = LocalSettings.LoadDriverAttachAtStart();
        SelectedWindowWay = WindowWays.First(option => option.Way == CaptureChoice.Current.Window);
        SelectedScreenWay = ScreenWays.First(option => option.Way == CaptureChoice.Current.Screen);
        IsElevated = elevated ?? ProcessRights.IsElevated;
        RunAsAdmin = LocalSettings.LoadRunAsAdmin();

        DriverState = driverInput();
        DriverReady = DriverState == DriverInputState.Ready;
        MenuLauncher = LocalSettings.LoadMenuLauncher();
        RefreshMenuRules();
        ReadWheel();
    }
}
