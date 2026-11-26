using System.Linq;

using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The numbers a counting loop runs over. A step of zero takes its direction from the two ends, and
/// a step that walks away from the end leaves the loop with nothing to do.
/// </summary>
public class CountPlanTests
{
    private static int[] Values(int from, int to, int step)
    {
        var range = CountPlan.Of(from, to, step);
        var values = new List<int>();
        for (var value = range.From; CountPlan.Covers(range, value); value += range.Stride)
        {
            values.Add(value);
            if (values.Count > 100)
            {
                break;
            }
        }

        return [.. values];
    }

    [Fact]
    public void A_step_of_zero_counts_up_when_the_end_is_above()
    {
        var range = CountPlan.Of(1, 5, 0);

        Assert.Equal(1, range.Stride);
        Assert.Equal([1, 2, 3, 4, 5], Values(1, 5, 0));
    }

    [Fact]
    public void A_step_of_zero_counts_down_when_the_end_is_below()
    {
        var range = CountPlan.Of(5, 1, 0);

        Assert.Equal(-1, range.Stride);
        Assert.Equal([5, 4, 3, 2, 1], Values(5, 1, 0));
    }

    [Fact]
    public void A_written_step_is_kept()
    {
        Assert.Equal([1, 3, 5, 7], Values(1, 7, 2));
        Assert.Equal([7, 5, 3, 1], Values(7, 1, -2));
    }

    [Fact]
    public void A_step_that_moves_away_from_the_end_runs_nothing()
        => Assert.Empty(Values(5, 1, 1));

    [Fact]
    public void One_number_counts_once()
        => Assert.Equal([3], Values(3, 3, 0));

    [Fact]
    public void The_end_itself_is_covered_and_the_one_past_it_is_not()
    {
        var up = CountPlan.Of(1, 5, 0);
        Assert.True(CountPlan.Covers(up, 1));
        Assert.True(CountPlan.Covers(up, 5));
        Assert.False(CountPlan.Covers(up, 6));

        var down = CountPlan.Of(5, 1, 0);
        Assert.True(CountPlan.Covers(down, 5));
        Assert.True(CountPlan.Covers(down, 1));
        Assert.False(CountPlan.Covers(down, 0));
    }
}
