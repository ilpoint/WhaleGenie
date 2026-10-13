using SharpHook.Data;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Turns the code the hook library speaks into the virtual key code Windows posts. The two
/// vocabularies number nearly every key differently — "A" is one number to the library and another
/// to Windows — so a code from one side sent as the other presses a different key, and nothing in
/// the program notices: the window simply receives a key nobody asked for.
/// </summary>
/// <remarks>
/// A macro's key name is resolved to a library code by <see cref="SharpKeys"/>, which is the one
/// place that knows how a name is spelled; this is the rest of that journey, from the code to what
/// Windows presses. The pairs are the ones the library publishes for Windows, which come from the
/// system's own list of virtual key codes. A key the library has and Windows has not is answered
/// with null rather than with a neighbouring key, so a step can say which key it could not press.
/// </remarks>
internal static class WindowsKeys
{
    /// <summary>The virtual key code of F1, which the rest of that row follows.</summary>
    private const int VkF1 = 0x70;

    /// <summary>The virtual key code for a library code, or null when Windows has no such key.</summary>
    internal static ushort? VirtualKey(KeyCode code) => code switch
    {
        // The letters, the digits and the function keys each sit in one run on both sides, so their
        // place in the run is what carries over; the digits run 1 to 9 and then 0.
        >= KeyCode.VcA and <= KeyCode.VcZ => (ushort)('A' + ((int)code - (int)KeyCode.VcA)),
        >= KeyCode.Vc1 and <= KeyCode.Vc9 => (ushort)('1' + ((int)code - (int)KeyCode.Vc1)),
        KeyCode.Vc0 => '0',
        >= KeyCode.VcF1 and <= KeyCode.VcF24 => (ushort)(VkF1 + ((int)code - (int)KeyCode.VcF1)),

        KeyCode.VcEscape => 0x1B,
        KeyCode.VcBackQuote => 0xC0,
        KeyCode.VcMinus => 0xBD,
        KeyCode.VcEquals => 0xBB,
        KeyCode.VcBackspace => 0x08,
        KeyCode.VcTab => 0x09,
        KeyCode.VcCapsLock => 0x14,
        KeyCode.VcOpenBracket => 0xDB,
        KeyCode.VcCloseBracket => 0xDD,
        KeyCode.VcBackslash => 0xDC,
        KeyCode.VcSemicolon => 0xBA,
        KeyCode.VcQuote => 0xDE,
        KeyCode.VcEnter => 0x0D,
        KeyCode.VcComma => 0xBC,
        KeyCode.VcPeriod => 0xBE,
        KeyCode.VcSlash => 0xBF,
        KeyCode.VcSpace => 0x20,

        // The two keys Windows names for the keyboard a machine happens to have: the section sign
        // and the key the library calls "misc" are the extra keys a non-English layout carries.
        KeyCode.VcSection => 0xE2,
        KeyCode.VcMisc => 0xDF,

        KeyCode.VcPrintScreen => 0x2C,
        KeyCode.VcScrollLock => 0x91,
        KeyCode.VcPause => 0x13,
        KeyCode.VcCancel => 0x03,
        KeyCode.VcHelp => 0x2F,
        KeyCode.VcInsert => 0x2D,
        KeyCode.VcDelete => 0x2E,
        KeyCode.VcHome => 0x24,
        KeyCode.VcEnd => 0x23,
        KeyCode.VcPageUp => 0x21,
        KeyCode.VcPageDown => 0x22,
        KeyCode.VcUp => 0x26,
        KeyCode.VcLeft => 0x25,
        KeyCode.VcRight => 0x27,
        KeyCode.VcDown => 0x28,

        // The number pad: its digits sit in a run of their own, and its operators are separate keys
        // from the ones above the letters, so each is named on its own.
        >= KeyCode.VcNumPad1 and <= KeyCode.VcNumPad9 =>
            (ushort)(0x61 + ((int)code - (int)KeyCode.VcNumPad1)),
        KeyCode.VcNumPad0 => 0x60,
        KeyCode.VcNumLock => 0x90,
        KeyCode.VcNumPadClear => 0x0C,
        KeyCode.VcNumPadDivide => 0x6F,
        KeyCode.VcNumPadMultiply => 0x6A,
        KeyCode.VcNumPadSubtract => 0x6D,
        KeyCode.VcNumPadAdd => 0x6B,
        KeyCode.VcNumPadDecimal => 0x6E,
        KeyCode.VcNumPadSeparator => 0x6C,
        KeyCode.VcNumPadEquals => 0x92,

        // The number pad's Enter carries the same code as the main one and is told apart by the
        // extended flag, which is what the posting device's scan code lookup and IsExtended are for.
        KeyCode.VcNumPadEnter => 0x0D,

        // The two halves of each modifier are two different keys to Windows, which is what makes a
        // chord written with a named half arrive as that half.
        KeyCode.VcLeftShift => 0xA0,
        KeyCode.VcRightShift => 0xA1,
        KeyCode.VcLeftControl => 0xA2,
        KeyCode.VcRightControl => 0xA3,
        KeyCode.VcLeftAlt => 0xA4,
        KeyCode.VcRightAlt => 0xA5,
        KeyCode.VcLeftMeta => 0x5B,
        KeyCode.VcRightMeta => 0x5C,
        KeyCode.VcContextMenu => 0x5D,
        KeyCode.VcSleep => 0x5F,

        KeyCode.VcMediaPlay => 0xB3,
        KeyCode.VcMediaStop => 0xB2,
        KeyCode.VcMediaPrevious => 0xB1,
        KeyCode.VcMediaNext => 0xB0,
        KeyCode.VcMediaSelect => 0xB5,
        KeyCode.VcVolumeMute => 0xAD,
        KeyCode.VcVolumeDown => 0xAE,
        KeyCode.VcVolumeUp => 0xAF,
        KeyCode.VcApp1 => 0xB6,
        KeyCode.VcApp2 => 0xB7,
        KeyCode.VcAppMail => 0xB4,
        KeyCode.VcBrowserHome => 0xAC,
        KeyCode.VcBrowserBack => 0xA6,
        KeyCode.VcBrowserForward => 0xA7,
        KeyCode.VcBrowserStop => 0xA9,
        KeyCode.VcBrowserRefresh => 0xA8,
        KeyCode.VcBrowserSearch => 0xAA,
        KeyCode.VcBrowserFavorites => 0xAB,

        // The keys a Japanese or Korean keyboard has for its own script. Windows has no code for
        // the kana pairs and only one for the kana key itself, which is the one named here.
        KeyCode.VcKana => 0x15,
        KeyCode.VcHanja => 0x19,
        KeyCode.VcJunja => 0x17,
        KeyCode.VcFinal => 0x18,
        KeyCode.VcAccept => 0x1E,
        KeyCode.VcConvert => 0x1C,
        KeyCode.VcNonConvert => 0x1D,
        KeyCode.VcImeOn => 0x16,
        KeyCode.VcImeOff => 0x1A,
        KeyCode.VcModeChange => 0x1F,
        KeyCode.VcProcess => 0xE5,

        // Keys the library has a code for and Windows has not: a macro that names one of these gets
        // a step that says which key it could not press, rather than a different key being pressed.
        KeyCode.VcFunction => null,
        KeyCode.VcChangeInputSource => null,
        KeyCode.VcPower => null,
        KeyCode.VcMediaEject => null,
        KeyCode.VcApp3 => null,
        KeyCode.VcApp4 => null,
        KeyCode.VcAppBrowser => null,
        KeyCode.VcAppCalculator => null,
        KeyCode.VcKatakanaHiragana => null,
        KeyCode.VcKatakana => null,
        KeyCode.VcHiragana => null,
        KeyCode.VcAlphanumeric => null,
        KeyCode.VcUnderscore => null,
        KeyCode.VcYen => null,
        KeyCode.VcJpComma => null,

        _ => null,
    };
}
