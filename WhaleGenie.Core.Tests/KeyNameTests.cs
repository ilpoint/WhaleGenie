using System;
using SharpHook.Data;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The names a key travels under. A trigger binding has to say which half of a modifier it
/// means, while the spelling a recording or a hand-written chord uses stays short.
/// </summary>
public class KeyNameTests
{
    [Theory]
    [InlineData(KeyCode.VcLeftShift, "左Shift")]
    [InlineData(KeyCode.VcRightShift, "右Shift")]
    [InlineData(KeyCode.VcLeftControl, "左Ctrl")]
    [InlineData(KeyCode.VcRightControl, "右Ctrl")]
    [InlineData(KeyCode.VcLeftAlt, "左Alt")]
    [InlineData(KeyCode.VcRightAlt, "右Alt")]
    [InlineData(KeyCode.VcLeftMeta, "左Win")]
    [InlineData(KeyCode.VcRightMeta, "右Win")]
    public void SidedNameKeepsTheHalf(KeyCode code, string expected)
        => Assert.Equal(expected, KeyNames.Name(SharpKeys.Key(code), sided: true));

    [Theory]
    [InlineData(KeyCode.VcLeftShift)]
    [InlineData(KeyCode.VcRightShift)]
    public void PlainNameStaysSideAgnostic(KeyCode code)
        => Assert.Equal("Shift", KeyNames.Name(SharpKeys.Key(code)));

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
        Assert.Equal(["Home"], KeyNames.AlsoSeenAs(SharpKeys.Key(KeyCode.VcNumPad7)));
        Assert.Equal(["Delete"], KeyNames.AlsoSeenAs(SharpKeys.Key(KeyCode.VcNumPadDecimal)));

        // A key whose name no state decides has only the one name.
        Assert.Empty(KeyNames.AlsoSeenAs(SharpKeys.Key(KeyCode.VcHome)));
        Assert.Empty(KeyNames.AlsoSeenAs(SharpKeys.Key(KeyCode.VcA)));
    }

    [Fact]
    public void Every_second_name_is_a_name_the_engine_understands()
    {
        // These words are written into the input gate, which compares them against what the hook
        // says; a name nothing ever reports would be a key whose own output is not recognised.
        foreach (var code in Enum.GetValues<KeyCode>())
        {
            Assert.All(KeyNames.AlsoSeenAs(SharpKeys.Key(code)), name => Assert.NotNull(KeyNames.Resolve(name)));
        }
    }

    [Fact]
    public void OtherKeysAreSpelledTheSameEitherWay()
    {
        Assert.Equal("NumPad7", KeyNames.Name(SharpKeys.Key(KeyCode.VcNumPad7), sided: true));
        Assert.Equal("NumPad7", KeyNames.Name(SharpKeys.Key(KeyCode.VcNumPad7)));
        Assert.Equal("Home", KeyNames.Name(SharpKeys.Key(KeyCode.VcHome), sided: true));
    }

    [Theory]
    [InlineData("左Shift", KeyCode.VcLeftShift)]
    [InlineData("右Shift", KeyCode.VcRightShift)]
    [InlineData("LeftShift", KeyCode.VcLeftShift)]
    [InlineData("RShift", KeyCode.VcRightShift)]
    [InlineData("右Ctrl", KeyCode.VcRightControl)]
    [InlineData("LAlt", KeyCode.VcLeftAlt)]
    [InlineData("右Win", KeyCode.VcRightMeta)]
    public void EverySpellingResolves(string name, KeyCode expected)
        => Assert.Equal(SharpKeys.Key(expected), KeyNames.Resolve(name));

    [Fact]
    public void BareModifiersKeepMeaningTheLeftOne()
    {
        Assert.Equal(SharpKeys.Key(KeyCode.VcLeftShift), KeyNames.Resolve("Shift"));
        Assert.Equal(SharpKeys.Key(KeyCode.VcLeftControl), KeyNames.Resolve("Ctrl"));
        Assert.Equal(SharpKeys.Key(KeyCode.VcLeftAlt), KeyNames.Resolve("Alt"));
        Assert.Equal(SharpKeys.Key(KeyCode.VcLeftMeta), KeyNames.Resolve("Win"));
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
                SharpKeys.Key(KeyCode.VcRightControl),
                SharpKeys.Key(KeyCode.VcRightShift),
                SharpKeys.Key(KeyCode.VcS),
            ],
            chords);
    }

    [Fact]
    public void Every_code_the_hook_reports_keeps_its_identity()
    {
        // Which library reads the keyboard is the engine's business, and this is the seam where the
        // two vocabularies meet: a key that did not come back as the code it was made from would
        // make a trigger answer to the wrong key, and nothing else in the program would notice.
        foreach (var code in Enum.GetValues<KeyCode>())
        {
            Assert.Equal(code, SharpKeys.Code(SharpKeys.Key(code)));
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
