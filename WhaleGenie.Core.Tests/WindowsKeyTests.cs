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
    /// The keys the library has and Windows has no code for, named here rather than mapped to a
    /// neighbouring key that would press something the macro did not ask for. A key the library adds
    /// later lands in this list only when somebody has looked at what Windows makes of it.
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
}
