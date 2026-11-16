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
    internal async void OnOpenHotstringKeyboard(object? sender, RoutedEventArgs e)
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

    internal void OnCaptureHotstringEnd(object? sender, RoutedEventArgs e)
        => _viewModel.BeginHotstringEndCapture();

    internal void OnSetInterfaceHotkey(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.BeginKeyCapture(binding => _viewModel.SetInterfaceToggleHotkey(binding),
                combination: true, keyboardOnly: true))
        {
            _viewModel.IsCapturingInterfaceHotkey = true;
        }
    }

    /// <summary>Switches the settings drawer between its two halves.</summary>
    internal void OnSettingsPageClicked(object? sender, RoutedEventArgs e)
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
    internal void OnSetToggleKey(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.BeginKeyCapture(binding => _viewModel.SetToggleKey(binding),
                combination: true, keyboardOnly: true))
        {
            _viewModel.IsCapturingToggleKey = true;
        }
    }

    internal void OnAddTriggerMenu(object? sender, RoutedEventArgs e)
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
}
