using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// Taking one binding: a key, a mouse button, a wheel notch or a controller button, plus the
/// modifiers held with it. The editor keeps the shortcuts that do not depend on a capture, so this
/// class only answers "is this event part of the binding being taken, and was it consumed".
/// </summary>
/// <remarks>
/// A modifier is held back rather than taken: Ctrl may be the front of a combination, and which it
/// was is known only when the capture ends. A lone modifier that nothing follows is a binding of
/// its own, and its release is what settles it. The releases of the keys that made the binding are
/// swallowed afterwards so a focused button does not read Space or Enter as a click.
/// </remarks>
internal sealed class EditorBindingCapture(
    Window host,
    Func<MacroEditorViewModel?> viewModel,
    IDeviceLayer? devices)
{
    private readonly List<Key> _modifiers = [];
    private readonly List<Key> _swallowReleases = [];
    private readonly GamepadButtonWatch _held = new();
    private DispatcherTimer? _timer;

    /// <summary>
    /// Starts a capture with nothing held: whatever the last one took is over by now, and a key
    /// still in the swallow list would eat the next press for no reason.
    /// </summary>
    public void Reset()
    {
        _modifiers.Clear();
        _swallowReleases.Clear();
    }

    /// <summary>Takes one notch of the wheel as the trigger while the editor waits for one.</summary>
    public bool TakeWheel(PointerWheelEventArgs e)
    {
        if (viewModel() is not { IsCapturingKey: true } model)
        {
            return false;
        }

        // A key field cannot hold a wheel notch; swallow the wheel so it does not scroll the form
        // while the field waits for a key.
        if (model.CaptureKeyboardOnly)
        {
            return true;
        }

        // The pointer reports the tilt the other way round from the input device, so the sign is
        // flipped here and the shared naming decides which way that is.
        var (axis, rotation) = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? (HookWheelAxis.Horizontal, e.Delta.X < 0 ? 1 : -1)
            : (HookWheelAxis.Vertical, e.Delta.Y >= 0 ? 1 : -1);

        // A wheel binding is one name, so the modifiers of a combination being built do not go on.
        _modifiers.Clear();
        model.CaptureKey(KeyNames.WheelName(axis, rotation));
        return true;
    }

    /// <summary>Takes a mouse button as the trigger while the editor waits for one.</summary>
    public bool TakePointer(PointerPressedEventArgs e)
    {
        if (viewModel() is not { IsCapturingKey: true } model)
        {
            return false;
        }

        // The capture button itself has to stay clickable: it is how a mouse ends a capture. Every
        // other click is swallowed, so it cannot bind a mouse button or move the focus.
        if (model.CaptureKeyboardOnly)
        {
            if (e.Source is Visual source
                && source.FindAncestorOfType<Button>(true) is { } toggle
                && toggle.Classes.Contains("CaptureToggle"))
            {
                return false;
            }

            return true;
        }

        var button = e.GetCurrentPoint(host).Properties.PointerUpdateKind switch
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
            return false;
        }

        // A mouse binding is one name, so the modifiers of a combination being built do not go on.
        _modifiers.Clear();
        model.CaptureKey(KeyNames.MouseName(pressed));
        return true;
    }

    /// <summary>
    /// Takes the key half of a capture: the end of a typed-string trigger, or a binding. False when
    /// no capture is up, which is where the editor's own shortcuts begin.
    /// </summary>
    public bool TakeKeyDown(KeyEventArgs e)
    {
        if (viewModel() is not { } model)
        {
            return false;
        }

        if (model.IsCapturingHotstringEnd)
        {
            if (e.Key == Key.Escape)
            {
                model.StopHotstringEndCapture();
                _modifiers.Clear();
                return true;
            }

            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                if (!_modifiers.Contains(e.Key))
                {
                    _modifiers.Add(e.Key);
                }

                return true;
            }

            var character = HotstringKeyCapture.Character(e.Key, e.KeyModifiers);
            model.CaptureHotstringEnd(character);
            if (character is not null)
            {
                _swallowReleases.Clear();
                _swallowReleases.AddRange(_modifiers);
                _swallowReleases.Add(e.Key);
                _modifiers.Clear();
            }

            return true;
        }

        if (!model.IsCapturingKey)
        {
            return false;
        }

        if (HotkeyNameMapper.IsModifier(e.Key))
        {
            // Held back rather than taken: Ctrl, Shift, Alt and Win may be the front of a
            // combination, and which it was is only known once the capture ends.
            if (!_modifiers.Contains(e.Key))
            {
                _modifiers.Add(e.Key);
            }

            return true;
        }

        // A single key field takes the key itself; a combination keeps the modifiers held at the
        // moment it was pressed, the way a trigger is written.
        var captured = model.CaptureSingleKey
            ? DescribeKey(e.Key)
            : Chord(e.Key, Held(e));
        model.CaptureKey(captured);
        _swallowReleases.Clear();
        _swallowReleases.AddRange(_modifiers);
        _swallowReleases.Add(e.Key);
        _modifiers.Clear();
        return true;
    }

    /// <summary>
    /// Lets go of a modifier that nothing followed — which makes it the binding, the way a trigger
    /// bound to Right Shift reads — and swallows the releases that follow a captured press. A
    /// focused button reads Space or Enter on release as a click, which would arm the very capture
    /// the user just finished.
    /// </summary>
    public bool TakeKeyUp(KeyEventArgs e)
    {
        if (viewModel() is { IsCapturingKey: true } model
            && HotkeyNameMapper.IsModifier(e.Key))
        {
            if (_modifiers.Remove(e.Key) && _modifiers.Count == 0)
            {
                model.CaptureKey(DescribeKey(e.Key));
            }

            // Either it was part of a combination still being built, or it was already down when
            // the capture started; neither is what finishes the binding.
            return true;
        }

        return _swallowReleases.Remove(e.Key);
    }

    /// <summary>
    /// Reads the controller while a binding is being taken, or stops reading it. A check that
    /// opened the editor without handing a layer in has no controller to read and none is asked for.
    /// </summary>
    public void Watch(bool watching)
    {
        if (!watching || devices is not { } layer)
        {
            _timer?.Stop();
            _timer = null;
            _held.Take(GamepadButtons.None);
            return;
        }

        if (_timer is not null)
        {
            return;
        }

        // Whatever is held when the capture starts is not a press. The keyboard path only sees keys
        // going down after the button was pressed, and a controller button that is already down is
        // the same "already there" as a key somebody is resting a finger on.
        try
        {
            _held.Take(layer.GamepadReading.Held());
        }
        catch (Exception)
        {
            _held.Take(GamepadButtons.None);
        }

        // The pace the triggers read it at: quicker than a hand presses one button twice, and one
        // reading of a controller is a fraction of the cost of one look at a pixel.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Read(layer);
        _timer.Start();
    }

    /// <summary>
    /// One reading of the controller, for the timer and for the checks. The first button found going
    /// down is the binding and ends the capture, the way the first key or mouse button does.
    /// </summary>
    internal void LookAtController()
    {
        if (devices is { } layer)
        {
            Read(layer);
        }
    }

    private void Read(IDeviceLayer layer)
    {
        if (viewModel() is not { IsCapturingKey: true, CaptureKeyboardOnly: false } model)
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

        foreach (var (button, down) in _held.Changed(held))
        {
            var name = down ? GamepadNames.Binding(button) : string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            // A controller button is one name, so the modifiers of a combination being built do not
            // go on — the same as a binding taken from the wheel or a mouse button.
            _modifiers.Clear();
            model.CaptureKey(name);
            Watch(false);
            return;
        }
    }

    /// <summary>
    /// Everything held when a key was pressed: what the event itself carries, plus the modifiers
    /// this window saw go down, so a modifier pressed before the capture started still counts.
    /// </summary>
    private KeyModifiers Held(KeyEventArgs e)
    {
        var held = e.KeyModifiers;
        foreach (var modifier in _modifiers)
        {
            held |= HotkeyNameMapper.ModifierFlag(modifier);
        }

        return held;
    }

    /// <summary>
    /// The binding a key press makes: the modifiers held at that moment, then the key itself. Each
    /// modifier is written without its side — either Ctrl answers it — which is how a recorded
    /// combination is written too; a lone modifier, where the two sides are two different keys,
    /// keeps its side in <see cref="DescribeKey"/>.
    /// </summary>
    private static string Chord(Key key, KeyModifiers held) => HotkeyNameMapper.Chord(key, held);

    private static string DescribeKey(Key key) => HotkeyNameMapper.Name(key);
}
