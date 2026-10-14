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
    /// hands in so that a machine which has neither the driver nor the server can be described.
    /// </summary>
    internal SettingsViewModel(IDriverInput driver, bool? elevated = null)
        : this(() => driver.State, driver.IsServerAnswering, elevated)
    {
    }

    /// <summary>
    /// The same, with the two answers about driver-level input handed in separately, so a check can
    /// describe a machine that is not this one instead of asking the registry and the network — and
    /// with what rights the program has, which a check cannot give itself either.
    /// </summary>
    internal SettingsViewModel(Func<DriverInputState> driverInput, Func<bool> serverAnswering,
        bool? elevated = null)
    {
        SelectedLanguage = Languages.First(option => option.Language == Strings.Current.Language);
        FailureScreenshot = LocalSettings.LoadFailureScreenshot();
        PreloadOcr = LocalSettings.LoadPreloadOcr();
        ViiperPath = LocalSettings.LoadViiperPath();
        SelectedWindowWay = WindowWays.First(option => option.Way == CaptureChoice.Current.Window);
        SelectedScreenWay = ScreenWays.First(option => option.Way == CaptureChoice.Current.Screen);
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
    /// Whether a run that stops on a failure leaves a picture of the screen in the log folder.
    /// </summary>
    [ObservableProperty]
    public partial bool FailureScreenshot { get; set; }

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

    /// <summary>The same, for the choice that holds the text recognition models in memory.</summary>
    partial void OnPreloadOcrChanged(bool value)
    {
        if (value != LocalSettings.LoadPreloadOcr())
        {
            LocalSettings.StorePreloadOcr(value);
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
