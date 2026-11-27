using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How many places a walk that counts where it finds them collects: the whole list when the step keeps
/// every one or counts from a corner the walk does not start in, and only as far as the place it named
/// otherwise. A step that takes the first red pixel pays for the first red pixel and no more.
/// </summary>
public class MatchQuotaTests
{
    [Fact]
    public void A_step_that_keeps_every_place_has_to_collect_them_all()
        => Assert.Equal(RunLimits.Matches,
            MatchQuota.Of(every: true, MatchOrder.Reading, index: 3));

    [Theory]
    [InlineData(MatchOrder.ReadingUp)]
    [InlineData(MatchOrder.ReadingBack)]
    [InlineData(MatchOrder.ReadingBackUp)]
    [InlineData(MatchOrder.Near)]
    [InlineData(MatchOrder.Random)]
    public void An_order_that_counts_from_the_far_side_or_at_random_wants_them_all(MatchOrder kind)
        => Assert.Equal(RunLimits.Matches, MatchQuota.Of(every: false, kind, index: 1));

    [Theory]
    [InlineData(MatchOrder.Reading, 1)]
    [InlineData(MatchOrder.Reading, 3)]
    [InlineData(MatchOrder.Combined, 2)]
    [InlineData(MatchOrder.Score, 5)]
    [InlineData(MatchOrder.Area, 7)]
    public void A_walk_that_counts_as_it_goes_stops_at_the_place_named(MatchOrder kind, int index)
        => Assert.Equal(index, MatchQuota.Of(every: false, kind, index));
}
