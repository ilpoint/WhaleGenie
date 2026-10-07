using SharpHook.Data;
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
        => Assert.Equal(expected, KeyNames.Name(code, sided: true));

    [Theory]
    [InlineData(KeyCode.VcLeftShift)]
    [InlineData(KeyCode.VcRightShift)]
    public void PlainNameStaysSideAgnostic(KeyCode code)
        => Assert.Equal("Shift", KeyNames.Name(code));

    [Fact]
    public void OtherKeysAreSpelledTheSameEitherWay()
    {
        Assert.Equal("NumPad7", KeyNames.Name(KeyCode.VcNumPad7, sided: true));
        Assert.Equal("NumPad7", KeyNames.Name(KeyCode.VcNumPad7));
        Assert.Equal("Home", KeyNames.Name(KeyCode.VcHome, sided: true));
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
        => Assert.Equal(expected, KeyNames.Resolve(name));

    [Fact]
    public void BareModifiersKeepMeaningTheLeftOne()
    {
        Assert.Equal(KeyCode.VcLeftShift, KeyNames.Resolve("Shift"));
        Assert.Equal(KeyCode.VcLeftControl, KeyNames.Resolve("Ctrl"));
        Assert.Equal(KeyCode.VcLeftAlt, KeyNames.Resolve("Alt"));
        Assert.Equal(KeyCode.VcLeftMeta, KeyNames.Resolve("Win"));
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
        Assert.Equal([KeyCode.VcRightControl, KeyCode.VcRightShift, KeyCode.VcS], chords);
    }
}
