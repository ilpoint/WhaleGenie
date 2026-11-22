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

// General settings: what opens at start, frame rate and cache, input rate, logging, sound and the driver.
public partial class SettingsViewModel
{
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
    /// Whether the frame cap follows the display. On means the fastest a macro usefully looks,
    /// because a display cannot draw anything new between two refreshes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameRateEnabled))]
    [NotifyPropertyChangedFor(nameof(FrameRateNote))]
    public partial bool FrameRateFollow { get; set; } = true;

    /// <summary>
    /// Pictures a second a run may take, held as the number field's double because that is what the
    /// control speaks. It is a count, so the rounded value is what is stored.
    /// </summary>
    [ObservableProperty]
    public partial double FrameRate { get; set; }

    /// <summary>Whether the field beside the switch is a choice the person can make.</summary>
    public bool FrameRateEnabled => !FrameRateFollow;

    /// <summary>The fastest this machine can publish, said in both units.</summary>
    public string FrameRateNote => Strings.Format("Settings.FrameRateNote", FastestRate);

    /// <summary>The ceiling of the frame field: the display's own rate.</summary>
    public int FastestRate => FrameSetup.FastestRate;

    partial void OnFrameRateFollowChanged(bool value)
    {
        if (_frameReady)
        {
            FrameSetup.StoreRate(value, (int)Math.Round(FrameRate));
        }
    }

    partial void OnFrameRateChanged(double value)
    {
        var rate = Math.Clamp((int)Math.Round(value), 1, FrameSetup.FastestRate);
        if (_frameReady && !FrameRateFollow)
        {
            FrameSetup.StoreRate(followDisplay: false, rate);
        }
    }

    /// <summary>
    /// Whether a picture already taken may be read again instead of taken again. Off unless the
    /// person turned it on: a step that waits for something to disappear must see it disappear.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameCacheAgeEnabled))]
    public partial bool FrameCache { get; set; }

    /// <summary>How old a reused picture may be, in milliseconds.</summary>
    [ObservableProperty]
    public partial double FrameCacheAge { get; set; }

    public bool FrameCacheAgeEnabled => FrameCache;

    partial void OnFrameCacheChanged(bool value)
    {
        if (_frameReady)
        {
            FrameSetup.StoreCache(value, (int)Math.Round(FrameCacheAge));
        }
    }

    partial void OnFrameCacheAgeChanged(double value)
    {
        var age = Math.Clamp((int)Math.Round(value), 0, AppSettings.FrameCacheOldest);
        if (_frameReady && FrameCache)
        {
            FrameSetup.StoreCache(reuse: true, age);
        }
    }

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
        if (rate != _settings.LoadInputRate())
        {
            _settings.StoreInputRate(rate);
        }
    }

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
        if (count != _settings.LoadLogCount())
        {
            _settings.StoreLogCount(count);
        }
    }

    /// <summary>
    /// Whether what this program sends counts as its own rather than as the person's. It reaches the
    /// triggers at once — they ask this on every key — while a recording already open keeps the value
    /// it started with, because that one can be changed for that recording alone.
    /// </summary>
    [ObservableProperty]
    public partial bool IgnoreOwnInput { get; set; }

    /// <summary>Whether the program's own switch is heard when it goes on or off.</summary>
    [ObservableProperty]
    public partial bool SoundSystem { get; set; }

    /// <summary>Whether arming or disarming one macro is heard.</summary>
    [ObservableProperty]
    public partial bool SoundMacro { get; set; }

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
    public partial string SystemHotkey { get; set; } = AppSettings.Shared.LoadSystemHotkey();
}
