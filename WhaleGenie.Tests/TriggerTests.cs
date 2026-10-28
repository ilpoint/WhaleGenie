using WhaleGenie.Models;
using WhaleGenie.Runtime;

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

    /// <summary>
    /// A window that is already open when the watching starts is where the counting starts, not an
    /// appearance. It is also what keeps a burst of events — a window being made says so more than
    /// once — from setting the same macro off several times over.
    /// </summary>
    [Fact]
    public void A_window_seen_twice_is_one_appearance()
    {
        var watches = new WindowWatches();
        var macro = new MacroItem();

        Assert.False(watches.Changed(macro, true));
        Assert.True(watches.For(macro));

        Assert.False(watches.Changed(macro, true));
        Assert.False(watches.Changed(macro, true));

        Assert.True(watches.Changed(macro, false));
        Assert.False(watches.Changed(macro, false));
        Assert.True(watches.Changed(macro, true));

        // A macro that is put back in the list starts over rather than being told what it knew.
        watches.Forget(macro);
        Assert.Null(watches.For(macro));
        Assert.False(watches.Changed(macro, true));
    }

    /// <summary>
    /// Nothing says when a program starts, so this is a difference against the reading before. The
    /// same program counted twice is one start, and what was running when the watching began is not
    /// a start at all: switching the system on must not set off every macro whose program is open.
    /// </summary>
    [Fact]
    public void A_program_already_running_is_not_a_start()
    {
        var watches = new ProgramWatches();
        var macro = new MacroItem();

        Assert.Null(watches.Changed(macro, [7, 8]));
        Assert.True(watches.For(macro));

        Assert.Equal((false, false), watches.Changed(macro, [7, 8]));
        Assert.Equal((true, false), watches.Changed(macro, [7, 8, 9]));
        Assert.Equal((false, true), watches.Changed(macro, [7, 9]));
        Assert.Equal((true, true), watches.Changed(macro, [9, 10]));
        Assert.Equal((false, true), watches.Changed(macro, []));
        Assert.False(watches.For(macro));

        watches.Forget(macro);
        Assert.Null(watches.For(macro));
        Assert.Null(watches.Changed(macro, [7]));
    }
}
