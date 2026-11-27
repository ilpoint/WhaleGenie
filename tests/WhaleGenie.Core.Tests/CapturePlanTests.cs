using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Where a capture starts and how far it reaches: a corner written in screen pixels or counted from
/// the corner the step measures from, and a side that is either the number written or the rest of the
/// screen — never smaller than one pixel.
/// </summary>
public class CapturePlanTests
{
    [Fact]
    public void A_corner_with_no_origin_is_the_pixels_it_writes()
        => Assert.Equal(new ScreenPoint(10, 20), CapturePlan.Placed(null, 10, 20));

    [Fact]
    public void A_corner_with_an_origin_is_counted_from_it()
        => Assert.Equal(new ScreenPoint(115, 220),
            CapturePlan.Placed(new ScreenPoint(100, 200), 15, 20));

    [Fact]
    public void A_side_the_step_wrote_is_that_many_pixels()
        => Assert.Equal(30, CapturePlan.Reach(given: true, ActionEmptyMeans.ToEdge, 30, 100, 800));

    [Fact]
    public void A_side_left_empty_reaches_the_edge_of_the_screen()
        => Assert.Equal(700, CapturePlan.Reach(given: false, ActionEmptyMeans.ToEdge, 0, 100, 800));

    [Fact]
    public void A_side_left_empty_that_does_not_mean_the_edge_is_nothing_to_capture()
    {
        Assert.Equal(1, CapturePlan.Reach(given: false, ActionEmptyMeans.Zero, 0, 100, 800));
        Assert.Equal(1, CapturePlan.Reach(given: false, ActionEmptyMeans.Unset, 0, 100, 800));
    }

    [Fact]
    public void A_side_is_never_smaller_than_one_pixel()
    {
        Assert.Equal(1, CapturePlan.Reach(given: true, ActionEmptyMeans.Zero, 0, 0, 800));
        Assert.Equal(1, CapturePlan.Reach(given: true, ActionEmptyMeans.Zero, -5, 0, 800));
        Assert.Equal(1, CapturePlan.Reach(given: false, ActionEmptyMeans.ToEdge, 0, 900, 800));
    }
}
