using System;
using System.Collections.Generic;
using SharpHook.Data;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Turns the key names a macro writes into the codes the platform sends, and back again, so the
/// interface and the hook always agree on what a key is called.
/// </summary>
public static class KeyNames
{
    /// <summary>Everyday spellings, mapped onto the code the hook expects.</summary>
    private static readonly Dictionary<string, KeyCode> Spoken = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = KeyCode.VcLeftControl,
        ["control"] = KeyCode.VcLeftControl,
        ["leftctrl"] = KeyCode.VcLeftControl,
        ["lctrl"] = KeyCode.VcLeftControl,
        ["左ctrl"] = KeyCode.VcLeftControl,
        ["rightctrl"] = KeyCode.VcRightControl,
        ["rctrl"] = KeyCode.VcRightControl,
        ["右ctrl"] = KeyCode.VcRightControl,
        ["shift"] = KeyCode.VcLeftShift,
        ["leftshift"] = KeyCode.VcLeftShift,
        ["lshift"] = KeyCode.VcLeftShift,
        ["左shift"] = KeyCode.VcLeftShift,
        ["rightshift"] = KeyCode.VcRightShift,
        ["rshift"] = KeyCode.VcRightShift,
        ["右shift"] = KeyCode.VcRightShift,
        ["alt"] = KeyCode.VcLeftAlt,
        ["leftalt"] = KeyCode.VcLeftAlt,
        ["lalt"] = KeyCode.VcLeftAlt,
        ["左alt"] = KeyCode.VcLeftAlt,
        ["rightalt"] = KeyCode.VcRightAlt,
        ["ralt"] = KeyCode.VcRightAlt,
        ["右alt"] = KeyCode.VcRightAlt,
        ["win"] = KeyCode.VcLeftMeta,
        ["windows"] = KeyCode.VcLeftMeta,
        ["meta"] = KeyCode.VcLeftMeta,
        ["super"] = KeyCode.VcLeftMeta,
        ["leftwin"] = KeyCode.VcLeftMeta,
        ["lwin"] = KeyCode.VcLeftMeta,
        ["左win"] = KeyCode.VcLeftMeta,
        ["rightwin"] = KeyCode.VcRightMeta,
        ["rwin"] = KeyCode.VcRightMeta,
        ["右win"] = KeyCode.VcRightMeta,
        ["esc"] = KeyCode.VcEscape,
        ["escape"] = KeyCode.VcEscape,
        ["return"] = KeyCode.VcEnter,
        ["spacebar"] = KeyCode.VcSpace,
        ["pgup"] = KeyCode.VcPageUp,
        ["pageup"] = KeyCode.VcPageUp,
        ["pgdn"] = KeyCode.VcPageDown,
        ["pagedown"] = KeyCode.VcPageDown,
        ["del"] = KeyCode.VcDelete,
        ["ins"] = KeyCode.VcInsert,
        ["plus"] = KeyCode.VcEquals,
        ["-"] = KeyCode.VcMinus,
        ["="] = KeyCode.VcEquals,
        [","] = KeyCode.VcComma,
        ["."] = KeyCode.VcPeriod,
        ["/"] = KeyCode.VcSlash,
        [";"] = KeyCode.VcSemicolon,
        ["'"] = KeyCode.VcQuote,
        ["`"] = KeyCode.VcBackQuote,
        ["["] = KeyCode.VcOpenBracket,
        ["]"] = KeyCode.VcCloseBracket,
        ["\\"] = KeyCode.VcBackslash,
    };

    /// <summary>The code for a key name, or null when the name means nothing.</summary>
    public static KeyCode? Resolve(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (Spoken.TryGetValue(text, out var spoken))
        {
            return spoken;
        }

        // "Vc" plus the name covers the letters, the digits, the function keys and every
        // spelled-out key ("Enter", "PageUp", "NumPad5").
        return Enum.TryParse<KeyCode>("Vc" + text, ignoreCase: true, out var code)
               && code is not KeyCode.VcUndefined
            ? code
            : null;
    }

    /// <summary>
    /// Every key name a macro may write, in the order a picker reads them, so a key can be chosen
    /// rather than remembered. A modifier is listed by the spelling a chord uses and again with
    /// each side named, because both are written by macros: a chord reads better short, while a
    /// trigger is bound to one particular key and the two shift keys are two different keys.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = ChooseNames();

    private static List<string> ChooseNames()
    {
        var names = new List<string>();
        foreach (var code in Enum.GetValues<KeyCode>())
        {
            if (code == KeyCode.VcUndefined)
            {
                continue;
            }

            // A key that is not a modifier is named the same either way, so the second pass only
            // ever adds the left and right spellings.
            foreach (var name in new[] { Name(code), Name(code, sided: true) })
            {
                if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>Splits a chord such as <c>Ctrl+Shift+S</c> into codes.</summary>
    public static List<KeyCode> ResolveChord(string? chord, out string? unknown)
    {
        var keys = new List<KeyCode>();
        foreach (var part in (chord ?? string.Empty)
                     .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = Resolve(part);
            if (code is null)
            {
                unknown = part;
                return keys;
            }

            keys.Add(code.Value);
        }

        unknown = null;
        return keys;
    }

    /// <summary>
    /// The name a macro writes for a code, the opposite of <see cref="Resolve"/>. Modifiers are
    /// spelled the way a chord spells them, so a recording and a hand-written macro agree.
    /// </summary>
    public static string Name(KeyCode code) => Name(code, sided: false);

    /// <summary>
    /// The same name, optionally keeping the left/right word on the modifiers. A trigger binding
    /// is written with its side (<c>右Shift</c>) because the two shift keys are two different
    /// keys; the side-agnostic name stays the default for chords and recordings, where the
    /// shorter spelling reads better.
    /// </summary>
    public static string Name(KeyCode code, bool sided) => code switch
    {
        KeyCode.VcUndefined => string.Empty,
        KeyCode.VcLeftControl => sided ? "左Ctrl" : "Ctrl",
        KeyCode.VcRightControl => sided ? "右Ctrl" : "Ctrl",
        KeyCode.VcLeftShift => sided ? "左Shift" : "Shift",
        KeyCode.VcRightShift => sided ? "右Shift" : "Shift",
        KeyCode.VcLeftAlt => sided ? "左Alt" : "Alt",
        KeyCode.VcRightAlt => sided ? "右Alt" : "Alt",
        KeyCode.VcLeftMeta => sided ? "左Win" : "Win",
        KeyCode.VcRightMeta => sided ? "右Win" : "Win",
        KeyCode.VcEscape => "Esc",
        KeyCode.VcMinus => "-",
        KeyCode.VcEquals => "=",
        KeyCode.VcComma => ",",
        KeyCode.VcPeriod => ".",
        KeyCode.VcSlash => "/",
        KeyCode.VcSemicolon => ";",
        KeyCode.VcQuote => "'",
        KeyCode.VcBackQuote => "`",
        KeyCode.VcOpenBracket => "[",
        KeyCode.VcCloseBracket => "]",
        KeyCode.VcBackslash => "\\",
        _ => Unprefixed(code),
    };

    /// <summary>Drops the "Vc" the enum names every key with.</summary>
    private static string Unprefixed(KeyCode code)
    {
        var text = code.ToString();
        return text.StartsWith("Vc", StringComparison.Ordinal) && text.Length > 2 ? text[2..] : text;
    }

    /// <summary>
    /// The name a macro writes for a mouse button, so a trigger bound to one reads the same
    /// wherever it is shown. Every name carries "Mouse", which is also how the macro list knows
    /// to draw the mouse icon rather than the keyboard one.
    /// </summary>
    public static string MouseName(MouseButton button) => button switch
    {
        MouseButton.Button1 => "Mouse Left",
        MouseButton.Button2 => "Mouse Right",
        MouseButton.Button3 => "Mouse Middle",
        MouseButton.Button4 => "Mouse 4",
        MouseButton.Button5 => "Mouse 5",
        _ => string.Empty,
    };

    /// <summary>
    /// The name a macro writes for one notch of the wheel. The sign follows the input device, so
    /// a recording and a trigger agree on which way the wheel was turned.
    /// </summary>
    public static string WheelName(MouseWheelScrollDirection axis, short rotation) =>
        axis == MouseWheelScrollDirection.Horizontal
            ? rotation >= 0 ? "Wheel Left" : "Wheel Right"
            : rotation >= 0 ? "Wheel Up" : "Wheel Down";
}
