using System.Runtime.InteropServices;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// Turns the code the hook library speaks into the virtual key code Windows posts. The two
/// vocabularies number nearly every key differently — "A" is one number to the library and another
/// to Windows — so a code from one side sent as the other presses a different key, and nothing in
/// the program notices: the window simply receives a key nobody asked for.
/// </summary>
/// <remarks>
/// A macro's key name is resolved to a library code by <see cref="KeyTable"/>, which is the one
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
    internal static bool CanBePressed(HookCode code)
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
    internal static bool IsExtended(HookCode code) => code switch
    {
        HookCode.NumPadEnter => true,
        HookCode.Enter => false,
        _ => VirtualKey(code) is { } virtualKey && IsExtended(virtualKey),
    };

    /// <summary>
    /// The Windows key code for a library key, or null when there is no key a keystroke could carry:
    /// either Windows numbers none, or it numbers one with no scan code here — an IME's own keys, the
    /// number pad's separator and equals, and the library's "misc". Those are answered null rather
    /// than with a neighbouring key, which is what keeps a keystroke from pressing something the
    /// macro did not ask for; the posting device reads this table too, so it refuses them as well.
    /// </summary>
    internal static ushort? VirtualKey(HookCode code) => code switch
    {
        // The letters, the digits and the function keys each sit in one run on both sides, so their
        // place in the run is what carries over; the digits run 1 to 9 and then 0.
        >= HookCode.A and <= HookCode.Z => (ushort)('A' + ((int)code - (int)HookCode.A)),
        >= HookCode.Digit1 and <= HookCode.Digit9 => (ushort)('1' + ((int)code - (int)HookCode.Digit1)),
        HookCode.Digit0 => '0',
        >= HookCode.F1 and <= HookCode.F24 => (ushort)(VkF1 + ((int)code - (int)HookCode.F1)),

        HookCode.Escape => 0x1B,
        HookCode.BackQuote => 0xC0,
        HookCode.Minus => 0xBD,
        HookCode.Equals => 0xBB,
        HookCode.Backspace => 0x08,
        HookCode.Tab => 0x09,
        HookCode.CapsLock => 0x14,
        HookCode.OpenBracket => 0xDB,
        HookCode.CloseBracket => 0xDD,
        HookCode.Backslash => 0xDC,
        HookCode.Semicolon => 0xBA,
        HookCode.Quote => 0xDE,
        HookCode.Enter => 0x0D,
        HookCode.Comma => 0xBC,
        HookCode.Period => 0xBE,
        HookCode.Slash => 0xBF,
        HookCode.Space => 0x20,

        // The key Windows names for the keyboard a machine happens to have: the section sign is the
        // extra key an ISO keyboard carries left of Z. The key the library calls "misc" is another
        // such extra key, and Windows gives it a number without a scan code to send it with, so it
        // is left to the null group below like the rest of the keys no keystroke can carry.
        HookCode.Section => 0xE2,
        HookCode.Misc => null,

        HookCode.PrintScreen => 0x2C,
        HookCode.ScrollLock => 0x91,
        HookCode.Pause => 0x13,
        HookCode.Cancel => 0x03,
        HookCode.Help => 0x2F,
        HookCode.Insert => 0x2D,
        HookCode.Delete => 0x2E,
        HookCode.Home => 0x24,
        HookCode.End => 0x23,
        HookCode.PageUp => 0x21,
        HookCode.PageDown => 0x22,
        HookCode.Up => 0x26,
        HookCode.Left => 0x25,
        HookCode.Right => 0x27,
        HookCode.Down => 0x28,

        // The number pad: its digits sit in a run of their own, and its operators are separate keys
        // from the ones above the letters, so each is named on its own.
        >= HookCode.NumPad1 and <= HookCode.NumPad9 =>
            (ushort)(0x61 + ((int)code - (int)HookCode.NumPad1)),
        HookCode.NumPad0 => 0x60,
        HookCode.NumLock => 0x90,
        HookCode.NumPadClear => 0x0C,
        HookCode.NumPadDivide => 0x6F,
        HookCode.NumPadMultiply => 0x6A,
        HookCode.NumPadSubtract => 0x6D,
        HookCode.NumPadAdd => 0x6B,
        HookCode.NumPadDecimal => 0x6E,

        // The number pad's separator and its equals key are numbered by Windows but given no scan
        // code, so no keyboard on a machine with an ordinary layout can send them; they are left to
        // the null group below rather than being posted as a key with nothing behind it.
        HookCode.NumPadSeparator => null,
        HookCode.NumPadEquals => null,

        // The number pad's Enter carries the same code as the main one and is told apart by the
        // extended flag, which is what the posting device's scan code lookup and IsExtended are for.
        HookCode.NumPadEnter => 0x0D,

        // The two halves of each modifier are two different keys to Windows, which is what makes a
        // chord written with a named half arrive as that half.
        HookCode.LeftShift => 0xA0,
        HookCode.RightShift => 0xA1,
        HookCode.LeftControl => 0xA2,
        HookCode.RightControl => 0xA3,
        HookCode.LeftAlt => 0xA4,
        HookCode.RightAlt => 0xA5,
        HookCode.LeftMeta => 0x5B,
        HookCode.RightMeta => 0x5C,
        HookCode.ContextMenu => 0x5D,
        HookCode.Sleep => 0x5F,

        HookCode.MediaPlay => 0xB3,
        HookCode.MediaStop => 0xB2,
        HookCode.MediaPrevious => 0xB1,
        HookCode.MediaNext => 0xB0,
        HookCode.MediaSelect => 0xB5,
        HookCode.VolumeMute => 0xAD,
        HookCode.VolumeDown => 0xAE,
        HookCode.VolumeUp => 0xAF,
        HookCode.App1 => 0xB6,
        HookCode.App2 => 0xB7,
        HookCode.AppMail => 0xB4,
        HookCode.BrowserHome => 0xAC,
        HookCode.BrowserBack => 0xA6,
        HookCode.BrowserForward => 0xA7,
        HookCode.BrowserStop => 0xA9,
        HookCode.BrowserRefresh => 0xA8,
        HookCode.BrowserSearch => 0xAA,
        HookCode.BrowserFavorites => 0xAB,

        // The keys a Japanese or Korean keyboard has for its own script. Windows numbers them, but
        // gives them no scan code on the layouts this program runs on, so nothing a keyboard sends
        // can become one of them: measured, asking the foreground device for one posts VK 0xFF and
        // no key arrives at all. They are therefore answered with null, like every other key no
        // keystroke can carry, rather than being offered as a key that silently does nothing.
        HookCode.Kana => null,
        HookCode.Hanja => null,
        HookCode.Junja => null,
        HookCode.Final => null,
        HookCode.Accept => null,
        HookCode.Convert => null,
        HookCode.NonConvert => null,
        HookCode.ImeOn => null,
        HookCode.ImeOff => null,
        HookCode.ModeChange => null,
        HookCode.Process => null,

        // Keys the library has a code for and Windows has not: a macro that names one of these gets
        // a step that says which key it could not press, rather than a different key being pressed.
        HookCode.Function => null,
        HookCode.ChangeInputSource => null,
        HookCode.Power => null,
        HookCode.MediaEject => null,
        HookCode.App3 => null,
        HookCode.App4 => null,
        HookCode.AppBrowser => null,
        HookCode.AppCalculator => null,
        HookCode.KatakanaHiragana => null,
        HookCode.Katakana => null,
        HookCode.Hiragana => null,
        HookCode.Alphanumeric => null,
        HookCode.Underscore => null,
        HookCode.Yen => null,
        HookCode.JpComma => null,

        _ => null,
    };

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
