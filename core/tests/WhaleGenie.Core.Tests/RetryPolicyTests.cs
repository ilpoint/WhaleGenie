using System;

using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The retry rule read from a step's settings alone: how many tries are left, how long the pause
/// before one is at the run's speed, and whether the time left can pay for it.
/// </summary>
public class RetryPolicyTests
{
    private static StepMeta Meta(int retries = 0, int delay = 0,
        RetryBackoff backoff = RetryBackoff.Fixed) => new()
        {
            RetryCount = retries,
            RetryDelayMs = delay,
            RetryBackoff = backoff,
        };

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, false)]
    public void A_retry_is_allowed_while_the_count_has_room(int retries, int attempt, bool expected)
        => Assert.Equal(expected, RetryPolicy.Allows(Meta(retries: retries), attempt));

    [Theory]
    [InlineData(1.0, 100)]
    [InlineData(2.0, 200)]
    [InlineData(0.5, 50)]
    public void The_pause_is_scaled_with_the_run(double scale, int expected)
        => Assert.Equal(expected, RetryPolicy.PauseMs(Meta(delay: 100), 1, scale));

    [Fact]
    public void A_pause_the_speed_writes_down_to_nothing_is_no_pause()
        => Assert.Equal(0, RetryPolicy.PauseMs(Meta(delay: 1), 1, 0.1));

    [Fact]
    public void A_doubling_pause_doubles_each_time_and_stops_at_the_longest_one()
    {
        Assert.Equal(100, RetryPolicy.PauseMs(Meta(delay: 100, backoff: RetryBackoff.Doubling), 1));
        Assert.Equal(200, RetryPolicy.PauseMs(Meta(delay: 100, backoff: RetryBackoff.Doubling), 2));
        Assert.Equal(400, RetryPolicy.PauseMs(Meta(delay: 100, backoff: RetryBackoff.Doubling), 3));
        Assert.Equal(StepMeta.LongestPauseMs,
            RetryPolicy.PauseMs(Meta(delay: 1000, backoff: RetryBackoff.Doubling), 40));
    }

    [Fact]
    public void A_jittered_pause_stays_within_half_and_one_and_a_half_of_its_length()
    {
        var meta = Meta(delay: 100, backoff: RetryBackoff.Jitter);

        var first = RetryPolicy.PauseMs(meta, 1, random: new Random(7));
        var again = RetryPolicy.PauseMs(meta, 1, random: new Random(7));

        Assert.Equal(first, again);
        Assert.InRange(first, 50, 150);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(100.0, true)]
    [InlineData(50.0, false)]
    [InlineData(49.0, false)]
    [InlineData(0.0, false)]
    public void A_pause_that_uses_all_the_time_left_is_refused(double? remaining, bool expected)
        => Assert.Equal(expected, RetryPolicy.CanAfford(50, remaining));
}
