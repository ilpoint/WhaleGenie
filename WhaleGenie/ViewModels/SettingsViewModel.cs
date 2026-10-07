using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>One entry of the language picker, named in its own language.</summary>
public sealed record LanguageOption(Language Language, string Display)
{
    public override string ToString() => Display;
}

/// <summary>Backs the settings window. Changing the language applies immediately.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel()
        : this(DriverInput.Check, DriverInput.IsServerAnswering, ProcessRights.IsElevated)
    {
    }

    /// <summary>
    /// The same, with the answers about driver-level input handed in, so a check can describe a
    /// machine that is not this one instead of asking the registry and the network — and with what
    /// rights the program has, which a check cannot give itself either.
    /// </summary>
    internal SettingsViewModel(Func<DriverInputState> driverInput, Func<bool> serverAnswering,
        bool? elevated = null)
    {
        SelectedLanguage = Languages.First(option => option.Language == Strings.Current.Language);
        FailureScreenshot = LocalSettings.LoadFailureScreenshot();
        ViiperPath = LocalSettings.LoadViiperPath();
        IsElevated = elevated ?? ProcessRights.IsElevated;

        DriverState = driverInput();
        DriverReady = DriverState == DriverInputState.Ready;
        DriverStatus = Strings.Get(DriverState switch
        {
            DriverInputState.DriverMissing => "Settings.DriverMissing",
            DriverInputState.ServerMissing => "Settings.DriverServerMissing",
            _ => "Settings.DriverReady",
        });

        ServerAnswering = serverAnswering();
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(Language.English, "English"),
        new(Language.Chinese, "中文"),
    ];

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    /// <summary>
    /// Whether a run that stops on a failure leaves a picture of the screen in the log folder.
    /// </summary>
    [ObservableProperty]
    public partial bool FailureScreenshot { get; set; }

    /// <summary>
    /// What this machine has of the two pieces driver-level input needs. Read once when the window
    /// opens: installing a driver is not something that happens while the window sits there.
    /// </summary>
    public DriverInputState DriverState { get; }

    /// <summary>Whether both pieces are in place, which is what the panel's colour says.</summary>
    public bool DriverReady { get; }

    /// <summary>The panel's sentence about this machine, in the chosen language.</summary>
    public string DriverStatus { get; }

    /// <summary>
    /// Whether a VIIPER server is answering right now. Read once with the rest: whether one is
    /// running is a fact about this moment, and the panel is not a live view.
    /// </summary>
    public bool ServerAnswering { get; }

    /// <summary>
    /// The line above the server's buttons: a server that is up needs nothing, one that is not says
    /// whether WhaleGenie can start it by itself.
    /// </summary>
    public string ServerStatus => Strings.Get(ServerAnswering
        ? "Settings.ServerReady"
        : HasViiperPath ? "Settings.ServerWillStart" : "Settings.ServerNotSet");

    /// <summary>Where viiper.exe is, or empty while nobody has chosen one.</summary>
    [ObservableProperty]
    public partial string ViiperPath { get; set; }

    public bool HasViiperPath => ViiperPath.Length > 0;

    /// <summary>
    /// Whether this copy of the program was started as an administrator, which decides whether a
    /// hotkey still reaches it while a window of a higher privilege is in front.
    /// </summary>
    public bool IsElevated { get; }

    /// <summary>The line above the button that restarts the program with that token.</summary>
    public string AdminStatus
        => Strings.Get(IsElevated ? "Settings.AdminYes" : "Settings.AdminNo");

    partial void OnSelectedLanguageChanged(LanguageOption value)
        => Strings.Current.Language = value.Language;

    /// <summary>Choosing a file changes both lines the panel shows, so both are announced again.</summary>
    partial void OnViiperPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasViiperPath));
        OnPropertyChanged(nameof(ServerStatus));
    }

    /// <summary>
    /// Writes the choice down, but only when it really changed: opening the window reads the
    /// stored value into this property, and that must not count as the user asking for something.
    /// </summary>
    partial void OnFailureScreenshotChanged(bool value)
    {
        if (value != LocalSettings.LoadFailureScreenshot())
        {
            LocalSettings.StoreFailureScreenshot(value);
        }
    }
}
