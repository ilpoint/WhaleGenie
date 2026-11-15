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
        var key = ReadText(step, "key");
        device.KeyDown(key);
        if (!_heldKeys.Any(held => Same(held.Device, device) && Same(held.Name, key)))
        {
            _heldKeys.Add((device, key));
        }
    }

    /// <summary>Releases a key the macro asked to release, and stops counting it as held.</summary>
    private void LetGoKey(ExecutableStep step)
    {
        var device = Input(step);
        var key = ReadText(step, "key");
        device.KeyUp(key);
        _heldKeys.RemoveAll(held => Same(held.Device, device) && Same(held.Name, key));
    }

    /// <summary>The same, for a mouse button, remembering where it was pressed.</summary>
    private void HoldButton(ExecutableStep step)
    {
        var device = Input(step);
        var button = Button(step);
        var point = Point(step, "x", "y");
        device.MouseDown(button, point.Device.X, point.Device.Y);
        if (!_heldButtons.Any(held =>
                Same(held.Device, device) && Same(held.Button, button)))
        {
            _heldButtons.Add((device, button, point.Device));
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
        _heldButtons.RemoveAll(held => Same(held.Device, device) && Same(held.Button, button));
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
        if (_heldKeys.Count == 0 && _heldButtons.Count == 0 && !_gamepadUsed)
        {
            return;
        }

        var keys = _heldKeys.ToList();
        var buttons = _heldButtons.ToList();
        _heldKeys.Clear();
        _heldButtons.Clear();

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
        if (_gamepadUsed)
        {
            _gamepadUsed = false;
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

    /// <summary>Where a step wants its input to go. Anything unset means the front window.</summary>
    private InputRoute Route(ExecutableStep step)
    {
        var mode = DeliveryMode(step);
        if (mode == DriverMode)
        {
            return new InputRoute(InputDelivery.Driver, 0);
        }

        if (mode != BackgroundMode)
        {
            return InputRoute.Front;
        }

        // Posting messages needs a window to post them at. The step names it the way the window
        // actions name one — the text held up against the window the way it says to hold it up —
        // so a step that types into a window whose title moves with the document is not left
        // without a handle on it.
        var rule = WindowRuleOf(step);
        if (rule.MatchValue.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingTargetWindow);
        }

        var window = _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                         rule.Options)
                     ?? throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);

        return new InputRoute(InputDelivery.Background, window.Handle);
    }

    /// <summary>What a step's input mode says it wants, as the file writes it.</summary>
    private static string DeliveryMode(ExecutableStep step)
        => step.Text("inputMode").Trim().ToLowerInvariant();

    /// <summary>
    /// Whether this step's input is posted at a window as messages rather than driving the desktop.
    /// The two things this mode cannot do — report where the pointer is, and move it — are asked
    /// about before the window is looked up, so a step that cannot work is turned down on the
    /// strength of its own settings rather than of what happens to be open.
    /// </summary>
    private static bool PostsMessages(ExecutableStep step) => DeliveryMode(step) == BackgroundMode;

    /// <summary>What the input mode is written as when the messages are posted at a window.</summary>
    private const string BackgroundMode = "background";

    /// <summary>What the input mode is written as when a virtual device sends the input.</summary>
    private const string DriverMode = "driver";

    /// <summary>
    /// The corner a step's numbers are measured from, or <c>null</c> when they are screen pixels
    /// already. It is looked up afresh every time the step runs, which is what keeps a macro that
    /// names a window or a control pointing at the same place after the window has been dragged
    /// elsewhere or the list under the control has been scrolled.
    /// </summary>
    private ScreenPoint? Anchor(ExecutableStep step) => AnchorOf(step).Corner;
}
