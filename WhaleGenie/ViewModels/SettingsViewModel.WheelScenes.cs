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

// The scenes a wheel can show: adding, changing, ordering, and the slots inside one.
public partial class SettingsViewModel
{

    // ── Wheel action manager ─────────────────────────────────────────────────

    public ObservableCollection<WheelSceneRow> WheelScenes { get; } = [];

    [ObservableProperty]
    public partial WheelSceneRow? SelectedWheelScene { get; set; }

    [ObservableProperty]
    public partial int WheelRevision { get; set; }

    private string _selectedWheelKey = WheelScene.GlobalKey;

    public WheelScene? EditingWheelScene => WheelRules.Find(_selectedWheelKey);

    public WheelScene WheelGlobal => WheelRules.Global;

    public string WheelProblem => string.Join(" ", WheelRules.Problems(
        [.. Project?.Macros ?? []], key => MenuIconCatalog.Knows(key)));

    public void RefreshWheelScenes()
    {
        WheelScenes.Clear();
        WheelScenes.Add(new WheelSceneRow(WheelScene.GlobalKey,
            WheelGlobal.Summary, WheelGlobal, true));
        foreach (var scene in WheelRules.Scenes)
        {
            WheelScenes.Add(new WheelSceneRow(scene.Key, scene.Summary, scene, false));
        }

        SelectedWheelScene = WheelScenes.FirstOrDefault(row => row.Key == _selectedWheelKey)
            ?? WheelScenes[0];
        OnPropertyChanged(nameof(WheelProblem));
        OnPropertyChanged(nameof(WheelRevision));
    }

    public void AddWheelScene(WindowRule rule)
    {
        var scene = WheelRules.AddScene(rule);
        _selectedWheelKey = scene.Key;
        TouchWheel();
        RefreshWheelScenes();
    }

    public void UpdateWheelScene(WheelSceneRow row, WindowRule rule)
    {
        if (WheelRules.Find(row.Key) is { IsGlobal: false } scene)
        {
            scene.Window = rule with { };
            TouchWheel();
            RefreshWheelScenes();
        }
    }

    public void RemoveWheelScene(WheelSceneRow row)
    {
        if (row.IsGlobal)
        {
            return;
        }

        WheelRules.RemoveScene(row.Key);
        _selectedWheelKey = WheelScene.GlobalKey;
        TouchWheel();
        RefreshWheelScenes();
    }

    public void MoveWheelScene(int step)
    {
        var row = SelectedWheelScene;
        if (row is null || row.IsGlobal)
        {
            return;
        }

        var scenes = WheelRules.Scenes;
        var from = scenes.FirstOrDefault(scene => scene.Key == row.Key);
        if (from is null)
        {
            return;
        }

        var index = scenes.IndexOf(from);
        var target = Math.Clamp(index + step, 0, scenes.Count - 1);
        if (target == index)
        {
            return;
        }

        scenes.RemoveAt(index);
        scenes.Insert(target, from);
        TouchWheel();
        RefreshWheelScenes();
    }

    public void SetWheelSlot(WheelSlotRef reference, string? macroName)
    {
        if (EditingWheelScene is not { } scene)
        {
            return;
        }

        var slot = scene.At(reference);
        slot.Macro = macroName?.Trim() ?? string.Empty;
        if (slot.IsEmpty)
        {
            slot.Icon = null;
            slot.Note = string.Empty;
        }

        TouchWheel();
    }

    /// <summary>
    /// What the editor should open for a position. A program scene shows the global action it
    /// inherits until it fills that position itself; editing starts from that action and saves it
    /// back as a program override, which is the same rule the wheel follows when it is drawn.
    /// </summary>
    public WheelSlot? WheelSlotForEdit(WheelSlotRef reference)
    {
        if (EditingWheelScene is not { } scene)
        {
            return null;
        }

        var own = scene.At(reference);
        if (!scene.IsGlobal && own.IsEmpty
            && WheelRules.Global.At(reference) is { IsEmpty: false } inherited)
        {
            return inherited.Copy();
        }

        return own.Copy();
    }

    /// <summary>Whether the position is being drawn from the global scene rather than its own.</summary>
    public bool IsWheelSlotInherited(WheelSlotRef reference)
        => EditingWheelScene is { IsGlobal: false } scene
           && scene.At(reference).IsEmpty
           && WheelRules.Global.At(reference) is { IsEmpty: false };

    /// <summary>
    /// Writes an edited action back into the position. An action with no macro is left completely
    /// empty, so "clear" cannot leave a note or an icon behind in a slot nothing runs.
    /// </summary>
    public void SetWheelAction(WheelSlotRef reference, WheelSlot edited)
    {
        if (EditingWheelScene is not { } scene)
        {
            return;
        }

        var slot = scene.At(reference);
        slot.Macro = edited.Macro.Trim();
        slot.Note = slot.Macro.Length == 0 ? string.Empty : edited.Note.Trim();
        slot.Icon = slot.Macro.Length == 0 || edited.Icon is not { IsEmpty: false } icon
            ? null
            : icon;

        TouchWheel();
    }

    private void TouchWheel()
    {
        WheelMenuStore.Save(_wheelState);
        Project?.Triggers.NotifyChanged();
        OnPropertyChanged(nameof(WheelProblem));
        OnPropertyChanged(nameof(WheelLauncherStatus));
        WheelRevision++;
    }

    partial void OnSelectedWheelSceneChanged(WheelSceneRow? value)
    {
        if (value is not null)
        {
            _selectedWheelKey = value.Key;
        }

        OnPropertyChanged(nameof(EditingWheelScene));
    }

    // ── Menu launcher: the middle button by default, or a custom key ─────────

    public bool MenuLauncherMiddle
    {
        get => string.Equals(MenuLauncher, WheelOptions.MiddleButton,
            StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                CaptureMenuLauncher(WheelOptions.MiddleButton);
            }
        }
    }

    public bool MenuLauncherCustom
    {
        get => IsCapturingMenuLauncher
            || (MenuLauncher.Length > 0 && !MenuLauncherMiddle);
        set
        {
            if (value)
            {
                IsCapturingWheelRepeat = false;
                BeginMenuLauncherCapture();
            }
            else
            {
                StopMenuLauncherCapture();
            }
        }
    }

    public void BeginWheelRepeatCapture()
    {
        IsCapturingMenuLauncher = false;
        IsCapturingSystemHotkey = false;
        IsCapturingRecordStop = false;
        IsCapturingWheelRepeat = true;
    }

    public bool CaptureWheelRepeat(string binding)
    {
        if (!HotkeyBinding.IsSupported(binding))
        {
            return false;
        }

        IsCapturingWheelRepeat = false;
        WheelRepeatKey = binding.Trim();
        return true;
    }

    private void ReadWheel()
    {
        _wheelState = WheelMenuStore.Load();
        WheelLauncher = _wheelState.Options.Launcher;
        WheelDiameter = _wheelState.Options.Diameter;
        _wheelReady = true;
        RefreshWheelScenes();
    }

    private void SaveWheel()
    {
        OnPropertyChanged(nameof(WheelLauncherStatus));
        if (!_wheelReady)
        {
            return;
        }

        _wheelState.Options.Launcher = WheelLauncher;
        _wheelState.Options.Diameter =
            Math.Clamp((int)Math.Round(WheelDiameter),
                WheelOptions.SmallestDiameter, WheelOptions.LargestDiameter);
        WheelMenuStore.Save(_wheelState);
    }

    partial void OnWheelLauncherChanged(string value) => SaveWheel();

    partial void OnWheelDiameterChanged(double value) => SaveWheel();

    partial void OnWheelLauncherMessageChanged(string value)
        => OnPropertyChanged(nameof(WheelLauncherStatus));

    partial void OnIsCapturingWheelRepeatChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCapturingHotkey));
        OnPropertyChanged(nameof(WheelRepeatButton));
    }
}
