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

// What opens the wheel, whether it is on, and the layout it is drawn from.
public partial class SettingsViewModel
{
    /// <summary>
    /// The four buttons a wheel can be opened with. The list is fixed on purpose: the gesture is
    /// "hold and move", which the left button cannot do without dragging what is under it, and a
    /// key that has to be held while the pointer moves is a different thing to explain.
    /// </summary>
    public IReadOnlyList<WheelLauncherChoice> WheelLauncherChoices { get; } =
    [
        new(WheelOptions.RightButton, "Settings.WheelLauncher.Right"),
        new(WheelOptions.MiddleButton, "Settings.WheelLauncher.Middle"),
        new(WheelOptions.ForwardButton, "Settings.WheelLauncher.Forward"),
        new(WheelOptions.BackButton, "Settings.WheelLauncher.Back"),
    ];

    /// <summary>
    /// The button in the list, or null while the wheel is switched off. Setting it goes through
    /// <see cref="SetWheelLauncher"/>, so a button the menu already owns is refused with a reason
    /// rather than stored and quietly losing one of the two gestures.
    /// </summary>
    public WheelLauncherChoice? SelectedWheelLauncher
    {
        get => WheelLauncherChoices.FirstOrDefault(choice =>
            string.Equals(choice.Binding, WheelLauncher, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
            {
                SetWheelLauncher(value.Binding);
            }
        }
    }

    public string WheelLauncherStatus
    {
        get
        {
            if (WheelLauncherMessage.Length > 0)
            {
                return WheelLauncherMessage;
            }

            if (WheelLauncher.Length == 0)
            {
                return Strings.Get("Wheel.LauncherMissing");
            }

            if (MenuOwns(WheelLauncher))
            {
                return Strings.Get("Wheel.LauncherConflict");
            }

            if (SystemHotkey.Length > 0
                && string.Equals(WheelLauncher, SystemHotkey, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Get("Wheel.LauncherConflictHotkey");
            }

            if (Project is { IsRunning: false })
            {
                return Strings.Get("Wheel.SystemOff");
            }

            // "Only in these programs" with no program scene is a wheel that cannot open anywhere.
            // The switch is one click away from being understood, but only if the line says so.
            if (OnlyInPrograms && WheelRules.Scenes.Count == 0)
            {
                return Strings.Get("Wheel.OnlyInProgramsNoScene");
            }

            return Strings.Get("Wheel.Ready");
        }
    }

    /// <summary>
    /// The wheel and the menu both need the system switch on. It is off by default after a restart,
    /// and that looks exactly like a broken hotkey, so the settings page offers the switch itself.
    /// </summary>
    public bool IsSystemOff => Project is { IsRunning: false };

    public void EnableSystem()
    {
        if (Project is { IsRunning: false } project)
        {
            project.IsRunning = true;
            OnPropertyChanged(nameof(IsSystemOff));
            OnPropertyChanged(nameof(WheelLauncherStatus));
        }
    }

    public bool WheelEnabled
    {
        get => WheelLauncher.Length > 0;
        set
        {
            if (value)
            {
                SetWheelLauncher(WheelOptions.RightButton);
            }
            else
            {
                ClearWheelLauncher();
            }
        }
    }

    /// <summary>
    /// Keeps the wheel to the programs that have a scene. It is the switch for people who want the
    /// trigger button everywhere except in the two or three programs they actually use the wheel in.
    /// </summary>
    public bool OnlyInPrograms
    {
        get => _wheelState.Options.OnlyInPrograms;
        set
        {
            SetWheel(o => o.OnlyInPrograms, (o, v) => o.OnlyInPrograms = v, value);
            OnPropertyChanged(nameof(WheelLauncherStatus));
        }
    }

    /// <summary>
    /// Stores one of the four buttons. The menu's own button is refused: both gestures start on a
    /// press, and a press can only belong to one of them, so whichever ran second would never open.
    /// </summary>
    public bool SetWheelLauncher(string binding)
    {
        if (MenuOwns(binding))
        {
            WheelLauncherMessage = Strings.Get("Wheel.LauncherConflict");
            OnPropertyChanged(nameof(WheelLauncherStatus));
            OnPropertyChanged(nameof(SelectedWheelLauncher));
            return false;
        }

        // The system switch is a keyboard hotkey, so this cannot normally collide; a stored file
        // that says otherwise is still worth refusing rather than leaving two jobs on one key.
        if (SystemHotkey.Length > 0
            && string.Equals(binding.Trim(), SystemHotkey.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            WheelLauncherMessage = Strings.Get("Wheel.LauncherConflictHotkey");
            OnPropertyChanged(nameof(WheelLauncherStatus));
            OnPropertyChanged(nameof(SelectedWheelLauncher));
            return false;
        }

        WheelLauncherMessage = string.Empty;
        WheelLauncher = binding.Trim();
        OnPropertyChanged(nameof(WheelLauncherStatus));
        return true;
    }

    public void ClearWheelLauncher()
    {
        WheelLauncherMessage = string.Empty;
        WheelLauncher = string.Empty;
        OnPropertyChanged(nameof(WheelLauncherStatus));
    }

    /// <summary>Whether the floating macro menu opens on this binding.</summary>
    private bool MenuOwns(string binding)
        => MenuLauncher.Length > 0
           && string.Equals(MenuLauncher.Trim(), binding.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The layout the preview shows: the global scene resolved against this project's macros.</summary>
    public WheelRuntimeLayout BuildWheelLayout()
        => WheelRules.Resolve(null, WheelSource.Global,
            _wheelState.Options.WideOuter, [.. Project?.Macros ?? []],
            _wheelState.Options.ThirdCapacity, _wheelState.Options.HideEmptyOuter);
}
