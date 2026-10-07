using SharpHook.Data;
using Viiper.Client.Devices.Keyboard;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Turns the keys a macro names into the codes a virtual USB keyboard sends. Viktor names its keys
/// after the platform's own codes and the device wants HID usage IDs, so this is where the two
/// vocabularies meet; a key with no code on the other side is refused rather than guessed at.
/// </summary>
public static class ViiperKeys
{
    /// <summary>
    /// The modifier a code stands for, which travels in the report's modifier byte rather than in
    /// its list of held keys. Anything else is an ordinary key.
    /// </summary>
    public static Mod? Modifier(KeyCode code) => code switch
    {
        KeyCode.VcLeftControl => Mod.LeftCtrl,
        KeyCode.VcRightControl => Mod.RightCtrl,
        KeyCode.VcLeftShift => Mod.LeftShift,
        KeyCode.VcRightShift => Mod.RightShift,
        KeyCode.VcLeftAlt => Mod.LeftAlt,
        KeyCode.VcRightAlt => Mod.RightAlt,
        KeyCode.VcLeftMeta => Mod.LeftGUI,
        KeyCode.VcRightMeta => Mod.RightGUI,
        _ => null,
    };

    /// <summary>
    /// The HID usage ID a key sends, or null when a virtual keyboard has no such key. The letters,
    /// the digits and the function keys are contiguous in both vocabularies, so they are worked out
    /// from where they sit in their range; the rest are named one by one, since the two sides
    /// disagree about almost everything else ("Esc" is Escape, the number pad is Kp, and so on).
    /// </summary>
    public static Key? Usage(KeyCode code) => code switch
    {
        >= KeyCode.VcA and <= KeyCode.VcZ => (Key)((int)Key.A + ((int)code - (int)KeyCode.VcA)),

        // The digits run 1 to 9 and then 0, so that is the way round the codes go.
        >= KeyCode.Vc1 and <= KeyCode.Vc9 => (Key)((int)Key.Num1 + ((int)code - (int)KeyCode.Vc1)),
        KeyCode.Vc0 => Key.Num0,

        // The first twelve function keys sit together, and the rest sit at the end of the device's
        // list, past the number pad.
        >= KeyCode.VcF1 and <= KeyCode.VcF12 => (Key)((int)Key.F1 + ((int)code - (int)KeyCode.VcF1)),
        >= KeyCode.VcF13 and <= KeyCode.VcF24 => (Key)((int)Key.F13 + ((int)code - (int)KeyCode.VcF13)),

        KeyCode.VcEscape => Key.Escape,
        KeyCode.VcBackspace => Key.Backspace,
        KeyCode.VcTab => Key.Tab,
        KeyCode.VcSpace => Key.Space,
        KeyCode.VcEnter => Key.Enter,
        KeyCode.VcCapsLock => Key.CapsLock,
        KeyCode.VcMinus => Key.Minus,
        KeyCode.VcEquals => Key.Equal,
        KeyCode.VcOpenBracket => Key.LeftBrace,
        KeyCode.VcCloseBracket => Key.RightBrace,
        KeyCode.VcBackslash => Key.Backslash,
        KeyCode.VcSemicolon => Key.Semicolon,
        KeyCode.VcQuote => Key.Apostrophe,
        KeyCode.VcBackQuote => Key.Grave,
        KeyCode.VcComma => Key.Comma,
        KeyCode.VcPeriod => Key.Period,
        KeyCode.VcSlash => Key.Slash,

        KeyCode.VcPrintScreen => Key.PrintScreen,
        KeyCode.VcScrollLock => Key.ScrollLock,
        KeyCode.VcPause => Key.Pause,
        KeyCode.VcInsert => Key.Insert,
        KeyCode.VcDelete => Key.Delete,
        KeyCode.VcHome => Key.Home,
        KeyCode.VcEnd => Key.End,
        KeyCode.VcPageUp => Key.PageUp,
        KeyCode.VcPageDown => Key.PageDown,
        KeyCode.VcUp => Key.Up,
        KeyCode.VcDown => Key.Down,
        KeyCode.VcLeft => Key.Left,
        KeyCode.VcRight => Key.Right,
        KeyCode.VcContextMenu => Key.Application,
        KeyCode.VcHelp => Key.Help,
        KeyCode.VcPower => Key.Power,

        KeyCode.VcNumLock => Key.NumLock,
        KeyCode.VcNumPadDivide => Key.KpSlash,
        KeyCode.VcNumPadMultiply => Key.KpAsterisk,
        KeyCode.VcNumPadSubtract => Key.KpMinus,
        KeyCode.VcNumPadAdd => Key.KpPlus,
        KeyCode.VcNumPadEnter => Key.KpEnter,
        KeyCode.VcNumPadDecimal => Key.KpDot,
        KeyCode.VcNumPadEquals => Key.KpEqual,
        >= KeyCode.VcNumPad1 and <= KeyCode.VcNumPad9 =>
            (Key)((int)Key.Kp1 + ((int)code - (int)KeyCode.VcNumPad1)),
        KeyCode.VcNumPad0 => Key.Kp0,

        KeyCode.VcVolumeMute => Key.Mute,
        KeyCode.VcVolumeDown => Key.VolumeDown,
        KeyCode.VcVolumeUp => Key.VolumeUp,
        KeyCode.VcMediaPlay => Key.MediaPlayPause,
        KeyCode.VcMediaStop => Key.MediaStop,
        KeyCode.VcMediaNext => Key.MediaNext,
        KeyCode.VcMediaPrevious => Key.MediaPrevious,

        _ => null,
    };

    /// <summary>
    /// What one character of typed text needs from the keyboard, or null when a keyboard cannot
    /// produce it at all. Text arrives as characters and a keyboard sends keys, so the character
    /// has to be looked up on the layout a keyboard has — the same one the client library keeps.
    /// </summary>
    public static (Key Key, bool Shift)? Typing(char character)
        => character <= 0xFF && CharToKey.TryGetValue((byte)character, out var key)
            ? (key, ShiftChars.ContainsKey((byte)character))
            : null;
}
