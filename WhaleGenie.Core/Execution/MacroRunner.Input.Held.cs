using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// Holding and letting go: keys and mouse buttons kept down across steps, and where input is sent.
public sealed partial class MacroRunner
{
    /// <summary>
    /// A click worked out after waiting for the screen. The wait that found the place is not input,
    /// so it is not what puts this click in any kind of queue: a run that must not be interrupted
    /// says so by wrapping the stretch in control.holdInput, which is where the one input door is
    /// taken.
    /// </summary>
    private void ClickAt(ExecutableStep step, ScreenPoint at)
    {
        Aim(at);
        Input(step).Click(Button(step), at.X, at.Y, 1, 0);
    }

    /// <summary>Presses a key and remembers it, so the run can let it go if the macro never does.</summary>
    private void HoldKey(ExecutableStep step)
    {
        var device = Input(step);
        var key = Args(step).Text("key");
        device.KeyDown(key);
        if (!_run.HeldKeys.Any(held => Same(held.Device, device) && Same(held.Name, key)))
        {
            _run.HeldKeys.Add((device, key));
        }
    }

    /// <summary>Releases a key the macro asked to release, and stops counting it as held.</summary>
    private void LetGoKey(ExecutableStep step)
    {
        var device = Input(step);
        var key = Args(step).Text("key");
        device.KeyUp(key);
        _run.HeldKeys.RemoveAll(held => Same(held.Device, device) && Same(held.Name, key));
    }

    /// <summary>The same, for a mouse button, remembering where it was pressed.</summary>
    private void HoldButton(ExecutableStep step)
    {
        var device = Input(step);
        var button = Button(step);
        var point = Point(step, "x", "y");
        device.MouseDown(button, point.Device.X, point.Device.Y);
        if (!_run.HeldButtons.Any(held =>
                Same(held.Device, device) && Same(held.Button, button)))
        {
            _run.HeldButtons.Add((device, button, point.Device));
        }
        GivePoints(step, point.Screen, point.Window);
    }

    /// <summary>Releases a mouse button the macro asked to release, wherever it is now held.</summary>
    private void LetGoButton(ExecutableStep step)
    {
        var device = Input(step);
        var button = Button(step);
        var point = Point(step, "x", "y");
        device.MouseUp(button, point.Device.X, point.Device.Y);
        _run.HeldButtons.RemoveAll(held => Same(held.Device, device) && Same(held.Button, button));
        GivePoints(step, point.Screen, point.Window);
    }

    /// <summary>True when two device handles or two names stand for the same thing.</summary>
    private static bool Same(IInputDevice device, IInputDevice other) => ReferenceEquals(device, other);

    private static bool Same(string name, string other)
        => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lets go of anything the macro left pressed. A key or a button held down when a run ends
    /// stays down on the machine until something else takes it up — the user's own typing goes
    /// to the wrong place and the mouse drags whatever it is over — so this happens however the
    /// run ended: finished, stopped, failed or cancelled. A release that fails is logged rather
    /// than raised, because the run already has its answer and a stuck key is not made better by
    /// throwing.
    /// </summary>
    private void LetGoOfHeldInput()
    {
        if (_run.HeldKeys.Count == 0 && _run.HeldButtons.Count == 0 && !_run.GamepadUsed)
        {
            return;
        }

        var keys = _run.HeldKeys.ToList();
        var buttons = _run.HeldButtons.ToList();
        _run.HeldKeys.Clear();
        _run.HeldButtons.Clear();

        var released = 0;
        foreach (var (device, key) in keys)
        {
            try
            {
                device.KeyUp(key);
                released++;
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, LogKeys.LetGoFailed, key, failure.Message);
            }
        }

        foreach (var (device, button, point) in buttons)
        {
            try
            {
                device.MouseUp(button, point.X, point.Y);
                released++;
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, LogKeys.LetGoFailed, button, failure.Message);
            }
        }

        // A controller left holding a button, or a stick pushed over, would keep a game walking
        // into a wall long after the macro that pushed it has finished.
        if (_run.GamepadUsed)
        {
            _run.GamepadUsed = false;
            try
            {
                _devices.Gamepad.ReleaseAll();
                Log(LogLevel.Info, 0, string.Empty, LogKeys.GamepadReleased);
            }
            catch (Exception failure)
            {
                Log(LogLevel.Warn, 0, string.Empty, LogKeys.GamepadLetGoFailed, failure.Message);
            }
        }

        if (released > 0)
        {
            Log(LogLevel.Info, 0, string.Empty, LogKeys.LetGo, released);
        }
    }

}
