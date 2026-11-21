using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class AdvancedSettingsWindow
{
    /// <summary>
    /// Keys typed into the settings window: they belong to the interface rather than to a
    /// macro, so a capture can hold a combination without setting one off.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingWheelRepeat)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.IsCapturingWheelRepeat = false;
            }
            else
            {
                _viewModel.CaptureWheelRepeat(Chord(e.Key, e.KeyModifiers));
            }

            e.Handled = true;
            return;
        }

        if (_viewModel.IsCapturingSystemHotkey)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.IsCapturingSystemHotkey = false;
                e.Handled = true;
                return;
            }

            _viewModel.CaptureSystemHotkey(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (_viewModel.IsCapturingMenuLauncher)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.StopMenuLauncherCapture();
                e.Handled = true;
                return;
            }

            _viewModel.CaptureMenuLauncher(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (_viewModel.IsCapturingRecordStop)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.IsCapturingRecordStop = false;
                e.Handled = true;
                return;
            }

            _viewModel.CaptureRecordStop(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            // 气泡固定着时 Esc 先收气泡，窗口留着；没有气泡才按原来的意思关窗。
            if (HintBubbleHost.For(this).DismissIfOpen(HintBubbleCloseReason.Escape))
            {
                e.Handled = true;
                return;
            }

            Close();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingHotkey && HotkeyNameMapper.IsModifier(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_viewModel.IsCapturingMenuLauncher)
        {
            return;
        }

        // A press on the buttons of the hotkey row is the user cancelling or clearing, not a
        // mouse button to record. The handler runs on the way down, before the button sees the
        // click, so swallowing it here left the capture impossible to stop with the mouse.
        if (IsHotkeyButton(e.Source))
        {
            return;
        }

        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => HookMouseButton.Left,
            PointerUpdateKind.RightButtonPressed => HookMouseButton.Right,
            PointerUpdateKind.MiddleButtonPressed => HookMouseButton.Middle,
            PointerUpdateKind.XButton1Pressed => HookMouseButton.Back,
            PointerUpdateKind.XButton2Pressed => HookMouseButton.Forward,
            _ => (HookMouseButton?)null,
        };
        if (button is not { } pressed)
        {
            return;
        }

        var binding = KeyNames.MouseName(pressed);
        _viewModel.CaptureMenuLauncher(binding);
        e.Handled = true;
    }

    /// <summary>Whether a press landed on one of the buttons that set, cancel or clear a hotkey.</summary>
    private static bool IsHotkeyButton(object? source)
    {
        var names = new[]
        {
            "CaptureSystemHotkeyButton", "ClearSystemHotkeyButton",
            "CaptureMenuLauncherButton", "ClearMenuLauncherButton",
            "CaptureRecordStopButton",
        };

        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            // Wheel buttons are looked up by their Click handler, so they carry a tag instead of
            // a name: a click on one of them cancels or clears, not a mouse button to record.
            if (visual is Control { Tag: string tag } && tag == "HotkeyButton")
            {
                return true;
            }

            if (visual is Button button && button.Name is { } name
                && names.Contains(name, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_viewModel.IsCapturingMenuLauncher)
        {
            return;
        }

        var (axis, rotation) = System.Math.Abs(e.Delta.X) > System.Math.Abs(e.Delta.Y)
            ? (HookWheelAxis.Horizontal, e.Delta.X < 0 ? 1 : -1)
            : (HookWheelAxis.Vertical, e.Delta.Y >= 0 ? 1 : -1);
        var binding = KeyNames.WheelName(axis, rotation);
        _viewModel.CaptureMenuLauncher(binding);
        e.Handled = true;
    }

    private static string Chord(Key key, KeyModifiers held)
        => HotkeyNameMapper.Chord(key, held);

    /// <summary>
    /// Opens the folder one of those rows names, making it first: the folders for a project's
    /// pictures only exist once something has been saved, and a button that fails on a fresh
    /// install teaches the wrong thing about where things go.
    /// </summary>
}

