using WhaleGenie.Core.Devices;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// The keys a USB keyboard sends, under the numbers HID gives them. They are also the numbers the
/// virtual keyboard counts its keys by: a report has one bit per usage code.
/// </summary>
/// <remarks>
/// Only the keys a macro can name are here. The numbers are nobody's private convention — they are
/// what the HID usage tables say — so they are written down here rather than taken from a package,
/// which is one thing less that has to agree with something else.
/// </remarks>
public enum HidUsage : byte
{
    A = 0x04,
    B = 0x05,
    C = 0x06,
    D = 0x07,
    E = 0x08,
    F = 0x09,
    G = 0x0A,
    H = 0x0B,
    I = 0x0C,
    J = 0x0D,
    K = 0x0E,
    L = 0x0F,
    M = 0x10,
    N = 0x11,
    O = 0x12,
    P = 0x13,
    Q = 0x14,
    R = 0x15,
    S = 0x16,
    T = 0x17,
    U = 0x18,
    V = 0x19,
    W = 0x1A,
    X = 0x1B,
    Y = 0x1C,
    Z = 0x1D,
    Num1 = 0x1E,
    Num2 = 0x1F,
    Num3 = 0x20,
    Num4 = 0x21,
    Num5 = 0x22,
    Num6 = 0x23,
    Num7 = 0x24,
    Num8 = 0x25,
    Num9 = 0x26,
    Num0 = 0x27,
    Enter = 0x28,
    Escape = 0x29,
    Backspace = 0x2A,
    Tab = 0x2B,
    Space = 0x2C,
    Minus = 0x2D,
    Equal = 0x2E,
    LeftBrace = 0x2F,
    RightBrace = 0x30,
    Backslash = 0x31,

    /// <summary>The extra key an ISO keyboard carries left of Z, which Windows calls the section sign.</summary>
    NonUsBackslash = 0x64,

    Semicolon = 0x33,
    Apostrophe = 0x34,
    Grave = 0x35,
    Comma = 0x36,
    Period = 0x37,
    Slash = 0x38,
    CapsLock = 0x39,
    F1 = 0x3A,
    F2 = 0x3B,
    F3 = 0x3C,
    F4 = 0x3D,
    F5 = 0x3E,
    F6 = 0x3F,
    F7 = 0x40,
    F8 = 0x41,
    F9 = 0x42,
    F10 = 0x43,
    F11 = 0x44,
    F12 = 0x45,
    PrintScreen = 0x46,
    ScrollLock = 0x47,
    Pause = 0x48,
    Insert = 0x49,
    Home = 0x4A,
    PageUp = 0x4B,
    Delete = 0x4C,
    End = 0x4D,
    PageDown = 0x4E,
    Right = 0x4F,
    Left = 0x50,
    Down = 0x51,
    Up = 0x52,
    NumLock = 0x53,
    KpSlash = 0x54,
    KpAsterisk = 0x55,
    KpMinus = 0x56,
    KpPlus = 0x57,
    KpEnter = 0x58,
    Kp1 = 0x59,
    Kp2 = 0x5A,
    Kp3 = 0x5B,
    Kp4 = 0x5C,
    Kp5 = 0x5D,
    Kp6 = 0x5E,
    Kp7 = 0x5F,
    Kp8 = 0x60,
    Kp9 = 0x61,
    Kp0 = 0x62,
    KpDot = 0x63,
    Application = 0x65,
    Power = 0x66,
    F13 = 0x68,
    F14 = 0x69,
    F15 = 0x6A,
    F16 = 0x6B,
    F17 = 0x6C,
    F18 = 0x6D,
    F19 = 0x6E,
    F20 = 0x6F,
    F21 = 0x70,
    F22 = 0x71,
    F23 = 0x72,
    F24 = 0x73,
}

/// <summary>
/// Turns the keys a macro names into the codes a virtual USB keyboard sends. WhaleGenie names its keys
/// after the platform's own codes and the device wants HID usage IDs, so this is where the two
/// vocabularies meet; a key with no code on the other side is refused rather than guessed at.
/// </summary>
internal static class ViiperKeys
{
    /// <summary>
    /// The modifier a code stands for, which travels in the report's modifier byte rather than in
    /// its list of held keys. Anything else is an ordinary key.
    /// </summary>
    public static byte? Modifier(HookCode code) => code switch
    {
        HookCode.LeftControl => LibViiper.ModifierLeftCtrl,
        HookCode.RightControl => LibViiper.ModifierRightCtrl,
        HookCode.LeftShift => LibViiper.ModifierLeftShift,
        HookCode.RightShift => LibViiper.ModifierRightShift,
        HookCode.LeftAlt => LibViiper.ModifierLeftAlt,
        HookCode.RightAlt => LibViiper.ModifierRightAlt,
        HookCode.LeftMeta => LibViiper.ModifierLeftGui,
        HookCode.RightMeta => LibViiper.ModifierRightGui,
        _ => null,
    };

    /// <summary>
    /// The HID usage ID a key sends, or null when a virtual keyboard has no such key. The letters,
    /// the digits and the function keys are contiguous in both vocabularies, so they are worked out
    /// from where they sit in their range; the rest are named one by one, since the two sides
    /// disagree about almost everything else ("Esc" is Escape, the number pad is Kp, and so on).
    /// </summary>
    public static HidUsage? Usage(HookCode code) => code switch
    {
        >= HookCode.A and <= HookCode.Z =>
            (HidUsage)((int)HidUsage.A + ((int)code - (int)HookCode.A)),

        // The digits run 1 to 9 and then 0, so that is the way round the codes go.
        >= HookCode.Digit1 and <= HookCode.Digit9 =>
            (HidUsage)((int)HidUsage.Num1 + ((int)code - (int)HookCode.Digit1)),
        HookCode.Digit0 => HidUsage.Num0,

        // The first twelve function keys sit together, and the rest sit at the end of the device's
        // list, past the number pad.
        >= HookCode.F1 and <= HookCode.F12 =>
            (HidUsage)((int)HidUsage.F1 + ((int)code - (int)HookCode.F1)),
        >= HookCode.F13 and <= HookCode.F24 =>
            (HidUsage)((int)HidUsage.F13 + ((int)code - (int)HookCode.F13)),

        HookCode.Escape => HidUsage.Escape,
        HookCode.Backspace => HidUsage.Backspace,
        HookCode.Tab => HidUsage.Tab,
        HookCode.Space => HidUsage.Space,
        HookCode.Enter => HidUsage.Enter,
        HookCode.CapsLock => HidUsage.CapsLock,
        HookCode.Minus => HidUsage.Minus,
        HookCode.Equals => HidUsage.Equal,
        HookCode.OpenBracket => HidUsage.LeftBrace,
        HookCode.CloseBracket => HidUsage.RightBrace,
        HookCode.Backslash => HidUsage.Backslash,
        HookCode.Section => HidUsage.NonUsBackslash,
        HookCode.Semicolon => HidUsage.Semicolon,
        HookCode.Quote => HidUsage.Apostrophe,
        HookCode.BackQuote => HidUsage.Grave,
        HookCode.Comma => HidUsage.Comma,
        HookCode.Period => HidUsage.Period,
        HookCode.Slash => HidUsage.Slash,

        HookCode.PrintScreen => HidUsage.PrintScreen,
        HookCode.ScrollLock => HidUsage.ScrollLock,
        HookCode.Pause => HidUsage.Pause,
        HookCode.Insert => HidUsage.Insert,
        HookCode.Delete => HidUsage.Delete,
        HookCode.Home => HidUsage.Home,
        HookCode.End => HidUsage.End,
        HookCode.PageUp => HidUsage.PageUp,
        HookCode.PageDown => HidUsage.PageDown,
        HookCode.Up => HidUsage.Up,
        HookCode.Down => HidUsage.Down,
        HookCode.Left => HidUsage.Left,
        HookCode.Right => HidUsage.Right,
        HookCode.ContextMenu => HidUsage.Application,

        // The library's "help" key is the device's usage 0x75, which Windows' own keyboard driver
        // turns into no key at all — measured with a hook: the report arrives, nothing comes out.
        // Leaving it here would be a key that quietly does nothing, so the device refuses it.
        HookCode.Power => HidUsage.Power,

        HookCode.NumLock => HidUsage.NumLock,
        HookCode.NumPadDivide => HidUsage.KpSlash,
        HookCode.NumPadMultiply => HidUsage.KpAsterisk,
        HookCode.NumPadSubtract => HidUsage.KpMinus,
        HookCode.NumPadAdd => HidUsage.KpPlus,
        HookCode.NumPadEnter => HidUsage.KpEnter,
        HookCode.NumPadDecimal => HidUsage.KpDot,

        // The number pad's equals key becomes VK_CLEAR on Windows, which is the number pad's 5 with
        // NumLock off — a key the macro already has under its own name. Two names for one key is
        // what makes a recording and a macro disagree, so this one is left to the refusal.
        HookCode.NumPadEquals => null,
        >= HookCode.NumPad1 and <= HookCode.NumPad9 =>
            (HidUsage)((int)HidUsage.Kp1 + ((int)code - (int)HookCode.NumPad1)),
        HookCode.NumPad0 => HidUsage.Kp0,

        // The media keys and the volume keys are not on the device's keyboard page at all: a real
        // keyboard sends them as consumer-control reports, and the virtual keyboard here has only
        // the one report it fills with key usages. Writing them into that report is a key Windows
        // does not number — measured: the report arrives and VK 0xFF comes out, or nothing does.
        // They are therefore refused by this device rather than pressed as something else.
        HookCode.VolumeMute => null,
        HookCode.VolumeDown => null,
        HookCode.VolumeUp => null,
        HookCode.MediaPlay => null,
        HookCode.MediaStop => null,
        HookCode.MediaNext => null,
        HookCode.MediaPrevious => null,

        _ => null,
    };

    /// <summary>
    /// What one character of typed text needs from the keyboard, or null when a keyboard cannot
    /// produce it at all. Text arrives as characters and a keyboard sends keys, so the character
    /// has to be looked up on the layout a keyboard has: the one printed on its key caps, where
    /// the shift of a key is the character written above it.
    /// </summary>
    public static (HidUsage Usage, bool Shift)? Typing(char character) => character switch
    {
        >= 'a' and <= 'z' => ((HidUsage)((int)HidUsage.A + (character - 'a')), false),
        >= 'A' and <= 'Z' => ((HidUsage)((int)HidUsage.A + (character - 'A')), true),
        >= '1' and <= '9' => ((HidUsage)((int)HidUsage.Num1 + (character - '1')), false),
        '0' => (HidUsage.Num0, false),

        '\t' => (HidUsage.Tab, false),
        '\n' or '\r' => (HidUsage.Enter, false),
        ' ' => (HidUsage.Space, false),

        '!' => (HidUsage.Num1, true),
        '"' => (HidUsage.Apostrophe, true),
        '#' => (HidUsage.Num3, true),
        '$' => (HidUsage.Num4, true),
        '%' => (HidUsage.Num5, true),
        '&' => (HidUsage.Num7, true),
        '\'' => (HidUsage.Apostrophe, false),
        '(' => (HidUsage.Num9, true),
        ')' => (HidUsage.Num0, true),
        '*' => (HidUsage.Num8, true),
        '+' => (HidUsage.Equal, true),
        ',' => (HidUsage.Comma, false),
        '-' => (HidUsage.Minus, false),
        '.' => (HidUsage.Period, false),
        '/' => (HidUsage.Slash, false),
        ':' => (HidUsage.Semicolon, true),
        ';' => (HidUsage.Semicolon, false),
        '<' => (HidUsage.Comma, true),
        '=' => (HidUsage.Equal, false),
        '>' => (HidUsage.Period, true),
        '?' => (HidUsage.Slash, true),
        '@' => (HidUsage.Num2, true),
        '[' => (HidUsage.LeftBrace, false),
        '\\' => (HidUsage.Backslash, false),
        ']' => (HidUsage.RightBrace, false),
        '^' => (HidUsage.Num6, true),
        '_' => (HidUsage.Minus, true),
        '`' => (HidUsage.Grave, false),
        '{' => (HidUsage.LeftBrace, true),
        '|' => (HidUsage.Backslash, true),
        '}' => (HidUsage.RightBrace, true),
        '~' => (HidUsage.Grave, true),

        _ => null,
    };
}
