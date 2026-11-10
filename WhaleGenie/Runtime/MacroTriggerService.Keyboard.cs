using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Runtime;

// Keys and the mouse: chords, hotstrings, the menu launcher, and starting from a trigger entry.
public sealed partial class MacroTriggerService
{
    private void OnKeyDown(KeyHookEvent e)
    {
        RunGuarded("hook.keydown", () =>
        {
            if (!Listening)
            {
                return;
            }

            var name = KeyNames.Name(e.Key, sided: true);
            if (TryMenuLauncher(name, pressed: true))
            {
                e.Suppress = true;
                return;
            }

            if (!e.Injected && TryHotstring(e))
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
            if (Listening)
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
        lock (_keys)
        {
            if (pressed)
            {
                _heldKeys.Add(name);
            }
            else
            {
                _heldKeys.Remove(name);
            }
        }

        if (pressed && name is "Esc" or "Escape" or "Left" or "Right" or "Up" or "Down"
            or "左" or "右" or "上" or "下"
            or "Home" or "End" or "PageUp" or "PageDown")
        {
            _hotstrings.Reset();
        }

        Fire(name, pressed);
    }

    /// <summary>True while a key press should be looked at.</summary>
    private bool Listening => _enabled && !_disposed && !MacroTriggerGate.IsOpen;

    /// <summary>A mouse button answers a macro the same way a key does.</summary>
    private void OnMouseDown(MouseHookEvent e)
    {
        RunGuarded("hook.mousedown", () =>
        {
            if (!Listening)
            {
                return;
            }

            var button = KeyNames.MouseName(e.Button);
            if (button.Length == 0)
            {
                return;
            }

            lock (_keys)
            {
                _heldKeys.Add(button);
            }

            if (MouseButtonResetsHotstring(e.Button))
            {
                _hotstrings.Reset();
            }

            if ((e.Button is HookMouseButton.Middle
                    or HookMouseButton.Back or HookMouseButton.Forward)
                && TryMenuLauncher(button, pressed: true))
            {
                e.Suppress = true;
                return;
            }

            Fire(button, pressed: true);
        });
    }

    private void OnMouseUp(MouseHookEvent e)
    {
        RunGuarded("hook.mouseup", () =>
        {
            if (!Listening)
            {
                return;
            }

            var button = KeyNames.MouseName(e.Button);
            if (button.Length == 0)
            {
                return;
            }

            lock (_keys)
            {
                _heldKeys.Remove(button);
            }

            Fire(button, pressed: false);
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
            if (!Listening)
            {
                return;
            }

            var notch = KeyNames.WheelName(e.Axis, e.Rotation);
            Fire(notch, pressed: true);
            Fire(notch, pressed: false);
        });
    }

    private void Fire(string key, bool pressed)
    {
        // A key WhaleGenie is sending itself is its own output, not the user asking for something.
        if (WhaleGenieInputGate.RecentlySent(key))
        {
            return;
        }

        foreach (var entry in TriggerSnapshot().Entries)
        {
            if (!entry.IsEnabled || entry.Kind != TriggerKind.Key
                || MacroFor(entry) is not { IsEnabled: true } macro)
            {
                continue;
            }

            if (!Binds(entry.BindKey, key))
            {
                continue;
            }

            if (pressed)
            {
                Pressed(entry, macro);
            }
            else
            {
                Released(entry, macro);
            }
        }
    }

    /// <summary>Runs the input cleanup for one typed-string hit and starts its macro once.</summary>
    private bool TryHotstring(KeyHookEvent e)
    {
        var held = HeldKeys();
        if (held.Any(key => key is "左Ctrl" or "右Ctrl" or "Ctrl"
                or "左Alt" or "右Alt" or "Alt"
                or "左Win" or "右Win" or "Win"))
        {
            _hotstrings.Reset();
            return false;
        }

        var typed = HotstringText.Read(e.Key, held);
        if (typed.Text.Length == 0)
        {
            return false;
        }

        var hit = _hotstrings.Press(typed.Text, typed.EndKey,
            TriggerSnapshot().HotstringEntries());
        if (hit is null || MacroFor(hit.Entry) is not { IsEnabled: true } macro)
        {
            return false;
        }

        try
        {
            if (hit.Backspaces > 0)
            {
                for (var index = 0; index < hit.Backspaces; index++)
                {
                    _devices.Input.KeyPress("Backspace", 0);
                }
            }

            if (hit.ReplayEndingKey)
            {
                if (hit.EndingText is "\n" or "\t")
                {
                    _devices.Input.KeyPress(hit.EndingKey, 0);
                }
                else
                {
                    _devices.Input.TypeText(hit.EndingText, 0);
                }
            }

            e.Suppress = hit.SuppressEndingKey;
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("trigger.hotstring-input", error);
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Let the cleanup and the ending key reach the foreground control before the macro
                // starts sending its own input.
                await Task.Delay(1);
                StartFrom(hit.Entry, macro, repeating: false);
            }
            catch (Exception error)
            {
                AppDiagnostics.Record("trigger.hotstring-start", error);
            }
        });
        return true;
    }

    internal void ResetTextRecognition() => _hotstrings.Reset();

    internal static bool MouseButtonResetsHotstring(HookMouseButton button)
        => button is HookMouseButton.Left or HookMouseButton.Right;

    private IReadOnlyList<string> HeldKeys()
    {
        lock (_keys)
        {
            return [.. _heldKeys];
        }
    }

    private bool TryMenuLauncher(string binding, bool pressed)
    {
        if (!pressed || binding.Length == 0)
        {
            return false;
        }

        var launcher = TriggerSnapshot().MenuLauncher;
        var launch = launcher.Length > 0 && Binds(launcher, binding);
        if (launch)
        {
            MenuRequested?.Invoke();
        }

        return launch;
    }

    internal bool RequestMenuForCheck(string binding) => TryMenuLauncher(binding, pressed: true);

    // ---------------------------------------------------------------- triggers

    /// <summary>
    /// Whether a macro's binding answers to a key. A binding may name a combination, in which
    /// case the modifiers have to be down for it to count.
    /// </summary>
    private bool Binds(string binding, string key)
    {
        lock (_keys)
        {
            return Binds(binding, key, _heldKeys);
        }
    }

    /// <summary>
    /// Whether a binding answers to a key that was just pressed, with the named modifiers down.
    /// The words the binding is asked about are plain names, so this is where the two sides of a
    /// modifier and the older side-agnostic spelling are sorted out.
    /// </summary>
    internal static bool Binds(string binding, string key, IEnumerable<string> held)
    {
        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Same(parts[^1], key))
        {
            return false;
        }

        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!held.Any(heldKey => Same(parts[index], heldKey)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two key names. The key picker spells the top row the way the interface library
    /// does (<c>D7</c>) while the hook calls the same key <c>7</c>, and a binding says which
    /// shift it means (<c>右Shift</c>) unless it was written before the sides were told apart.
    /// </summary>
    internal static bool Same(string? first, string? second)
    {
        var left = Alias(first);
        var right = Alias(second);

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A binding of just "Shift" is the older, side-agnostic spelling: it answers to whichever
        // shift is pressed, so the macros written before the sides existed keep working.
        return StandsFor(left, right) || StandsFor(right, left);
    }

    /// <summary>Whether a bare modifier name means the side the other name spells out.</summary>
    private static bool StandsFor(string bare, string sided)
        => Sides.TryGetValue(bare, out var sides)
           && sides.Contains(sided, StringComparer.OrdinalIgnoreCase);

    /// <summary>The halves a bare modifier name stands for.</summary>
    private static readonly Dictionary<string, string[]> Sides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = ["左ctrl", "右ctrl"],
        ["control"] = ["左ctrl", "右ctrl"],
        ["shift"] = ["左shift", "右shift"],
        ["alt"] = ["左alt", "右alt"],
        ["win"] = ["左win", "右win"],
        ["windows"] = ["左win", "右win"],
        ["meta"] = ["左win", "右win"],
        ["super"] = ["左win", "右win"],
    };

    /// <summary>Trims a name to the spelling the comparisons use.</summary>
    private static string Alias(string? name)
    {
        var text = (name ?? string.Empty).Trim();

        // The key picker spells the top row the way the interface library does (D7), while the
        // hook calls the same key 7.
        if (text.Length == 2 && (text[0] is 'D' or 'd') && char.IsAsciiDigit(text[1]))
        {
            return text[1..];
        }

        // The sided names are written with Chinese words in the interface and with English words
        // in hand-written macros; both have to compare as the same key.
        return text.ToLowerInvariant() switch
        {
            "leftctrl" or "leftcontrol" or "lctrl" => "左ctrl",
            "rightctrl" or "rightcontrol" or "rctrl" => "右ctrl",
            "leftshift" or "lshift" => "左shift",
            "rightshift" or "rshift" => "右shift",
            "leftalt" or "lalt" => "左alt",
            "rightalt" or "ralt" => "右alt",
            "leftwin" or "leftmeta" or "lwin" => "左win",
            "rightwin" or "rightmeta" or "rwin" => "右win",
            _ => text.ToLowerInvariant(),
        };
    }

    private void Pressed(TriggerEntry entry, MacroItem macro)
    {
        switch (entry.LoopMode)
        {
            case MacroLoop.Press:
                StartFrom(entry, macro, repeating: false);
                break;

            case MacroLoop.Hold:
                StartFrom(entry, macro, repeating: true);
                break;

            case MacroLoop.Toggle:
                if (IsRunning(macro))
                {
                    Stop(macro);
                }
                else
                {
                    StartFrom(entry, macro, repeating: true);
                }

                break;
        }
    }

    private void Released(TriggerEntry entry, MacroItem macro)
    {
        switch (entry.LoopMode)
        {
            case MacroLoop.Hold:
                Stop(macro);
                break;

            case MacroLoop.Release:
                StartFrom(entry, macro, repeating: false);
                break;
        }
    }
}
