using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Runtime;

// Keys, the mouse and the wheel, from the hook thread. Nothing here touches an interface object.
public sealed partial class MacroTriggerService
{
    private void OnKeyDown(KeyHookEvent e)
    {
        RunGuarded("hook.keydown", () =>
        {
            if (!Listening || MacroTriggerGate.IsOpen)
            {
                return;
            }

            var name = KeyNames.Name(e.Key, sided: true);
            if (name.Length == 0)
            {
                return;
            }

            if (_hub.Menu(name))
            {
                e.Suppress = true;
                MenuRequested?.Invoke();
                return;
            }

            if (!e.Injected && _hub.Hotstring(DateTime.Now, e))
            {
                return;
            }

            Answer(e.Key, pressed: true);
        });
    }

    private void OnKeyUp(KeyHookEvent e)
    {
        RunGuarded("hook.keyup", () =>
        {
            if (Listening && !MacroTriggerGate.IsOpen)
            {
                Answer(e.Key, pressed: false);
            }
        });
    }

    /// <summary>
    /// A key the hook has seen, answered the way a bound key is answered. Called by the hook's own
    /// two handlers, and by the checks, which need an answer to arrive when they say so rather than
    /// when a keyboard is pressed.
    /// </summary>
    internal void Answer(HookKey key, bool pressed)
    {
        // Bound keys carry their side, so the right Shift and the left one are told apart.
        var name = KeyNames.Name(key, sided: true);
        if (name.Length == 0)
        {
            return;
        }

        // The held keys are what a combination is answered from, so a press is written down before
        // anything is asked and a release is forgotten before it is.
        if (pressed)
        {
            _hub.Hold(name);
            if (ResetsTyped(name))
            {
                _hub.ResetTyped();
            }
        }
        else
        {
            _hub.Release(name);
        }

        _hub.Key(DateTime.Now, name, pressed);
    }

    /// <summary>A mouse button answers a macro the same way a key does.</summary>
    private void OnMouseDown(MouseHookEvent e)
    {
        RunGuarded("hook.mousedown", () =>
        {
            if (!Listening || MacroTriggerGate.IsOpen)
            {
                return;
            }

            var button = KeyNames.MouseName(e.Button);
            if (button.Length == 0)
            {
                return;
            }

            _hub.Hold(button);
            if (MouseButtonResetsHotstring(e.Button))
            {
                _hub.ResetTyped();
            }

            if ((e.Button is HookMouseButton.Middle
                    or HookMouseButton.Back or HookMouseButton.Forward)
                && _hub.Menu(button))
            {
                e.Suppress = true;
                MenuRequested?.Invoke();
                return;
            }

            _hub.Key(DateTime.Now, button, pressed: true);
        });
    }

    private void OnMouseUp(MouseHookEvent e)
    {
        RunGuarded("hook.mouseup", () =>
        {
            if (!Listening || MacroTriggerGate.IsOpen)
            {
                return;
            }

            var button = KeyNames.MouseName(e.Button);
            if (button.Length == 0)
            {
                return;
            }

            _hub.Release(button);
            _hub.Key(DateTime.Now, button, pressed: false);
        });
    }

    /// <summary>
    /// A wheel notch has no "down" state, so it counts as a press that is let go again straight
    /// away. A trigger bound to a wheel fires once per notch, the way a key press does.
    /// </summary>
    private void OnWheel(WheelHookEvent e)
    {
        RunGuarded("hook.wheel", () =>
        {
            if (!Listening || MacroTriggerGate.IsOpen)
            {
                return;
            }

            var notch = KeyNames.WheelName(e.Axis, e.Rotation);
            var now = DateTime.Now;
            _hub.Key(now, notch, pressed: true);
            _hub.Key(now, notch, pressed: false);
        });
    }

    /// <summary>A key that ends a word, so the next character cannot continue the typed string.</summary>
    private static bool ResetsTyped(string name)
        => name is "Esc" or "Escape" or "Left" or "Right" or "Up" or "Down"
            or "左" or "右" or "上" or "下"
            or "Home" or "End" or "PageUp" or "PageDown";

    internal static bool MouseButtonResetsHotstring(HookMouseButton button)
        => button is HookMouseButton.Left or HookMouseButton.Right;
}
