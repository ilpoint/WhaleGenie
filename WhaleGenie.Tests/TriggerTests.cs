using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// Which key a trigger answers to. The two halves of a modifier are separate keys, the older
/// side-agnostic spelling has to keep working, and a chord only counts with its modifiers down.
/// </summary>
public class TriggerTests
{
    private static bool Binds(string binding, string key, params string[] held)
        => MacroTriggerService.Binds(binding, key, held);

    [Theory]
    [InlineData("右Shift", "右Shift", true)]
    [InlineData("右Shift", "左Shift", false)]
    [InlineData("左Ctrl", "左Ctrl", true)]
    [InlineData("左Ctrl", "右Ctrl", false)]
    [InlineData("NumPad7", "NumPad7", true)]
    [InlineData("NumPad7", "NumPad8", false)]
    [InlineData("D7", "7", true)]
    [InlineData("F5", "f5", true)]
    public void A_binding_answers_the_key_it_names(string binding, string key, bool expected)
        => Assert.Equal(expected, Binds(binding, key));

    [Theory]
    [InlineData("RightShift")]
    [InlineData("RShift")]
    [InlineData("右Shift")]
    public void Every_spelling_of_a_side_means_the_same_key(string spelling)
        => Assert.True(Binds(spelling, "右Shift"));

    [Theory]
    [InlineData("Shift", "右Shift")]
    [InlineData("Shift", "左Shift")]
    [InlineData("Ctrl", "右Ctrl")]
    [InlineData("Alt", "左Alt")]
    public void An_older_binding_answers_both_halves(string binding, string key)
        => Assert.True(Binds(binding, key));

    [Fact]
    public void A_chord_needs_its_modifiers_down()
    {
        Assert.True(Binds("左Ctrl+S", "S", "左Ctrl"));
        Assert.True(Binds("Ctrl+S", "S", "左Ctrl"));
        Assert.False(Binds("右Ctrl+S", "S", "左Ctrl"));
        Assert.False(Binds("左Ctrl+S", "S"));
    }

    [Fact]
    public void A_chord_does_not_answer_a_different_key()
    {
        Assert.False(Binds("左Ctrl+S", "D", "左Ctrl"));
        Assert.False(Binds("", "S", "左Ctrl"));
    }
}
