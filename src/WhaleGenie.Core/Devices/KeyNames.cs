using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The names a key travels under: a name a macro writes turned into the code the machine reports,
/// and a code back into the name.
/// </summary>
/// <remarks>
/// One vocabulary, asked in one place: the pickers, the recorder, the triggers and the input devices
/// all come through here, so a key is called the same thing wherever it is shown or stored. It
/// speaks <see cref="HookKey"/> and <see cref="HookCode"/> rather than the hook library's own types,
/// which keeps "which library reads the keyboard" the engine's business alone.
/// </remarks>
public static class KeyNames
{
    /// <summary>Everyday spellings, mapped onto the code the hook expects.</summary>
    private static readonly Dictionary<string, HookCode> Spoken = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = HookCode.LeftControl,
        ["control"] = HookCode.LeftControl,
        ["leftctrl"] = HookCode.LeftControl,
        ["lctrl"] = HookCode.LeftControl,
        ["左ctrl"] = HookCode.LeftControl,
        ["rightctrl"] = HookCode.RightControl,
        ["rctrl"] = HookCode.RightControl,
        ["右ctrl"] = HookCode.RightControl,
        ["shift"] = HookCode.LeftShift,
        ["leftshift"] = HookCode.LeftShift,
        ["lshift"] = HookCode.LeftShift,
        ["左shift"] = HookCode.LeftShift,
        ["rightshift"] = HookCode.RightShift,
        ["rshift"] = HookCode.RightShift,
        ["右shift"] = HookCode.RightShift,
        ["alt"] = HookCode.LeftAlt,
        ["leftalt"] = HookCode.LeftAlt,
        ["lalt"] = HookCode.LeftAlt,
        ["左alt"] = HookCode.LeftAlt,
        ["rightalt"] = HookCode.RightAlt,
        ["ralt"] = HookCode.RightAlt,
        ["右alt"] = HookCode.RightAlt,
        ["win"] = HookCode.LeftMeta,
        ["windows"] = HookCode.LeftMeta,
        ["meta"] = HookCode.LeftMeta,
        ["super"] = HookCode.LeftMeta,
        ["leftwin"] = HookCode.LeftMeta,
        ["lwin"] = HookCode.LeftMeta,
        ["左win"] = HookCode.LeftMeta,
        ["rightwin"] = HookCode.RightMeta,
        ["rwin"] = HookCode.RightMeta,
        ["右win"] = HookCode.RightMeta,
        ["esc"] = HookCode.Escape,
        ["escape"] = HookCode.Escape,
        ["return"] = HookCode.Enter,
        ["spacebar"] = HookCode.Space,
        ["pgup"] = HookCode.PageUp,
        ["pageup"] = HookCode.PageUp,
        ["pgdn"] = HookCode.PageDown,
        ["pagedown"] = HookCode.PageDown,
        ["del"] = HookCode.Delete,
        ["ins"] = HookCode.Insert,
        ["plus"] = HookCode.Equals,
        ["-"] = HookCode.Minus,
        ["="] = HookCode.Equals,
        [","] = HookCode.Comma,
        ["."] = HookCode.Period,
        ["/"] = HookCode.Slash,
        [";"] = HookCode.Semicolon,
        ["'"] = HookCode.Quote,
        ["`"] = HookCode.BackQuote,
        ["["] = HookCode.OpenBracket,
        ["]"] = HookCode.CloseBracket,
        ["\\"] = HookCode.Backslash,
    };

    /// <summary>
    /// Every key name a macro may write, in the order a picker reads them, so a key can be chosen
    /// rather than remembered. A modifier is listed by the spelling a chord uses and again with
    /// each side named, because both are written by macros: a chord reads better short, while a
    /// trigger is bound to one particular key and the two shift keys are two different keys.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = ChooseNames();

    /// <summary>The code for a key name, or null when the name means nothing.</summary>
    public static HookCode? ResolveCode(string? name)
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

        // The name itself covers the letters, the function keys and every spelled-out key
        // ("Enter", "PageUp", "NumPad5"). A digit is the one key whose name cannot be written as
        // itself in C#, so it is spelled "Digit1" in the enum and "1" in a macro.
        var spelled = text.Length == 1 && char.IsAsciiDigit(text[0]) ? "Digit" + text : text;
        return Enum.TryParse<HookCode>(spelled, ignoreCase: true, out var code)
               && code is not HookCode.Undefined
            ? code
            : null;
    }

    /// <summary>Splits a chord such as <c>Ctrl+Shift+S</c> into codes.</summary>
    public static IReadOnlyList<HookCode> ResolveChordCodes(string? chord, out string? unknown)
    {
        var keys = new List<HookCode>();
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
    /// The other names one key arrives under, for the keys whose name the keyboard's own state
    /// decides. With NumLock off the number pad is the navigation block — the same physical keys,
    /// the same scan codes, and a different name — so a macro that presses NumPad7 is answered by
    /// the hook with Home. Every other key has one name and answers with nothing here.
    /// </summary>
    /// <remarks>
    /// This is what the input gate needs to know that the Home it has just been handed is its own
    /// NumPad7 rather than a person pressing Home: without it the program takes its own key for
    /// somebody else's, and a shortcut bound to Home — or to any navigation key — is answered by a
    /// macro that is only trying to press the number pad.
    /// </remarks>
    public static IReadOnlyList<string> AlsoSeenAs(HookCode code) => code switch
    {
        HookCode.NumPad0 => ["Insert"],
        HookCode.NumPad1 => ["End"],
        HookCode.NumPad2 => ["Down"],
        HookCode.NumPad3 => ["PageDown"],
        HookCode.NumPad4 => ["Left"],
        HookCode.NumPad5 => ["NumPadClear"],
        HookCode.NumPad6 => ["Right"],
        HookCode.NumPad7 => ["Home"],
        HookCode.NumPad8 => ["Up"],
        HookCode.NumPad9 => ["PageUp"],
        HookCode.NumPadDecimal => ["Delete"],
        _ => [],
    };

    /// <summary>
    /// The name a macro writes for a code, the opposite of <see cref="ResolveCode"/>. Modifiers are
    /// spelled the way a chord spells them, so a recording and a hand-written macro agree.
    /// </summary>
    public static string Name(HookCode code) => Name(code, sided: false);

    /// <summary>
    /// The same name, optionally keeping the left/right word on the modifiers. A trigger binding
    /// is written with its side (<c>右Shift</c>) because the two shift keys are two different
    /// keys; the side-agnostic name stays the default for chords and recordings, where the
    /// shorter spelling reads better.
    /// </summary>
    public static string Name(HookCode code, bool sided) => code switch
    {
        HookCode.Undefined => string.Empty,
        HookCode.LeftControl => sided ? "左Ctrl" : "Ctrl",
        HookCode.RightControl => sided ? "右Ctrl" : "Ctrl",
        HookCode.LeftShift => sided ? "左Shift" : "Shift",
        HookCode.RightShift => sided ? "右Shift" : "Shift",
        HookCode.LeftAlt => sided ? "左Alt" : "Alt",
        HookCode.RightAlt => sided ? "右Alt" : "Alt",
        HookCode.LeftMeta => sided ? "左Win" : "Win",
        HookCode.RightMeta => sided ? "右Win" : "Win",
        HookCode.Escape => "Esc",
        HookCode.Minus => "-",
        HookCode.Equals => "=",
        HookCode.Comma => ",",
        HookCode.Period => ".",
        HookCode.Slash => "/",
        HookCode.Semicolon => ";",
        HookCode.Quote => "'",
        HookCode.BackQuote => "`",
        HookCode.OpenBracket => "[",
        HookCode.CloseBracket => "]",
        HookCode.Backslash => "\\",
        _ => SpokenName(code),
    };

    /// <summary>
    /// The name a macro writes for a mouse button, so a trigger bound to one reads the same
    /// wherever it is shown. Every name carries "Mouse", which is also how the macro list knows
    /// to draw the mouse icon rather than the keyboard one.
    /// </summary>
    public static string MouseName(HookMouseButton button) => button switch
    {
        HookMouseButton.Left => "Mouse Left",
        HookMouseButton.Right => "Mouse Right",
        HookMouseButton.Middle => "Mouse Middle",
        HookMouseButton.Back => "Mouse 4",
        HookMouseButton.Forward => "Mouse 5",
        _ => string.Empty,
    };

    /// <summary>
    /// The name a macro writes for one notch of the wheel. The sign follows the input device, so
    /// a recording and a trigger agree on which way the wheel was turned.
    /// </summary>
    public static string WheelName(HookWheelAxis axis, int rotation) =>
        axis == HookWheelAxis.Horizontal
            ? rotation >= 0 ? "Wheel Left" : "Wheel Right"
            : rotation >= 0 ? "Wheel Up" : "Wheel Down";

    /// <summary>
    /// The key of ours that stands for a code. A key is built here rather than by whoever holds a
    /// code, so the number reaches the rest of the program only through this vocabulary.
    /// </summary>
    public static HookKey Key(HookCode code) => new((int)code);

    /// <summary>The code behind a key of ours.</summary>
    public static HookCode Code(HookKey key) => (HookCode)key.Value;

    /// <summary>The key for a name, or null when the name means nothing.</summary>
    public static HookKey? Resolve(string? name)
        => ResolveCode(name) is { } code ? Key(code) : null;

    /// <summary>Splits a chord such as <c>Ctrl+Shift+S</c> into keys.</summary>
    public static IReadOnlyList<HookKey> ResolveChord(string? chord, out string? unknown)
    {
        var codes = ResolveChordCodes(chord, out unknown);
        var keys = new List<HookKey>(codes.Count);
        foreach (var code in codes)
        {
            keys.Add(Key(code));
        }

        return keys;
    }

    /// <summary>
    /// The name a macro writes for a key, the opposite of <see cref="Resolve"/>. Modifiers are
    /// spelled the way a chord spells them, so a recording and a hand-written macro agree.
    /// </summary>
    public static string Name(HookKey key) => Name(key, sided: false);

    /// <summary>
    /// The same name, optionally keeping the left/right word on the modifiers. A trigger binding
    /// is written with its side (<c>右Shift</c>) because the two shift keys are two different
    /// keys; the side-agnostic name stays the default for chords and recordings, where the
    /// shorter spelling reads better.
    /// </summary>
    public static string Name(HookKey key, bool sided) => Name(Code(key), sided);

    /// <summary>
    /// The other names one key arrives under, so that "is this key our own output" is answered for
    /// the key the window saw rather than for the name the macro wrote. The number pad is the case
    /// that matters: with NumLock off its keys are the navigation block, so pressing NumPad7 is
    /// answered with Home.
    /// </summary>
    public static IReadOnlyList<string> AlsoSeenAs(HookKey key) => AlsoSeenAs(Code(key));

    /// <summary>
    /// The name of a key the switch above does not spell differently: its enum name, except for the
    /// digits, which a macro writes as "0" and the enum cannot name as one.
    /// </summary>
    private static string SpokenName(HookCode code) => code switch
    {
        HookCode.Digit0 => "0",
        HookCode.Digit1 => "1",
        HookCode.Digit2 => "2",
        HookCode.Digit3 => "3",
        HookCode.Digit4 => "4",
        HookCode.Digit5 => "5",
        HookCode.Digit6 => "6",
        HookCode.Digit7 => "7",
        HookCode.Digit8 => "8",
        HookCode.Digit9 => "9",
        _ => code.ToString(),
    };

    private static List<string> ChooseNames()
    {
        var names = new List<string>();
        foreach (var code in Enum.GetValues<HookCode>())
        {
            if (code == HookCode.Undefined)
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
