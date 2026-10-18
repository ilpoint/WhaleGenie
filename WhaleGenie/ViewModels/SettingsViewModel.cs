using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>One entry of the language picker, named in its own language.</summary>
public sealed record LanguageOption(Language Language, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// One entry of a capture picker. What it reads as comes from the text table rather than being
/// copied here, so the two pickers and the ways named in a failure are named the same way.
/// </summary>
public sealed record CaptureOption(CaptureWay Way, string Key)
{
    public override string ToString() => Strings.Get(Key);
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
    internal SettingsViewModel(IDriverInput driver, bool? elevated = null)
        : this(() => driver.State, elevated)
    {
    }

    /// <summary>
    /// The same, with what this machine has of driver-level input handed in, so a check can describe
    /// a machine that is not this one instead of asking the registry — and with what rights the
    /// program has, which a check cannot give itself either.
    /// </summary>
    internal SettingsViewModel(Func<DriverInputState> driverInput, bool? elevated = null)
    {
        SelectedLanguage = Languages.First(option => option.Language == Strings.Current.Language);
        PreloadOcr = LocalSettings.LoadPreloadOcr();
        AttachDriverAtStart = LocalSettings.LoadDriverAttachAtStart();
        SelectedWindowWay = WindowWays.First(option => option.Way == CaptureChoice.Current.Window);
        SelectedScreenWay = ScreenWays.First(option => option.Way == CaptureChoice.Current.Screen);
        IsElevated = elevated ?? ProcessRights.IsElevated;

        DriverState = driverInput();
        DriverReady = DriverState == DriverInputState.Ready;
    }

    /// <summary>Re-reads the capture choices, which show their own language when read.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(WindowWays));
        OnPropertyChanged(nameof(ScreenWays));
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(Language.English, "English"),
        new(Language.Chinese, "中文"),
    ];

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

    /// <summary>The line above the button that restarts the program with that token.</summary>
    public string AdminStatus
        => Strings.Get(IsElevated ? "Settings.AdminYes" : "Settings.AdminNo");

    partial void OnSelectedLanguageChanged(LanguageOption value)
        => Strings.Current.Language = value.Language;

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
