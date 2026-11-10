using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // Bindings and the controller preview: the settings pages that take a key or button, the
    // controller read while waiting, and the preview surface that shows what a trigger catches.
    private async void OnOpenHotstringKeyboard(object? sender, RoutedEventArgs e)
    {
        if (_hotstringKeyboard is { } open)
        {
            open.Activate();
            return;
        }

        var keyboard = new HotstringKeyboardWindow(_viewModel.HotstringText);
        _hotstringKeyboard = keyboard;
        try
        {
            if (await keyboard.ShowDialogOver<string?>(this) is { } text)
            {
                _viewModel.HotstringText = text;
            }
        }
        finally
        {
            if (ReferenceEquals(_hotstringKeyboard, keyboard))
            {
                _hotstringKeyboard = null;
            }
        }
    }

    private void OnCaptureHotstringEnd(object? sender, RoutedEventArgs e)
        => _viewModel.BeginHotstringEndCapture();

    private void OnSetInterfaceHotkey(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.BeginKeyCapture(binding =>
            {
                _viewModel.SetInterfaceToggleHotkey(binding);
                ResetKeyCaptureLabel();
            }, combination: true, keyboardOnly: true))
        {
            _viewModel.IsCapturingInterfaceHotkey = true;
        }
    }

    /// <summary>Switches the settings drawer between its two halves.</summary>
    private void OnSettingsPageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _viewModel.ShowSettingsPage(tag == "Specific"
                ? MacroSettingsPage.Specific
                : MacroSettingsPage.General);
        }
    }

    /// <summary>
    /// Takes the key that arms or disarms this macro. The capture is the same one a trigger's key
    /// field uses, so a combination is written the same way; the answer goes to the macro rather
    /// than to the selected trigger entry.
    /// </summary>
    private void OnSetToggleKey(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.BeginKeyCapture(binding =>
            {
                _viewModel.SetToggleKey(binding);
                ResetKeyCaptureLabel();
            }, combination: true, keyboardOnly: true))
        {
            _viewModel.IsCapturingToggleKey = true;
        }
    }

    private void OnAddTriggerMenu(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MacroEditorViewModel viewModel
            && sender is MenuItem { Tag: TriggerKind kind })
        {
            viewModel.AddTriggerCommand.Execute(kind);
        }
    }

    /// <summary>
    /// Reads the controller while a binding is being taken, or stops reading it. A check that opened
    /// the editor without handing a layer in has no controller to read and none is asked for.
    /// </summary>
    private void WatchController(bool watching)
    {
        if (!watching || _devices is not { } layer)
        {
            _padTimer?.Stop();
            _padTimer = null;
            _padHeld.Take(GamepadButtons.None);
            return;
        }

        if (_padTimer is not null)
        {
            return;
        }

        // Whatever is held when the capture starts is not a press. The keyboard path only sees keys
        // going down after the button was pressed, and a controller button that is already down is
        // the same "already there" as a key somebody is resting a finger on.
        try
        {
            _padHeld.Take(layer.GamepadReading.Held());
        }
        catch (Exception)
        {
            _padHeld.Take(GamepadButtons.None);
        }

        // The pace the triggers read it at: quicker than a hand presses one button twice, and one
        // reading of a controller is a fraction of the cost of one look at a pixel.
        _padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _padTimer.Tick += (_, _) => ReadController(layer);
        _padTimer.Start();
    }

    /// <summary>
    /// One reading of the controller, for the timer and for the checks. The first button found going
    /// down is the binding and ends the capture, the way the first key or mouse button does.
    /// </summary>
    internal void LookAtController()
    {
        if (_devices is { } layer)
        {
            ReadController(layer);
        }
    }

    private void ReadController(IDeviceLayer layer)
    {
        if (!_viewModel.IsCapturingKey || _viewModel.CaptureKeyboardOnly)
        {
            return;
        }

        GamepadButtons held;
        try
        {
            held = layer.GamepadReading.Held();
        }
        catch (Exception)
        {
            // A controller that cannot be read is a controller with nothing to say; the next reading
            // tries again rather than the timer dying.
            return;
        }

        foreach (var (button, down) in _padHeld.Changed(held))
        {
            var name = down ? GamepadNames.Binding(button) : string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            // A controller button is one name, so the modifiers of a combination being built do not
            // go on — the same as a binding taken from the wheel or a mouse button.
            _captureModifiers.Clear();
            _viewModel.CaptureKey(name);
            WatchController(false);
            return;
        }
    }

    /// <summary>Takes one notch of the wheel as the trigger while the editor is waiting for one.</summary>
    private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel || !viewModel.IsCapturingKey)
        {
            return;
        }

        // A key field cannot hold a wheel notch; swallow the wheel so it does not scroll the
        // form while the field waits for a key.
        if (viewModel.CaptureKeyboardOnly)
        {
            e.Handled = true;
            return;
        }

        // The pointer reports the tilt the other way round from the input device, so the sign
        // is flipped here and the shared naming decides which way that is.
        var (axis, rotation) = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? (HookWheelAxis.Horizontal, e.Delta.X < 0 ? 1 : -1)
            : (HookWheelAxis.Vertical, e.Delta.Y >= 0 ? 1 : -1);

        // A wheel binding is one name, so the modifiers of a combination being built do not go on.
        _captureModifiers.Clear();
        viewModel.CaptureKey(KeyNames.WheelName(axis, rotation));
        e.Handled = true;
    }

    /// <summary>Takes a mouse button as the trigger while the editor is waiting for one.</summary>
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel || !viewModel.IsCapturingKey)
        {
            return;
        }

        // The capture button itself has to stay clickable: it is how a mouse ends a capture.
        // Every other click is swallowed, so it cannot bind a mouse button or move the focus.
        if (viewModel.CaptureKeyboardOnly)
        {
            if (e.Source is Visual source
                && source.FindAncestorOfType<Button>(true) is { } toggle
                && toggle.Classes.Contains("CaptureToggle"))
            {
                return;
            }

            e.Handled = true;
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

        // A mouse binding is one name, so the modifiers of a combination being built do not go on.
        _captureModifiers.Clear();
        viewModel.CaptureKey(KeyNames.MouseName(pressed));
        e.Handled = true;
    }

    /// <summary>
    /// Lets go of a modifier that nothing followed — which makes it the binding, the way a trigger
    /// bound to Right Shift reads — and swallows the releases that follow a captured press. A
    /// focused button reads Space or Enter on release as a click, which would arm the very capture
    /// the user just finished.
    /// </summary>
    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is MacroEditorViewModel viewModel && viewModel.IsCapturingKey
            && HotkeyNameMapper.IsModifier(e.Key))
        {
            if (_captureModifiers.Remove(e.Key) && _captureModifiers.Count == 0)
            {
                viewModel.CaptureKey(DescribeKey(e.Key));
            }

            // Either it was part of a combination still being built, or it was already down when
            // the capture started; neither is what finishes the binding.
            e.Handled = true;
            return;
        }

        if (_swallowReleases.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsCapturingHotstringEnd)
        {
            if (e.Key == Key.Escape)
            {
                viewModel.StopHotstringEndCapture();
                _captureModifiers.Clear();
                e.Handled = true;
                return;
            }

            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                if (!_captureModifiers.Contains(e.Key))
                {
                    _captureModifiers.Add(e.Key);
                }

                e.Handled = true;
                return;
            }

            var character = HotstringKeyCapture.Character(e.Key, e.KeyModifiers);
            viewModel.CaptureHotstringEnd(character);
            if (character is not null)
            {
                _swallowReleases.Clear();
                _swallowReleases.AddRange(_captureModifiers);
                _swallowReleases.Add(e.Key);
                _captureModifiers.Clear();
            }

            e.Handled = true;
            return;
        }

        if (viewModel.IsCapturingKey)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                // Held back rather than taken: Ctrl, Shift, Alt and Win may be the front of a
                // combination, and which it was is only known once the capture ends.
                if (!_captureModifiers.Contains(e.Key))
                {
                    _captureModifiers.Add(e.Key);
                }

                e.Handled = true;
                return;
            }

            // A single key field takes the key itself; a combination keeps the modifiers held
            // at the moment it was pressed, the way a trigger is written.
            var captured = viewModel.CaptureSingleKey
                ? DescribeKey(e.Key)
                : Chord(e.Key, Held(e));
            viewModel.CaptureKey(captured);
            _swallowReleases.Clear();
            _swallowReleases.AddRange(_captureModifiers);
            _swallowReleases.Add(e.Key);
            _captureModifiers.Clear();
            e.Handled = true;
            return;
        }

        // The screen pickers are reachable from anywhere in the editor, including while a
        // coordinate or the hex colour is being typed, so they answer before the text rule below.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.X:
                    e.Handled = true;
                    CaptureCursorPosition(viewModel);
                    return;
                case Key.C:
                    e.Handled = true;
                    CaptureCursorColour(viewModel);
                    return;
            }
        }

        // Never take a shortcut while the user is typing into a field.
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(true) is not null)
        {
            return;
        }

        // 设置里写的是"开始/停止"两半：结束那一次由录制器自己接住（它按全局钩子听），开始这一次
        // 只有窗口知道，所以在这里接。
        if (!viewModel.IsRecording
            && Chord(e.Key, e.KeyModifiers) is { Length: > 0 } pressed
            && string.Equals(pressed, Localization.LocalSettings.LoadRecordStopKey(),
                StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            viewModel.ToggleRecordCommand.Execute(null);
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (control)
        {
            switch (e.Key)
            {
                case Key.A:
                    e.Handled = true;
                    SelectAllSteps();
                    return;
                case Key.C:
                    e.Handled = true;
                    _ = CopySelectionAsync();
                    return;
                case Key.X:
                    e.Handled = true;
                    _ = CutSelectionAsync();
                    return;
                case Key.V:
                    e.Handled = true;
                    _ = PasteAsync();
                    return;
                case Key.Z:
                    e.Handled = true;
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        viewModel.RedoCommand.Execute(null);
                    }
                    else
                    {
                        viewModel.UndoCommand.Execute(null);
                    }

                    return;
                case Key.Y:
                    e.Handled = true;
                    viewModel.RedoCommand.Execute(null);
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Insert:
                viewModel.NewStepCommand.Execute(null);
                break;
            case Key.F2:
                viewModel.EditSelectedCommand.Execute(null);
                break;
            case Key.Delete when !control:
                viewModel.DeleteSelectedCommand.Execute(null);
                break;
            case Key.G when control:
                viewModel.GroupSelectedCommand.Execute(null);
                break;
            case Key.E when control:
                viewModel.ToggleStepEnabledCommand.Execute(null);
                break;
            case Key.Up when control:
                viewModel.MoveSelectedUpCommand.Execute(null);
                break;
            case Key.Down when control:
                viewModel.MoveSelectedDownCommand.Execute(null);
                break;
            case Key.Right when control:
                Fold(true);
                break;
            case Key.Left when control:
                Fold(false);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
