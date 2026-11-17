using WhaleGenie.Runtime;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// Which key a trigger answers to, and what the runtime remembers about an entry. The two halves of
/// a modifier are separate keys, the older side-agnostic spelling has to keep working, and a chord
/// only counts with its modifiers down.
/// </summary>
public class TriggerTests
{
    private static bool Binds(string binding, string key, params string[] held)
        => TriggerKeys.Binds(binding, key, held);

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
        var state = new TriggerStateStore();

        Assert.False(state.WindowChanged("one", true));
        Assert.True(state.Window("one"));

        Assert.False(state.WindowChanged("one", true));
        Assert.False(state.WindowChanged("one", true));

        Assert.True(state.WindowChanged("one", false));
        Assert.False(state.WindowChanged("one", false));
        Assert.True(state.WindowChanged("one", true));
    }

    /// <summary>
    /// Nothing says when a program starts, so this is a difference against the reading before. The
    /// same program counted twice is one start, and what was running when the watching began is not
    /// a start at all: switching the system on must not set off every macro whose program is open.
    /// </summary>
    [Fact]
    public void A_program_already_running_is_not_a_start()
    {
        var state = new TriggerStateStore();

        Assert.Null(state.ProgramChanged("one", [7, 8]));
        Assert.True(state.Program("one"));

        Assert.Equal((false, false), state.ProgramChanged("one", [7, 8]));
        Assert.Equal((true, false), state.ProgramChanged("one", [7, 8, 9]));
        Assert.Equal((false, true), state.ProgramChanged("one", [7, 9]));
        Assert.Equal((true, true), state.ProgramChanged("one", [9, 10]));
        Assert.Equal((false, true), state.ProgramChanged("one", []));
        Assert.False(state.Program("one"));
    }
}
