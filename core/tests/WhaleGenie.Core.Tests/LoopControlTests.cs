using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Whether a break or a continue has something to leave: a break also has a switch, a continue only
/// ever means a loop, and with nothing above it the step is refused with the key that says so.
/// </summary>
public class LoopControlTests
{
    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(2, 3, true)]
    [InlineData(0, 0, false)]
    public void A_break_leaves_a_loop_or_a_switch(int loops, int switches, bool expected)
        => Assert.Equal(expected, LoopControl.HasSomethingToLeave(Signal.Break, loops, switches));

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    public void A_continue_only_leaves_a_loop(int loops, int switches, bool expected)
        => Assert.Equal(expected, LoopControl.HasSomethingToLeave(Signal.Continue, loops, switches));

    [Fact]
    public void The_failure_names_which_control_had_nowhere_to_go()
    {
        Assert.Equal(RunKeys.BreakOutsideLoop, LoopControl.FailureKey(Signal.Break));
        Assert.Equal(RunKeys.ContinueOutsideLoop, LoopControl.FailureKey(Signal.Continue));
    }
}
