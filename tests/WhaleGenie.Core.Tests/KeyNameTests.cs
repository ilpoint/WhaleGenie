using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The names a key travels under. A trigger binding has to say which half of a modifier it
/// means, while the spelling a recording or a hand-written chord uses stays short.
/// </summary>
public class KeyNameTests
{
    [Theory]
    [InlineData((int)HookCode.LeftShift, "左Shift")]
    [InlineData((int)HookCode.RightShift, "右Shift")]
    [InlineData((int)HookCode.LeftControl, "左Ctrl")]
    [InlineData((int)HookCode.RightControl, "右Ctrl")]
    [InlineData((int)HookCode.LeftAlt, "左Alt")]
    [InlineData((int)HookCode.RightAlt, "右Alt")]
    [InlineData((int)HookCode.LeftMeta, "左Win")]
    [InlineData((int)HookCode.RightMeta, "右Win")]
    public void SidedNameKeepsTheHalf(int code, string expected)
        => Assert.Equal(expected, KeyNames.Name(KeyNames.Key((HookCode)code), sided: true));

    [Theory]
    [InlineData((int)HookCode.LeftShift)]
    [InlineData((int)HookCode.RightShift)]
    public void PlainNameStaysSideAgnostic(int code)
        => Assert.Equal("Shift", KeyNames.Name(KeyNames.Key((HookCode)code)));

    [Fact]
    public void The_list_of_names_holds_every_key_a_picker_offers()
    {
        // Every name the picker shows has to be one the engine understands again, or choosing a
        // key from the list would leave a step that cannot be run.
        Assert.All(KeyNames.Names, name => Assert.NotNull(KeyNames.Resolve(name)));

        // And the everyday keys are in it, without repeats from the two halves of a modifier.
        foreach (var wanted in new[] { "Ctrl", "Shift", "Alt", "Win", "A", "5", "F5", "Enter", "Esc", "NumPad5" })
        {
            Assert.Contains(wanted, KeyNames.Names);
        }

        Assert.Equal(KeyNames.Names.Count, KeyNames.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_number_pad_key_is_also_known_by_the_navigation_key_it_becomes()
    {
        // With NumLock off the number pad is the navigation block, and the hook names what it sees
        // in that state: the macro writes NumPad7 and the key comes back as Home.
        Assert.Equal(["Home"], KeyNames.AlsoSeenAs(KeyNames.Key(HookCode.NumPad7)));
        Assert.Equal(["Delete"], KeyNames.AlsoSeenAs(KeyNames.Key(HookCode.NumPadDecimal)));

        // A key whose name no state decides has only the one name.
        Assert.Empty(KeyNames.AlsoSeenAs(KeyNames.Key(HookCode.Home)));
        Assert.Empty(KeyNames.AlsoSeenAs(KeyNames.Key(HookCode.A)));
    }

    [Fact]
    public void Every_second_name_is_a_name_the_engine_understands()
    {
        // These words are written into the input gate, which compares them against what the hook
        // says; a name nothing ever reports would be a key whose own output is not recognised.
        foreach (var code in Enum.GetValues<HookCode>())
        {
            Assert.All(KeyNames.AlsoSeenAs(KeyNames.Key(code)), name => Assert.NotNull(KeyNames.Resolve(name)));
        }
    }

    [Fact]
    public void OtherKeysAreSpelledTheSameEitherWay()
    {
        Assert.Equal("NumPad7", KeyNames.Name(KeyNames.Key(HookCode.NumPad7), sided: true));
        Assert.Equal("NumPad7", KeyNames.Name(KeyNames.Key(HookCode.NumPad7)));
        Assert.Equal("Home", KeyNames.Name(KeyNames.Key(HookCode.Home), sided: true));
    }

    [Theory]
    [InlineData("左Shift", (int)HookCode.LeftShift)]
    [InlineData("右Shift", (int)HookCode.RightShift)]
    [InlineData("LeftShift", (int)HookCode.LeftShift)]
    [InlineData("RShift", (int)HookCode.RightShift)]
    [InlineData("右Ctrl", (int)HookCode.RightControl)]
    [InlineData("LAlt", (int)HookCode.LeftAlt)]
    [InlineData("右Win", (int)HookCode.RightMeta)]
    public void EverySpellingResolves(string name, int expected)
        => Assert.Equal(KeyNames.Key((HookCode)expected), KeyNames.Resolve(name));

    [Fact]
    public void BareModifiersKeepMeaningTheLeftOne()
    {
        Assert.Equal(KeyNames.Key(HookCode.LeftShift), KeyNames.Resolve("Shift"));
        Assert.Equal(KeyNames.Key(HookCode.LeftControl), KeyNames.Resolve("Ctrl"));
        Assert.Equal(KeyNames.Key(HookCode.LeftAlt), KeyNames.Resolve("Alt"));
        Assert.Equal(KeyNames.Key(HookCode.LeftMeta), KeyNames.Resolve("Win"));
    }

    [Theory]
    [InlineData("左Shift")]
    [InlineData("右Shift")]
    [InlineData("右Ctrl")]
    public void SidedNamesRoundTrip(string name)
    {
        var code = KeyNames.Resolve(name);
        Assert.NotNull(code);
        Assert.Equal(name, KeyNames.Name(code.Value, sided: true));
    }

    [Fact]
    public void AChordAcceptsBothSpellings()
    {
        var chords = KeyNames.ResolveChord("右Ctrl+右Shift+S", out var unknown);
        Assert.Null(unknown);
        Assert.Equal(
            [
                KeyNames.Key(HookCode.RightControl),
                KeyNames.Key(HookCode.RightShift),
                KeyNames.Key(HookCode.S),
            ],
            chords);
    }

    [Fact]
    public void Every_code_the_hook_reports_keeps_its_identity()
    {
        // Which library reads the keyboard is the engine's business, and this is the seam where the
        // two vocabularies meet: a key that did not come back as the code it was made from would
        // make a trigger answer to the wrong key, and nothing else in the program would notice.
        foreach (var code in Enum.GetValues<HookCode>())
        {
            Assert.Equal(code, KeyNames.Code(KeyNames.Key(code)));
        }
    }

    [Fact]
    public void The_mouse_and_the_wheel_keep_the_spelling_a_binding_stores()
    {
        // These words are written into a trigger binding and into a recorded macro, so changing one
        // would quietly stop an existing binding from answering.
        Assert.Equal("Mouse Left", KeyNames.MouseName(HookMouseButton.Left));
        Assert.Equal("Mouse Right", KeyNames.MouseName(HookMouseButton.Right));
        Assert.Equal("Mouse Middle", KeyNames.MouseName(HookMouseButton.Middle));
        Assert.Equal("Mouse 4", KeyNames.MouseName(HookMouseButton.Back));
        Assert.Equal("Mouse 5", KeyNames.MouseName(HookMouseButton.Forward));

        Assert.Equal("Wheel Up", KeyNames.WheelName(HookWheelAxis.Vertical, 120));
        Assert.Equal("Wheel Down", KeyNames.WheelName(HookWheelAxis.Vertical, -120));
        Assert.Equal("Wheel Left", KeyNames.WheelName(HookWheelAxis.Horizontal, 120));
        Assert.Equal("Wheel Right", KeyNames.WheelName(HookWheelAxis.Horizontal, -120));
    }
}
