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
    private readonly Action? _markMenuChanged;
    private readonly SystemState? _system;
    private readonly IIconCatalog? _icons;
    private readonly AppSettings _settings;

    /// <summary>
    /// The same, over whatever stands behind driver-level input: the program's own, or one a check
    /// hands in so that a machine which is not this one can be described.
    /// </summary>
    internal SettingsViewModel(IDriverInput driver, bool? elevated = null,
        ProjectWorkspace? project = null, string? packagePath = null,
        Action? markMenuChanged = null, SystemState? system = null,
        IIconCatalog? icons = null, AppSettings? settings = null)
        : this(() => driver.State, elevated, project, packagePath, markMenuChanged, system, icons,
            settings)
    {
    }

    /// <summary>
    /// The same, with what this machine has of driver-level input handed in, so a check can describe
    /// a machine that is not this one instead of asking the registry — and with what rights the
    /// program has, which a check cannot give itself either.
    /// </summary>
    internal SettingsViewModel(Func<DriverInputState> driverInput, bool? elevated = null,
        ProjectWorkspace? project = null, string? packagePath = null,
        Action? markMenuChanged = null, SystemState? system = null,
        IIconCatalog? icons = null, AppSettings? settings = null)
    {
        Project = project;
        _markMenuChanged = markMenuChanged;
        _system = system;
        _icons = icons;
        _settings = settings ?? AppSettings.Shared;
        PackagePath = packagePath;
        SelectedLanguage = Languages.First(option => option.Language == Strings.Current.Language);
        LoadLastProjectAtStart = _settings.LoadLastProjectAtStart();
        AutoSaveProject = _settings.LoadAutoSaveProject();
        SystemAtStart = _settings.LoadSystemAtStart();
        EnabledFirst = _settings.LoadEnabledFirst();
        PreloadOcr = _settings.LoadPreloadOcr();
        InputRate = _settings.LoadInputRate();
        FrameRateFollow = _settings.LoadFrameRateFollow();
        FrameRate = FrameRateFollow
            ? FrameSetup.FastestRate
            : Math.Clamp(_settings.LoadFrameRate(), 1, FrameSetup.FastestRate);
        FrameCache = _settings.LoadFrameCache();
        FrameCacheAge = Math.Clamp(_settings.LoadFrameCacheAge(), 0, AppSettings.FrameCacheOldest);
        _frameReady = true;
        IgnoreOwnInput = _settings.LoadIgnoreOwnInput();
        SoundSystem = _settings.LoadSoundSystem();
        SoundMacro = _settings.LoadSoundMacro();
        LogCount = _settings.LoadLogCount();
        AttachDriverAtStart = _settings.LoadDriverAttachAtStart();
        SelectedWindowWay = WindowWays.First(option => option.Way == CaptureChoice.Current.Window);
        SelectedScreenWay = ScreenWays.First(option => option.Way == CaptureChoice.Current.Screen);
        IsElevated = elevated ?? ProcessRights.IsElevated;
        RunAsAdmin = _settings.LoadRunAsAdmin();

        DriverState = driverInput();
        DriverReady = DriverState == DriverInputState.Ready;
        MenuLauncher = _settings.LoadMenuLauncher();
        RememberMenuPin = _settings.LoadRememberMenuPin();
        RefreshMenuRules();
        ReadWheel();
    }
}
