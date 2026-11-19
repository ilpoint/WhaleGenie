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

// The key that opens the menu: capturing it, what it is set to, and why a binding is refused.
public partial class SettingsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MenuLauncherButton))]
    public partial bool IsCapturingMenuLauncher { get; set; }

    [ObservableProperty]
    public partial string MenuLauncherMessage { get; set; } = string.Empty;

    public string MenuLauncherDisplay => MenuLauncher.Length == 0
        ? Strings.Get("Common.None")
        : MenuLauncher;

    public string MenuLauncherButton => Strings.Get(IsCapturingMenuLauncher
        ? "Settings.HotkeyPress"
        : "Settings.HotkeySet");

    public string MenuLauncherStatus
    {
        get
        {
            if (MenuLauncherMessage.Length > 0)
            {
                return MenuLauncherMessage;
            }

            if (MenuLauncher.Length == 0)
            {
                return Strings.Get("Settings.MenuLauncherMissing");
            }

            // The wheel takes the press for its own gesture, so the same button cannot also open
            // the menu: whichever ran first would swallow the other.
            if (WheelLauncher.Length > 0
                && string.Equals(MenuLauncher, WheelLauncher, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Get("Settings.MenuLauncherConflictWheel");
            }

            if (SystemHotkey.Length > 0
                && string.Equals(MenuLauncher, SystemHotkey, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Get("Settings.MenuLauncherConflictHotkey");
            }

            if (Project is { IsRunning: false })
            {
                return Strings.Get("Settings.MenuSystemOff");
            }

            var conflicts = ConflictBindings().ToList();
            if (conflicts.Any(binding =>
                    string.Equals(binding, MenuLauncher, StringComparison.OrdinalIgnoreCase)))
            {
                return Strings.Get("Settings.MenuLauncherConflict");
            }

            var menuProblems = Project?.Triggers.MenuProblems([.. Project.Macros],
                MenuIconCatalog.Knows) ?? [];
            if (menuProblems.Count > 0)
            {
                return menuProblems[0];
            }

            return Project is not null && Project.Triggers.Entries.Any(entry =>
                entry.Kind == TriggerKind.Menu && entry.IsEnabled)
                ? Strings.Get("Settings.MenuReady")
                : Strings.Get("Settings.MenuEmpty");
        }
    }

    public string MenuRuleProblem
    {
        get
        {
            if (Project is null)
            {
                return string.Empty;
            }

            if (Project.Triggers.ContextMenuPages()
                .Any(page => !page.Window.HasTarget))
            {
                return Strings.Get("Menu.Problem.RuleEmpty");
            }
            return Project.Triggers.BlockedMenuRules.Any(rule => !rule.HasTarget)
                ? Strings.Get("Menu.Problem.BlockedRuleEmpty")
                : string.Empty;
        }
    }

    public void SetPage(SettingsSubPage page) => SelectedPage = page;

    public void BeginMenuLauncherCapture()
    {
        MenuLauncherMessage = string.Empty;
        IsCapturingMenuLauncher = true;
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    public void StopMenuLauncherCapture()
    {
        IsCapturingMenuLauncher = false;
        MenuLauncherMessage = string.Empty;
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    public bool CaptureMenuLauncher(string binding)
    {
        if (!AllowedMenuLauncher(binding))
        {
            MenuLauncherMessage = Strings.Get("Settings.MenuLauncherInvalid");
            OnPropertyChanged(nameof(MenuLauncherStatus));
            return false;
        }

        if (WheelLauncher.Length > 0
            && string.Equals(binding.Trim(), WheelLauncher, StringComparison.OrdinalIgnoreCase))
        {
            MenuLauncherMessage = Strings.Get("Settings.MenuLauncherConflictWheel");
            OnPropertyChanged(nameof(MenuLauncherStatus));
            return false;
        }

        if (SystemHotkey.Length > 0
            && string.Equals(binding.Trim(), SystemHotkey, StringComparison.OrdinalIgnoreCase))
        {
            MenuLauncherMessage = Strings.Get("Settings.MenuLauncherConflictHotkey");
            OnPropertyChanged(nameof(MenuLauncherStatus));
            return false;
        }

        IsCapturingMenuLauncher = false;
        MenuLauncherMessage = string.Empty;
        MenuLauncher = binding.Trim();
        LocalSettings.StoreMenuLauncher(MenuLauncher);
        OnPropertyChanged(nameof(MenuLauncherStatus));
        return true;
    }

    public void ClearMenuLauncher()
    {
        IsCapturingMenuLauncher = false;
        MenuLauncherMessage = string.Empty;
        MenuLauncher = string.Empty;
        LocalSettings.StoreMenuLauncher(string.Empty);
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    // ── Wheel menu ────────────────────────────────────────────────────────────

    private WheelMenuState _wheelState = new();

    private bool _wheelReady;

    public WheelMenuState WheelState => _wheelState;

    /// <summary>
    /// The positions being edited: the project's own, or an empty set while no project is open.
    /// The wheel's look and button stay in the machine's file, but a position names a macro, so it
    /// travels with the project.
    /// </summary>
    private readonly WheelMenuRules _wheelDraft = new();

    private WheelMenuRules WheelRules => Project?.Triggers.Wheel ?? _wheelDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WheelEnabled))]
    [NotifyPropertyChangedFor(nameof(SelectedWheelLauncher))]
    [NotifyPropertyChangedFor(nameof(WheelLauncherStatus))]
    public partial string WheelLauncher { get; set; } = WheelOptions.RightButton;

    [ObservableProperty]
    public partial double WheelDiameter { get; set; } = 420;

    [ObservableProperty]
    public partial string WheelLauncherMessage { get; set; } = string.Empty;
}
