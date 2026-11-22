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

// How the wheel looks and behaves: capacity, size, timing, the repeat key and the switches.
public partial class SettingsViewModel
{
    [ObservableProperty]
    public partial bool IsCapturingWheelRepeat { get; set; }

    public string WheelRepeatButton => Strings.Get(IsCapturingWheelRepeat
        ? "Settings.HotkeyPress"
        : "Settings.HotkeySet");

    public IReadOnlyList<WheelCapacityOption> ThirdCapacityOptions { get; } =
    [
        new(0, "Settings.WheelThird0"),
        new(8, "Settings.WheelThird8"),
        new(16, "Settings.WheelThird16"),
        new(24, "Settings.WheelThird24"),
    ];

    public WheelCapacityOption SelectedThirdCapacity
    {
        get => ThirdCapacityOptions.First(option =>
            option.Value == _wheelState.Options.ThirdCapacity);
        set => SetWheel(o => o.ThirdCapacity, (o, v) => o.ThirdCapacity = v, value.Value);
    }

    public double WheelFontSize
    {
        get => _wheelState.Options.FontSize;
        set => SetWheel(o => (double)o.FontSize,
            (o, v) => o.FontSize = Math.Clamp((int)Math.Round(v),
                WheelOptions.SmallestFont, WheelOptions.LargestFont), value);
    }

    public double WheelTimeoutMs
    {
        get => _wheelState.Options.TimeoutMs;
        set => SetWheel(o => (double)o.TimeoutMs,
            (o, v) => o.TimeoutMs = Math.Clamp((int)Math.Round(v), 0,
                WheelOptions.LongestTimeoutMs), value);
    }

    public string WheelRepeatKey
    {
        get => _wheelState.Options.RepeatKey;
        set => SetWheel(o => o.RepeatKey, (o, v) => o.RepeatKey = v, value.Trim());
    }

    public bool WideOuter
    {
        get => _wheelState.Options.WideOuter;
        set => SetWheel(o => o.WideOuter, (o, v) => o.WideOuter = v, value);
    }

    public bool HideEmptyOuter
    {
        get => _wheelState.Options.HideEmptyOuter;
        set => SetWheel(o => o.HideEmptyOuter, (o, v) => o.HideEmptyOuter = v, value);
    }

    public bool ShowGuide
    {
        get => _wheelState.Options.ShowGuide;
        set => SetWheel(o => o.ShowGuide, (o, v) => o.ShowGuide = v, value);
    }

    public bool ExtendedHint
    {
        get => _wheelState.Options.ExtendedHint;
        set => SetWheel(o => o.ExtendedHint, (o, v) => o.ExtendedHint = v, value);
    }

    public bool HideWheelLabelWithIcon
    {
        get => _wheelState.Options.HideLabelWithIcon;
        set => SetWheel(o => o.HideLabelWithIcon, (o, v) => o.HideLabelWithIcon = v, value);
    }

    public bool CenterName
    {
        get => _wheelState.Options.CenterName;
        set => SetWheel(o => o.CenterName, (o, v) => o.CenterName = v, value);
    }

    public bool ClampToScreen
    {
        get => _wheelState.Options.ClampToScreen;
        set => SetWheel(o => o.ClampToScreen, (o, v) => o.ClampToScreen = v, value);
    }

    public bool MovePointerToCenter
    {
        get => _wheelState.Options.MovePointerToCenter;
        set => SetWheel(o => o.MovePointerToCenter, (o, v) => o.MovePointerToCenter = v, value);
    }

    public bool FollowTheme
    {
        get => _wheelState.Options.FollowTheme;
        set => SetWheel(o => o.FollowTheme, (o, v) => o.FollowTheme = v, value);
    }

    public bool Gloss
    {
        get => _wheelState.Options.Gloss;
        set => SetWheel(o => o.Gloss, (o, v) => o.Gloss = v, value);
    }

    public bool HoverBorder
    {
        get => _wheelState.Options.HoverBorder;
        set => SetWheel(o => o.HoverBorder, (o, v) => o.HoverBorder = v, value);
    }

    public bool PopAnimation
    {
        get => _wheelState.Options.PopAnimation;
        set => SetWheel(o => o.PopAnimation, (o, v) => o.PopAnimation = v, value);
    }

    /// <summary>
    /// Every wheel choice reads and writes the same option object, so the file, the preview and the
    /// running wheel can never disagree. The caller's name is used for the change notification.
    /// </summary>
    private void SetWheel<T>(Func<WheelOptions, T> read, Action<WheelOptions, T> write,
        T value, [CallerMemberName] string? name = null)
    {
        var options = _wheelState.Options;
        if (EqualityComparer<T>.Default.Equals(read(options), value))
        {
            return;
        }

        write(options, value);
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(WheelRevision));
        SaveWheel();
    }
}
