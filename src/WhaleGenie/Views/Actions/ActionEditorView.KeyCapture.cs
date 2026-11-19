using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

// The keyboard and the gamepad: the pad on screen, capturing a combination, and clamping it.
public partial class ActionEditorView : UserControl
{
    /// <summary>
    /// Opens the keyboard drawn on screen and keeps the key whose cap was clicked. A key is a name
    /// the macro has to spell exactly, so it is taken off a picture of a keyboard rather than out
    /// of a list of names.
    /// </summary>
    internal void OnOpenKeyPad(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            OpenKeyPadFor(parameter);
        }
    }

    /// <summary>
    /// Starts or stops taking the next key pressed on the keyboard into one field. Clicking it
    /// again is how a capture that was started by mistake is called off: a key field may hold Esc,
    /// so Esc is a key like any other here rather than the way out.
    /// </summary>
    internal void OnCaptureKey(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var wasWaiting = ReferenceEquals(_capturingField, parameter);
        EndKeyCapture();
        if (wasWaiting)
        {
            return;
        }

        _capturingField = parameter;
        parameter.IsCapturingKey = true;
    }

    /// <summary>
    /// Gives the key just pressed to whatever asked for it. A single key field holds one key, so
    /// the key pressed is the whole answer and the capture is over. A combination and a press of a
    /// run are built a key at a time — the way the keyboard drawn on screen builds them — so every
    /// key pressed joins what is already there and the capture stays up until it is stopped.
    /// </summary>
    private void TakeKey(string name, string chord)
    {
        if (_capturingRow is { } row)
        {
            row.AddKey(chord);
            return;
        }

        if (_capturingField is not { } field)
        {
            return;
        }

        if (field.IsKeys)
        {
            field.AddKey(chord);
            return;
        }

        field.Text = name;
        EndKeyCapture();
    }

    /// <summary>
    /// Stops waiting for a key and puts the button that asked back to its resting label. What is
    /// left in the way of swallowed releases stays: the press that ended the capture still has to
    /// come up without the focused button reading it as a click.
    /// </summary>
    private void EndKeyCapture()
    {
        _captureModifiers.Clear();

        if (_capturingField is { } field)
        {
            field.IsCapturingKey = false;
            _capturingField = null;
        }

        if (_capturingRow is { } row)
        {
            row.IsCapturing = false;
            _capturingRow = null;
        }
    }

    /// <summary>
    /// A key went down while a field or a run was waiting for it. The whole keyboard belongs to the
    /// capture for that moment, so every key is taken here rather than reaching the form.
    /// </summary>
    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingField is null && _capturingRow is null)
        {
            return;
        }

        // A capture that keeps taking keys is ended by the key that means stop, because every key
        // pressed belongs to what is being written. A single key field is answered by the key
        // itself, so Esc is a key there like any other.
        if (e.Key is Key.Escape && (_capturingRow is not null || _capturingField is { IsKeys: true }))
        {
            EndKeyCapture();
            e.Handled = true;
            return;
        }

        if (HotkeyNameMapper.IsModifier(e.Key))
        {
            // Held back rather than taken: Ctrl, Shift, Alt and Win are the front of a combination
            // more often than they are the key itself, and which it was is only known once the
            // capture ends.
            if (!_captureModifiers.Contains(e.Key))
            {
                _captureModifiers.Add(e.Key);
            }

            e.Handled = true;
            return;
        }

        var pressed = new List<Key>(_captureModifiers) { e.Key };
        TakeKey(HotkeyNameMapper.Name(e.Key), HotkeyNameMapper.Chord(e.Key, HeldKey(e)));

        // What was held has been written into the combination, so it starts again from nothing:
        // letting go of those keys must not add them a second time.
        _captureModifiers.Clear();
        _swallowReleases.Clear();
        _swallowReleases.AddRange(pressed);
        e.Handled = true;
    }

    /// <summary>
    /// Lets go of a modifier that nothing followed, which makes it the key that was picked — a
    /// field can hold Right Shift, and that is the only way it can be pressed — and swallows the
    /// releases that follow a captured press.
    /// </summary>
    private void OnCaptureKeyUp(object? sender, KeyEventArgs e)
    {
        if ((_capturingField is not null || _capturingRow is not null)
            && HotkeyNameMapper.IsModifier(e.Key)
            && _captureModifiers.Remove(e.Key)
            && _captureModifiers.Count == 0)
        {
            var name = HotkeyNameMapper.Name(e.Key);
            TakeKey(name, name);
        }

        if (_swallowReleases.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Everything held when a key was pressed: what the event carries, plus the modifiers this form
    /// saw go down, so a modifier pressed before the capture started still counts.
    /// </summary>
    private KeyModifiers HeldKey(KeyEventArgs e)
    {
        var held = e.KeyModifiers;
        foreach (var modifier in _captureModifiers)
        {
            held |= HotkeyNameMapper.ModifierFlag(modifier);
        }

        return held;
    }

    /// <summary>
    /// Opens the controller drawn on screen and keeps the control that was clicked. Which question
    /// the pad answers — a button, a stick or a trigger — follows from the parameter it was opened
    /// from, because that is what the step is asking for.
    /// </summary>
    internal async void OnOpenGamepadPad(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var pick = parameter.Definition.Name switch
        {
            "stick" => GamepadPick.Stick,
            "trigger" => GamepadPick.Trigger,
            _ => GamepadPick.Button,
        };

        if (await VirtualGamepadWindow.PickAsync(HostWindow!, pick) is { Length: > 0 } value)
        {
            parameter.Choose(value);
        }
    }
}
