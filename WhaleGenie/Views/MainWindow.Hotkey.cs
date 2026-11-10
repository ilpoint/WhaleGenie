using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// The system hotkey and the switches it drives, and the trigger state the window shows.
public partial class MainWindow : AppWindow
{
    /// <summary>
    /// Answers the Home key from anywhere, so the system switch works without bringing the window
    /// forward. The hook is the one the trigger service already keeps alive for the process.
    /// </summary>
    private void AttachSystemHotkey()
    {
        try
        {
            var hook = SharedDevices.Current.Hook;
            hook.KeyDown += OnSystemHotkeyDown;
            hook.KeyUp += OnSystemHotkeyUp;
            hook.MouseDown += OnMenuMouseDown;
            hook.MouseDown += OnWheelMouseDown;
            hook.MouseUp += OnWheelMouseUp;
            hook.Moved += OnWheelMouseMoved;
            hook.KeyDown += OnWheelKeyDown;
            hook.Wheel += OnWheelScroll;
            _hotkeyHook = hook;
        }
        catch (Exception)
        {
            // Without a hook the button on the window still works.
            _hotkeyHook = null;
        }
    }

    private void DetachSystemHotkey()
    {
        if (_hotkeyHook is { } hook)
        {
            hook.KeyDown -= OnSystemHotkeyDown;
            hook.KeyUp -= OnSystemHotkeyUp;
            hook.MouseDown -= OnMenuMouseDown;
            hook.MouseDown -= OnWheelMouseDown;
            hook.MouseUp -= OnWheelMouseUp;
            hook.Moved -= OnWheelMouseMoved;
            hook.KeyDown -= OnWheelKeyDown;
            hook.Wheel -= OnWheelScroll;
            _globalHeld.Clear();
            _hotkeyHook = null;
        }
    }

    private void OnSystemHotkeyDown(KeyHookEvent e)
    {
        var name = string.Empty;
        try
        {
            // The editor and the other dialogs own the keyboard while they are open.
            name = KeyNames.Name(e.Key, sided: true);
            if (name.Length == 0)
            {
                return;
            }

            // A key WhaleGenie sent itself is its own output, not the user asking for a switch.
            if (WhaleGenieInputGate.RecentlySent(name))
            {
                return;
            }

            var plan = Volatile.Read(ref _globalHotkeyPlan);
            if (Volatile.Read(ref _menuOpen) == 1)
            {
                if (plan.MenuLauncher.Length > 0
                    && MacroTriggerService.Binds(plan.MenuLauncher, name, _globalHeld))
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(() => _menu?.CloseFromLauncher());
                    return;
                }

                if (Volatile.Read(ref _menuSearchFocused) == 1)
                {
                    return;
                }

                if (_menu?.IsPinned == true)
                {
                    return;
                }

                if (name is "Left" or "Right" or "Up" or "Down"
                    or "左" or "右" or "上" or "下"
                    or "Enter" or "Return" or "回车" or "Esc" or "Escape")
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(() => _menu?.HandleKey(name));
                    return;
                }
            }

            if (Volatile.Read(ref _menuOpen) == 1
                && plan.SystemHotkey.Length > 0
                && MacroTriggerService.Binds(plan.SystemHotkey, name, _globalHeld))
            {
                e.Suppress = true;
                Dispatcher.UIThread.Post(ToggleSystemFromHotkey);
                return;
            }

            if (MacroTriggerGate.IsOpen)
            {
                return;
            }

            var match = plan.Match(name, _globalHeld);
            if (match.Action == GlobalHotkeyAction.ToggleSystem)
            {
                Dispatcher.UIThread.Post(ToggleSystemFromHotkey);
            }
            else if (match.Action == GlobalHotkeyAction.ToggleMacro)
            {
                // An arm/disarm key is a switch, not something the person meant to type into
                // whatever window is in front, so it is held back the way the system switch is.
                e.Suppress = true;
                Dispatcher.UIThread.Post(() => ToggleMacroFromHotkey(match.MacroName));
            }
            else if (match.Action == GlobalHotkeyAction.ToggleInterfaceGroup)
            {
                Dispatcher.UIThread.Post(() => ToggleInterfaceGroupFromHotkey(match.GroupKey));
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.global-shortcut", error);
        }
        finally
        {
            if (name.Length > 0)
            {
                _globalHeld.Add(name);
            }
        }
    }

    private void OnSystemHotkeyUp(KeyHookEvent e)
    {
        try
        {
            var name = KeyNames.Name(e.Key, sided: true);
            if (name.Length > 0)
            {
                _globalHeld.Remove(name);
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.global-shortcut-up", error);
        }
    }

    /// <summary>Flips the system switch, unless a field on the window has the keyboard.</summary>
    private void ToggleSystemFromHotkey()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (IsActive && FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        viewModel.IsRunning = !viewModel.IsRunning;
    }

    private void ToggleInterfaceGroupFromHotkey(string groupKey)
    {
        if (!_hiddenInterfaceGroups.Add(groupKey))
        {
            _hiddenInterfaceGroups.Remove(groupKey);
        }

        RefreshSurface(DataContext as MainViewModel);
    }

    /// <summary>Whether the pressed key should answer a macro, rather than the app being edited.</summary>
    private void OnTriggerStatusChanged()
        => Dispatcher.UIThread.Post(() => RefreshRunning(DataContext as MainViewModel));

    /// <summary>Marks the macros the trigger is running, which colours the dots on their cards.</summary>
    private void RefreshRunning(MainViewModel? viewModel)
    {
        if (_triggers is null || viewModel is null)
        {
            return;
        }

        foreach (var macro in viewModel.Macros)
        {
            macro.IsRunning = _triggers.IsRunning(macro);
        }
    }

    private void StopTriggers()
    {
        if (_triggers is null)
        {
            return;
        }

        _triggers.StatusChanged -= OnTriggerStatusChanged;

        // The container owns the service, so this only stops it: the window that shows what a
        // trigger did is going away, and nothing should keep triggering behind it.
        _triggers.IsEnabled = false;
        _triggers.StopAll();
        _triggers = null;
    }
}
