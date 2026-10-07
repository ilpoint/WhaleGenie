using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices.Platform;
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
        : this(DriverInput.Check)
    {
    }

    /// <summary>
    /// The same, with the answer about driver-level input handed in, so a check can describe a
    /// machine that is not this one instead of asking the registry and the network.
    /// </summary>
    internal SettingsViewModel(Func<DriverInputState> driverInput)
    {
        SelectedLanguage = Languages.First(option => option.Language == Strings.Current.Language);
        FailureScreenshot = LocalSettings.LoadFailureScreenshot();

        DriverState = driverInput();
        DriverReady = DriverState == DriverInputState.Ready;
        DriverStatus = Strings.Get(DriverState switch
        {
            DriverInputState.DriverMissing => "Settings.DriverMissing",
            DriverInputState.ServerMissing => "Settings.DriverServerMissing",
            _ => "Settings.DriverReady",
        });
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

    partial void OnSelectedLanguageChanged(LanguageOption value)
        => Strings.Current.Language = value.Language;

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
