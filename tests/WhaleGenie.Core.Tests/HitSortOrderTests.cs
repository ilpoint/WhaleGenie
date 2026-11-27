using System.Collections.Generic;
using System.Linq;

using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The orders a look may count its hits in: the four directions of reading, nearest a point, and the
/// weighted mix of likeness and size. Equal ranks fall back to reading order, so the same set counted
/// twice counts the same way.
/// </summary>
public class HitSortOrderTests
{
    private static ImageMatch Hit(int x, int y, int width = 4, int height = 4, double score = 0.9)
        => new(score, new ScreenPoint(x, y), new ScreenSize(width, height));

    private static List<(int X, int Y)> Places(List<ImageMatch> hits)
        => [.. hits.Select(hit => (hit.Location.X, hit.Location.Y))];

    private static List<ImageMatch> Sorted(List<ImageMatch> hits, HitOrder order)
    {
        HitSort.Sort(hits, order);
        return hits;
    }

    private static HitOrder Order(MatchOrder kind, int weight = 50, ScreenPoint near = default)
        => new(kind, near, weight);

    [Fact]
    public void Reading_order_goes_down_the_screen_and_then_across()
    {
        var hits = new List<ImageMatch> { Hit(10, 20), Hit(30, 10), Hit(5, 20) };

        Assert.Equal([(30, 10), (5, 20), (10, 20)],
            Places(Sorted(hits, Order(MatchOrder.Reading))));
    }

    [Fact]
    public void Reading_up_starts_at_the_bottom()
    {
        var hits = new List<ImageMatch> { Hit(10, 20), Hit(30, 10), Hit(5, 20) };

        Assert.Equal([(5, 20), (10, 20), (30, 10)],
            Places(Sorted(hits, Order(MatchOrder.ReadingUp))));
    }

    [Fact]
    public void Reading_back_starts_at_the_right()
    {
        var hits = new List<ImageMatch> { Hit(10, 20), Hit(30, 10), Hit(5, 20) };

        Assert.Equal([(30, 10), (10, 20), (5, 20)],
            Places(Sorted(hits, Order(MatchOrder.ReadingBack))));
    }

    [Fact]
    public void Reading_back_up_starts_at_the_bottom_right()
    {
        var hits = new List<ImageMatch> { Hit(10, 20), Hit(30, 10), Hit(5, 20) };

        Assert.Equal([(10, 20), (5, 20), (30, 10)],
            Places(Sorted(hits, Order(MatchOrder.ReadingBackUp))));
    }

    [Fact]
    public void Near_puts_the_hit_closest_to_the_point_first()
    {
        // The point is below both hits, so the one that is lower and to the right is the closer one;
        // adding where the point is rather than subtracting it would put the other one first.
        var hits = new List<ImageMatch> { Hit(100, 10), Hit(20, 60) };

        Assert.Equal([(20, 60), (100, 10)],
            Places(Sorted(hits, Order(MatchOrder.Near, near: new ScreenPoint(0, 100)))));
    }

    [Fact]
    public void The_weight_decides_how_much_likeness_counts_and_how_much_size_does()
    {
        var sharp = Hit(10, 10, width: 2, height: 2, score: 0.9);
        var big = Hit(30, 10, width: 10, height: 10, score: 0.4);

        Assert.Equal([(10, 10), (30, 10)],
            Places(Sorted([sharp, big], Order(MatchOrder.Combined, weight: 100))));
        Assert.Equal([(30, 10), (10, 10)],
            Places(Sorted([sharp, big], Order(MatchOrder.Combined, weight: 0))));
    }

    [Fact]
    public void A_size_is_the_area_of_the_hit_rather_than_one_of_its_sides()
    {
        var tall = Hit(10, 10, width: 10, height: 1);
        var square = Hit(30, 10, width: 6, height: 6);

        Assert.Equal([(30, 10), (10, 10)],
            Places(Sorted([tall, square], Order(MatchOrder.Combined, weight: 0))));
    }

    [Fact]
    public void A_set_with_no_score_to_weigh_falls_back_to_size()
    {
        var small = Hit(10, 10, width: 2, height: 2, score: 0);
        var big = Hit(30, 10, width: 6, height: 6, score: 0);

        // No score is above zero, so likeness contributes nothing and the size decides; the same
        // weight given to a set that does have scores would have let them decide.
        Assert.Equal([(30, 10), (10, 10)],
            Places(Sorted([small, big], Order(MatchOrder.Combined, weight: 50))));
    }

    [Fact]
    public void A_set_of_nothing_or_one_is_left_alone()
    {
        Assert.Empty(Sorted([], Order(MatchOrder.Reading)));
        Assert.Single(Sorted([Hit(10, 10)], Order(MatchOrder.Random)));
    }

    [Theory]
    [InlineData(MatchOrder.ReadingUp, true)]
    [InlineData(MatchOrder.ReadingBack, true)]
    [InlineData(MatchOrder.ReadingBackUp, true)]
    [InlineData(MatchOrder.Reading, false)]
    [InlineData(MatchOrder.Near, false)]
    [InlineData(MatchOrder.Combined, false)]
    public void The_orders_that_count_from_a_far_side_are_the_ones_that_walk_back(
        MatchOrder kind, bool expected)
        => Assert.Equal(expected, HitOrder.CountsFromTheFarSide(kind));

    [Fact]
    public void Nearest_the_middle_of_the_searched_areas_is_the_middle_of_them()
    {
        var step = new ExecutableStep
        {
            Type = "vision.findImage",
            Parameters = [Fields.Text("orderBy", "centre")],
        };
        var args = new StepArguments(step, new VariableStore(), 16, EvaluationContext.System);
        var area = (new ImageFrame(100, 80, new byte[100 * 80 * 4]), new ScreenPoint(10, 10));

        var order = HitOrder.Of(args, () => new ScreenPoint(0, 0), [area]);

        Assert.Equal(MatchOrder.Near, order.Kind);
        Assert.Equal(new ScreenPoint(60, 50), order.Near);
        Assert.Equal(HitOrder.UsualScoreWeight, order.ScoreWeight);
    }

    [Fact]
    public void Nearest_with_no_area_is_measured_from_nothing()
    {
        var step = new ExecutableStep
        {
            Type = "vision.findImage",
            Parameters = [Fields.Text("orderBy", "centre")],
        };
        var args = new StepArguments(step, new VariableStore(), 16, EvaluationContext.System);

        Assert.Equal(default, HitOrder.Of(args, () => null, []).Near);
    }
}
