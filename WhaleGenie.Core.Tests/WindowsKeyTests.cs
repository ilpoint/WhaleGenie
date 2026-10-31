using SharpHook.Data;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The virtual key codes Windows is posted for a macro's keys. The hook library numbers the keys its
/// own way, so a code from it cannot be sent to a window as it stands: the two are different numbers
/// for nearly every key, and the wrong one presses a different key without anything reporting it.
/// </summary>
public class WindowsKeyTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("1", 0x31)]
    [InlineData("0", 0x30)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("F24", 0x87)]
    [InlineData("Esc", 0x1B)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Space", 0x20)]
    [InlineData("Tab", 0x09)]
    [InlineData("Backspace", 0x08)]
    [InlineData("Up", 0x26)]
    [InlineData("Left", 0x25)]
    [InlineData("Home", 0x24)]
    [InlineData("PageDown", 0x22)]
    [InlineData("Insert", 0x2D)]
    [InlineData("Delete", 0x2E)]
    [InlineData("-", 0xBD)]
    [InlineData("=", 0xBB)]
    [InlineData("[", 0xDB)]
    [InlineData("\\", 0xDC)]
    [InlineData(";", 0xBA)]
    [InlineData("'", 0xDE)]
    [InlineData(",", 0xBC)]
    [InlineData(".", 0xBE)]
    [InlineData("/", 0xBF)]
    [InlineData("`", 0xC0)]
    [InlineData("NumPad5", 0x65)]
    [InlineData("NumPad0", 0x60)]
    [InlineData("NumPadAdd", 0x6B)]
    [InlineData("NumPadDecimal", 0x6E)]
    [InlineData("NumPadDivide", 0x6F)]
    [InlineData("NumPadEnter", 0x0D)]
    [InlineData("LeftCtrl", 0xA2)]
    [InlineData("RightCtrl", 0xA3)]
    [InlineData("左Shift", 0xA0)]
    [InlineData("右Shift", 0xA1)]
    [InlineData("左Alt", 0xA4)]
    [InlineData("右Alt", 0xA5)]
    [InlineData("Win", 0x5B)]
    [InlineData("RightWin", 0x5C)]
    [InlineData("ContextMenu", 0x5D)]
    [InlineData("PrintScreen", 0x2C)]
    [InlineData("ScrollLock", 0x91)]
    [InlineData("Pause", 0x13)]
    [InlineData("CapsLock", 0x14)]
    [InlineData("NumLock", 0x90)]
    [InlineData("MediaPlay", 0xB3)]
    [InlineData("MediaNext", 0xB0)]
    [InlineData("VolumeMute", 0xAD)]
    [InlineData("VolumeUp", 0xAF)]
    public void The_keys_a_macro_writes_reach_windows(string name, int expected)
        => Assert.Equal((ushort?)expected, WindowsKeys.VirtualKey(SharpKeys.ResolveCode(name)!.Value));

    [Theory]
    [InlineData("Yen")]
    [InlineData("Underscore")]
    [InlineData("Katakana")]
    [InlineData("KatakanaHiragana")]
    [InlineData("Power")]
    [InlineData("AppBrowser")]
    [InlineData("AppCalculator")]
    [InlineData("MediaEject")]
    [InlineData("Kana")]
    [InlineData("Convert")]
    [InlineData("Misc")]
    [InlineData("NumPadSeparator")]
    [InlineData("NumPadEquals")]
    public void A_key_windows_has_not_got_is_left_empty(string name)
        => Assert.Null(WindowsKeys.VirtualKey(SharpKeys.ResolveCode(name)!.Value));

    /// <summary>
    /// The runs that sit together on both sides are worked out from where a key stands rather than
    /// named one by one, so every key of the run is checked rather than the ends of it.
    /// </summary>
    [Fact]
    public void The_runs_of_keys_that_sit_together_keep_their_places()
    {
        for (ushort step = 0; step < 26; step++)
        {
            Assert.Equal((ushort)('A' + step), WindowsKeys.VirtualKey(KeyCode.VcA + step));
        }

        for (ushort digit = 0; digit < 9; digit++)
        {
            Assert.Equal((ushort)('1' + digit), WindowsKeys.VirtualKey(KeyCode.Vc1 + digit));
        }

        Assert.Equal((ushort)'0', WindowsKeys.VirtualKey(KeyCode.Vc0));

        for (ushort number = 0; number < 24; number++)
        {
            Assert.Equal((ushort)(0x70 + number), WindowsKeys.VirtualKey(KeyCode.VcF1 + number));
        }

        for (ushort digit = 0; digit < 9; digit++)
        {
            Assert.Equal((ushort)(0x61 + digit), WindowsKeys.VirtualKey(KeyCode.VcNumPad1 + digit));
        }

        Assert.Equal((ushort)0x60, WindowsKeys.VirtualKey(KeyCode.VcNumPad0));
    }

    /// <summary>
    /// The keys the library has and Windows has no keystroke for, named here rather than mapped to a
    /// neighbouring key that would press something the macro did not ask for. That is two groups:
    /// codes Windows does not number at all, and codes it numbers but gives no scan code — an IME's
    /// own keys, the number pad's separator and equals key, and the library's "misc" key. Measured
    /// with a keyboard hook: asking for one of the second group posts VK 0xFF and no key arrives. A
    /// key the library adds later lands in this list only when somebody has looked at what Windows
    /// makes of it.
    /// </summary>
    private static readonly KeyCode[] NoWindowsKey =
    [
        KeyCode.VcFunction,
        KeyCode.VcChangeInputSource,
        KeyCode.VcPower,
        KeyCode.VcMediaEject,
        KeyCode.VcApp3,
        KeyCode.VcApp4,
        KeyCode.VcAppBrowser,
        KeyCode.VcAppCalculator,
        KeyCode.VcKatakanaHiragana,
        KeyCode.VcKatakana,
        KeyCode.VcHiragana,
        KeyCode.VcAlphanumeric,
        KeyCode.VcUnderscore,
        KeyCode.VcYen,
        KeyCode.VcJpComma,
        KeyCode.VcKana,
        KeyCode.VcHanja,
        KeyCode.VcJunja,
        KeyCode.VcFinal,
        KeyCode.VcAccept,
        KeyCode.VcConvert,
        KeyCode.VcNonConvert,
        KeyCode.VcImeOn,
        KeyCode.VcImeOff,
        KeyCode.VcModeChange,
        KeyCode.VcProcess,
        KeyCode.VcMisc,
        KeyCode.VcNumPadSeparator,
        KeyCode.VcNumPadEquals,
    ];

    [Fact]
    public void Every_key_the_library_speaks_is_posted_or_named_as_having_no_code()
    {
        var unlisted = new List<string>();
        foreach (var code in Enum.GetValues<KeyCode>())
        {
            if (code == KeyCode.VcUndefined)
            {
                continue;
            }

            var posted = WindowsKeys.VirtualKey(code) is not null;
            if (posted == NoWindowsKey.Contains(code))
            {
                unlisted.Add(code.ToString());
            }
        }

        Assert.Empty(unlisted.Order());
    }

    /// <summary>
    /// Every key Windows numbers can be one key press, Pause included. A keyboard sends Pause as
    /// the three-byte sequence E1 1D 45, which a single input event cannot express — but the E1 1D
    /// prefix is eaten by the keyboard driver, and what the application is handed is VK_PAUSE with
    /// scan code 0x45. That pair is what the sender states, so the window is told the same thing a
    /// keyboard would have told it. Windows' own table answers 0 for Pause, which is why the scan
    /// code is written down for this one key: left out, the key could not be pressed at all — and
    /// the library's sender used to answer Pause with RightCtrl, which is worse than nothing.
    /// </summary>
    [Fact]
    public void Every_key_that_is_posted_can_be_pressed()
    {
        Assert.True(WindowsKeys.CanBePressed(KeyCode.VcPause));

        // The pair a real keyboard produces, once the driver has taken the E1 1D prefix off.
        Assert.Equal<ushort>(0x13, WindowsKeys.VirtualKey(KeyCode.VcPause) ?? 0);
        Assert.Equal(0x45, WindowsKeys.ScanCode(0x13));

        // Everything Windows numbers, and nothing it does not.
        var pressable = Enum.GetValues<KeyCode>()
            .Where(code => code != KeyCode.VcUndefined && WindowsKeys.CanBePressed(code))
            .Order()
            .ToArray();

        var wanted = Enum.GetValues<KeyCode>()
            .Where(code => code != KeyCode.VcUndefined && !NoWindowsKey.Contains(code))
            .Order()
            .ToArray();

        Assert.Equal(wanted, pressable);
    }

    /// <summary>
    /// Two keys can share a scan code, and the extended flag is the only thing that tells them
    /// apart. NumLock is where this went wrong: its scan code 0x45 is one byte of the Pause
    /// sequence (E1 1D 45), so a NumLock sent without the flag flips the lock on the Windows side
    /// and arrives at every browser as Pause — a key nobody pressed, one step after the Pause step
    /// that was switched off.
    /// </summary>
    [Fact]
    public void Keys_that_share_a_scan_code_are_told_apart_by_the_extended_flag()
    {
        // NumLock, whose scan code is the byte Pause is built from.
        Assert.Equal(0x45, WindowsKeys.ScanCode(0x90));
        Assert.True(WindowsKeys.IsExtended(0x90));

        // The navigation block against the number pad, which are the same physical keys.
        Assert.Equal(WindowsKeys.ScanCode(0x24), WindowsKeys.ScanCode(0x67));
        Assert.True(WindowsKeys.IsExtended(0x24));
        Assert.False(WindowsKeys.IsExtended(0x67));

        Assert.Equal(WindowsKeys.ScanCode(0x28), WindowsKeys.ScanCode(0x62));
        Assert.True(WindowsKeys.IsExtended(0x28));
        Assert.False(WindowsKeys.IsExtended(0x62));

        // A key that shares its scan code with nothing needs no flag.
        Assert.False(WindowsKeys.IsExtended(0x41));
        Assert.False(WindowsKeys.IsExtended(0x46));
    }

    /// <summary>
    /// Every key that is one half of such a pair is flagged, and the keys around them are not: a
    /// flag on the wrong key is the same bug from the other side.
    /// </summary>
    [Fact]
    public void The_extended_flag_covers_the_keys_that_need_it_and_no_others()
    {
        ushort[] extended =
        [
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,   // PageUp..Down, Home, End and the arrows
            0x2C, 0x2D, 0x2E,                                  // PrintScreen, Insert, Delete
            0x5B, 0x5C, 0x5D,                                  // the Windows keys and the menu key
            0x6F, 0x90, 0xA3, 0xA5,                            // number pad divide, NumLock, right Ctrl and Alt
        ];

        Assert.All(extended, key => Assert.True(WindowsKeys.IsExtended(key)));

        ushort[] plain = [0x41, 0x31, 0x46, 0x54, 0x60, 0x61, 0x67, 0x6B, 0x6D, 0x6E, 0xA0, 0xA2, 0xA4];
        Assert.All(plain, key => Assert.False(WindowsKeys.IsExtended(key)));
    }

    /// <summary>
    /// The number pad's Enter and the Enter in the middle of the keyboard are one Windows key code
    /// and one scan code; the extended flag is the only thing that says which was pressed. Asked of
    /// the key code alone the answer was "not extended" for both, so a macro that pressed the number
    /// pad's Enter typed the other one.
    /// </summary>
    [Fact]
    public void The_two_enters_are_told_apart_by_the_key_that_was_written()
    {
        Assert.Equal<ushort>(0x0D, WindowsKeys.VirtualKey(KeyCode.VcEnter) ?? 0);
        Assert.Equal<ushort>(0x0D, WindowsKeys.VirtualKey(KeyCode.VcNumPadEnter) ?? 0);

        Assert.False(WindowsKeys.IsExtended(KeyCode.VcEnter));
        Assert.True(WindowsKeys.IsExtended(KeyCode.VcNumPadEnter));
    }

    /// <summary>
    /// Every other key is answered from its Windows code, which is the same table the posting device
    /// builds its messages from: Home is extended, the number pad's 7 is not, and the letters are
    /// neither.
    /// </summary>
    [Fact]
    public void Every_other_key_takes_its_flag_from_its_key_code()
    {
        Assert.True(WindowsKeys.IsExtended(KeyCode.VcHome));
        Assert.False(WindowsKeys.IsExtended(KeyCode.VcNumPad7));
        Assert.True(WindowsKeys.IsExtended(KeyCode.VcUp));
        Assert.False(WindowsKeys.IsExtended(KeyCode.VcNumPad8));
        Assert.True(WindowsKeys.IsExtended(KeyCode.VcNumLock));
        Assert.False(WindowsKeys.IsExtended(KeyCode.VcA));
        Assert.False(WindowsKeys.IsExtended(KeyCode.VcPause));
    }
}
