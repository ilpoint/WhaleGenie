using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;


namespace WhaleGenie.Platform.Windows;

// The gamepad and the device itself: connecting, its buttons, sticks and triggers.
public sealed partial class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
{
    /// <summary>Closes the connection, which takes the virtual keyboard and mouse off the machine.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _link?.Dispose();
            _link = null;
        }
    }

    /// <summary>
    /// Puts the controller on the machine, and lets go of everything it was holding: a pad that
    /// arrives with a button already down is one no game ever pressed.
    /// </summary>
    public void Connect()
    {
        lock (_gate)
        {
            if (_gamepadOn)
            {
                return;
            }

            _gamepad = GamepadState.Neutral;
            Link().ConnectGamepad();
            _gamepadOn = true;
            PushGamepad();
        }
    }

    /// <summary>Holds a controller button down, or lets it up.</summary>
    public void Button(string button, bool down)
    {
        lock (_gate)
        {
            var flag = GamepadNames.Button(button)
                ?? throw new DeviceActionException(RunKeys.UnknownGamepadButton, button);

            BeginGamepad();
            _gamepad = _gamepad with
            {
                Buttons = down ? _gamepad.Buttons | flag : _gamepad.Buttons & ~flag,
            };

            // A trigger named as a button is the whole pull, or none of it: the pads that report
            // triggers do it by how far they are pulled rather than by a button of their own.
            if (flag == GamepadButtons.LeftTrigger)
            {
                _gamepad = _gamepad with { LeftTrigger = down ? 100 : 0 };
            }

            if (flag == GamepadButtons.RightTrigger)
            {
                _gamepad = _gamepad with { RightTrigger = down ? 100 : 0 };
            }

            PushGamepad();
        }
    }

    /// <summary>Moves one stick to a whole percent from its centre.</summary>
    public void Stick(string stick, int x, int y)
    {
        lock (_gate)
        {
            BeginGamepad();
            var (sideways, upwards) = (Least(x), Least(y));
            var which = (stick ?? string.Empty).Trim().ToLowerInvariant();

            _gamepad = which switch
            {
                "left" => _gamepad with { LeftX = sideways, LeftY = upwards },
                "right" => _gamepad with { RightX = sideways, RightY = upwards },
                _ => throw new DeviceActionException(RunKeys.UnknownGamepadStick, which),
            };

            PushGamepad();
        }
    }

    /// <summary>Pulls one trigger by a whole percent.</summary>
    public void Trigger(string trigger, int amount)
    {
        lock (_gate)
        {
            BeginGamepad();
            var pulled = Math.Clamp(amount, 0, 100);
            var which = (trigger ?? string.Empty).Trim().ToLowerInvariant();

            _gamepad = which switch
            {
                "left" => _gamepad with { LeftTrigger = pulled },
                "right" => _gamepad with { RightTrigger = pulled },
                _ => throw new DeviceActionException(RunKeys.UnknownGamepadTrigger, which),
            };

            PushGamepad();
        }
    }

    /// <summary>Lets go of everything, which is what the end of a run leaves behind it.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            if (!_gamepadOn)
            {
                return;
            }

            _gamepad = GamepadState.Neutral;
            PushGamepad();
        }
    }
}
