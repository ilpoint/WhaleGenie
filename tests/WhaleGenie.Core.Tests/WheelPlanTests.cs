using System.Linq;

using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How one scroll is split over wheel events: a scroll with no duration is one turn, a smooth one is
/// the same total over several turns with a beat between them, and the units always add up.
/// </summary>
public class WheelPlanTests
{
    [Fact]
    public void A_scroll_with_no_duration_is_one_turn()
    {
        var turn = Assert.Single(WheelPlan.Of(120, 0));

        Assert.Equal(120, turn.Units);
        Assert.Equal(0, turn.PauseBeforeMs);
    }

    [Fact]
    public void A_smooth_scroll_is_the_same_total_over_several_turns()
    {
        var turns = WheelPlan.Of(100, 150);

        Assert.Equal(10, turns.Count);
        Assert.All(turns, turn => Assert.Equal(10, turn.Units));
        Assert.Equal(0, turns[0].PauseBeforeMs);
        Assert.All(turns.Skip(1), turn => Assert.Equal(15, turn.PauseBeforeMs));
        Assert.Equal(100, turns.Sum(turn => turn.Units));
    }

    [Fact]
    public void A_duration_too_short_for_two_events_is_one_turn()
    {
        var turn = Assert.Single(WheelPlan.Of(100, 20));

        Assert.Equal(100, turn.Units);
        Assert.Equal(0, turn.PauseBeforeMs);
    }

    [Fact]
    public void There_are_never_more_events_than_units()
    {
        var turns = WheelPlan.Of(7, 150);

        Assert.Equal(7, turns.Count);
        Assert.All(turns, turn => Assert.Equal(1, turn.Units));
        Assert.Equal(21, turns[1].PauseBeforeMs);
        Assert.Equal(7, turns.Sum(turn => turn.Units));
    }

    [Fact]
    public void The_units_that_do_not_divide_evenly_go_on_the_first_turns()
    {
        var turns = WheelPlan.Of(10, 35);

        Assert.Equal(2, turns.Count);
        Assert.Equal([5, 5], turns.Select(turn => turn.Units));
        Assert.Equal(17, turns[1].PauseBeforeMs);
    }

    [Fact]
    public void A_scroll_of_nothing_sends_nothing()
        => Assert.Empty(WheelPlan.Of(0, 100));
}
