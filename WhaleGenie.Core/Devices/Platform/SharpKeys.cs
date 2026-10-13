using System;
using System.Collections.Generic;
using SharpHook.Data;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The names a key travels under, seen from the machine's side: a name a macro writes turned into
/// the code the hook library sends, and a code the hook reports turned back into the name.
/// </summary>
/// <remarks>
/// The library's own spelling of a code stops here: everything outside the engine's device code
/// asks <see cref="KeyNames"/>, which speaks <see cref="HookKey"/> and the two enums beside it. The
/// two halves have to agree on names, so they share this one table rather than keeping a copy each.
/// </remarks>
internal static class SharpKeys
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

    /// <summary>
    /// Every key name a macro may write, in the order a picker reads them, so a key can be chosen
    /// rather than remembered. A modifier is listed by the spelling a chord uses and again with
    /// each side named, because both are written by macros: a chord reads better short, while a
    /// trigger is bound to one particular key and the two shift keys are two different keys.
    /// </summary>
    internal static IReadOnlyList<string> Names { get; } = ChooseNames();

    /// <summary>The code for a key name, or null when the name means nothing.</summary>
    internal static KeyCode? ResolveCode(string? name)
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

    /// <summary>Splits a chord such as <c>Ctrl+Shift+S</c> into codes.</summary>
    internal static List<KeyCode> ResolveChordCodes(string? chord, out string? unknown)
    {
        var keys = new List<KeyCode>();
        foreach (var part in (chord ?? string.Empty)
                     .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = ResolveCode(part);
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
    /// The name a macro writes for a code, the opposite of <see cref="ResolveCode"/>. Modifiers are
    /// spelled the way a chord spells them, so a recording and a hand-written macro agree.
    /// </summary>
    internal static string Name(KeyCode code) => Name(code, sided: false);

    /// <summary>
    /// The same name, optionally keeping the left/right word on the modifiers. A trigger binding
    /// is written with its side (<c>右Shift</c>) because the two shift keys are two different
    /// keys; the side-agnostic name stays the default for chords and recordings, where the
    /// shorter spelling reads better.
    /// </summary>
    internal static string Name(KeyCode code, bool sided) => code switch
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

    /// <summary>
    /// The name a macro writes for a mouse button, so a trigger bound to one reads the same
    /// wherever it is shown. Every name carries "Mouse", which is also how the macro list knows
    /// to draw the mouse icon rather than the keyboard one.
    /// </summary>
    internal static string MouseName(MouseButton button) => button switch
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
    internal static string WheelName(MouseWheelScrollDirection axis, int rotation) =>
        axis == MouseWheelScrollDirection.Horizontal
            ? rotation >= 0 ? "Wheel Left" : "Wheel Right"
            : rotation >= 0 ? "Wheel Up" : "Wheel Down";

    /// <summary>The key of ours that stands for a code.</summary>
    internal static HookKey Key(KeyCode code) => new((int)code);

    /// <summary>The code behind a key of ours.</summary>
    internal static KeyCode Code(HookKey key) => (KeyCode)key.Value;

    /// <summary>Our name for a button, or null for one the hook reported that has no name here.</summary>
    internal static HookMouseButton? Mouse(MouseButton button) => button switch
    {
        MouseButton.Button1 => HookMouseButton.Left,
        MouseButton.Button2 => HookMouseButton.Right,
        MouseButton.Button3 => HookMouseButton.Middle,
        MouseButton.Button4 => HookMouseButton.Back,
        MouseButton.Button5 => HookMouseButton.Forward,
        _ => null,
    };

    /// <summary>The button the library sends for one of ours.</summary>
    internal static MouseButton Button(HookMouseButton button) => button switch
    {
        HookMouseButton.Right => MouseButton.Button2,
        HookMouseButton.Middle => MouseButton.Button3,
        HookMouseButton.Back => MouseButton.Button4,
        HookMouseButton.Forward => MouseButton.Button5,
        _ => MouseButton.Button1,
    };

    /// <summary>Our name for the way round a wheel is set.</summary>
    internal static HookWheelAxis Axis(MouseWheelScrollDirection axis) =>
        axis == MouseWheelScrollDirection.Horizontal ? HookWheelAxis.Horizontal : HookWheelAxis.Vertical;

    /// <summary>The way round the library spells a wheel that is set one of our ways.</summary>
    internal static MouseWheelScrollDirection Direction(HookWheelAxis axis) =>
        axis == HookWheelAxis.Horizontal
            ? MouseWheelScrollDirection.Horizontal
            : MouseWheelScrollDirection.Vertical;

    /// <summary>Drops the "Vc" the enum names every key with.</summary>
    private static string Unprefixed(KeyCode code)
    {
        var text = code.ToString();
        return text.StartsWith("Vc", StringComparison.Ordinal) && text.Length > 2 ? text[2..] : text;
    }

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
}
