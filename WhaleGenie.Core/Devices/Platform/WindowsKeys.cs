using System.Runtime.InteropServices;
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
///
/// A key Windows numbers but gives no scan code is answered with null as well: an input message
/// carries a scan code, so a key without one is a key no keystroke can carry, and pressing it
/// either sends nothing or sends a different key than the name says.
///
/// Whether a key can be sent at all is a question per way of sending, and the three ways answer
/// differently: injecting a keystroke asks <see cref="CanBePressed"/>, a posted key message only
/// needs a Windows code (<see cref="VirtualKey"/>, because the window is told about the key rather
/// than given a keystroke), and the virtual USB keyboard speaks HID usages of its own and asks
/// nothing here. Pause is where that shows: it is one key press like any other, it is only that
/// Windows will not say which scan code goes with it.
/// </remarks>
internal static class WindowsKeys
{
    /// <summary>The virtual key code of F1, which the rest of that row follows.</summary>
    private const int VkF1 = 0x70;

    /// <summary>The virtual key code of Pause, the one key whose scan code is written down here.</summary>
    private const ushort VkPause = 0x13;

    /// <summary>
    /// Whether one keystroke can carry the key, which is the question the injecting device asks: a
    /// key press is a Windows key code and a scan code, so a key with either missing cannot be one.
    /// Pause is the key this used to be asked about, and the answer has changed: what a keyboard
    /// sends for it is the three-byte sequence E1 1D 45, which a single <c>SendInput</c> event
    /// cannot express — but the E1 1D prefix is consumed by the keyboard driver, and what an
    /// application is handed is VK_PAUSE with scan code 0x45. Stating that pair is therefore the
    /// key press the window was going to see anyway, which is why <see cref="ScanCode"/> states it
    /// rather than taking Windows' word for it (Windows answers 0, because Pause has no entry in
    /// the one-scan-code-per-key table).
    /// </summary>
    internal static bool CanBePressed(KeyCode code)
        => VirtualKey(code) is { } virtualKey && ScanCode(virtualKey) != 0;

    /// <summary>MAPVK_VK_TO_VSC, which answers with 0 for a key no keyboard can send.</summary>
    private const uint VkToScan = 0;

    /// <summary>Pause's scan code, which Windows will not hand out but every window is told about.</summary>
    private const ushort ScanPause = 0x45;

    /// <summary>
    /// The scan code of a key, or 0 for one no keyboard can send. It goes out beside the Windows
    /// key code because a keystroke carries both, and the pair is what tells two keys apart: the
    /// number pad's 7 and Home are one scan code, and the extended flag is what says which of them
    /// was meant.
    /// </summary>
    /// <remarks>
    /// Pause is the one key that has to be written down here: its scan code is 0x45, the same byte
    /// NumLock's is, and Windows answers 0 for it because the two are told apart by the E1 prefix
    /// and the extended flag rather than by the table.
    /// </remarks>
    internal static ushort ScanCode(ushort virtualKey)
        => virtualKey == VkPause
            ? ScanPause
            : (ushort)(MapVirtualKey(virtualKey, VkToScan) & 0xFF);

    /// <summary>
    /// The keys that carry the extended flag, which is what tells them apart from the keys they
    /// share a scan code with. NumLock is the one that matters most in practice: its scan code
    /// 0x45 is the same byte Pause is built from (E1 1D 45), so a NumLock sent without the flag
    /// arrives as a key nobody asked for — every browser calls it Pause — while the Windows side
    /// still flips the lock. The arrows and the navigation block share their scan codes with the
    /// number pad the same way.
    /// </summary>
    internal static bool IsExtended(ushort virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    /// <summary>
    /// Whether the key a macro named carries the flag, which the Windows key code alone cannot
    /// answer: the number pad's Enter and the Enter under Backspace are both key code 0x0D and both
    /// scan code 0x1C, and the flag is the only thing that says which was meant. So the answer is
    /// read from the key that was written, and only the keys that have a key code of their own fall
    /// back to the table above.
    /// </summary>
    internal static bool IsExtended(KeyCode code) => code switch
    {
        KeyCode.VcNumPadEnter => true,
        KeyCode.VcEnter => false,
        _ => VirtualKey(code) is { } virtualKey && IsExtended(virtualKey),
    };

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

        // The key Windows names for the keyboard a machine happens to have: the section sign is the
        // extra key an ISO keyboard carries left of Z. The key the library calls "misc" is another
        // such extra key, and Windows gives it a number without a scan code to send it with, so it
        // is left to the null group below like the rest of the keys no keystroke can carry.
        KeyCode.VcSection => 0xE2,
        KeyCode.VcMisc => null,

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

        // The number pad's separator and its equals key are numbered by Windows but given no scan
        // code, so no keyboard on a machine with an ordinary layout can send them; they are left to
        // the null group below rather than being posted as a key with nothing behind it.
        KeyCode.VcNumPadSeparator => null,
        KeyCode.VcNumPadEquals => null,

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

        // The keys a Japanese or Korean keyboard has for its own script. Windows numbers them, but
        // gives them no scan code on the layouts this program runs on, so nothing a keyboard sends
        // can become one of them: measured, asking the foreground device for one posts VK 0xFF and
        // no key arrives at all. They are therefore answered with null, like every other key no
        // keystroke can carry, rather than being offered as a key that silently does nothing.
        KeyCode.VcKana => null,
        KeyCode.VcHanja => null,
        KeyCode.VcJunja => null,
        KeyCode.VcFinal => null,
        KeyCode.VcAccept => null,
        KeyCode.VcConvert => null,
        KeyCode.VcNonConvert => null,
        KeyCode.VcImeOn => null,
        KeyCode.VcImeOff => null,
        KeyCode.VcModeChange => null,
        KeyCode.VcProcess => null,

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

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
