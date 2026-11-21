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

// The language, theme and capture choices the general page offers.
public partial class SettingsViewModel
{
    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(Language.English, "English"),
        new(Language.Chinese, "中文"),
    ];

    /// <summary>
    /// Whether the frame settings have been read in. They are written back the moment they change,
    /// and opening a window is not a change: without this, opening the settings would rewrite the
    /// file that was just read.
    /// </summary>
    private bool _frameReady;

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
}
