using System;
using System.Collections.Generic;
using System.Linq;

using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The dice a run throws: the drift added to a click, the give on a wait, the order a set of hits is
/// counted in, and the shape of a hand-like move. All of them are the run's own, so a check that
/// starts a run with a fixed die gets the same answers twice.
/// </summary>
public class RunDiceTests
{
    private static EvaluationContext Dice(int seed)
        => new(TimeProvider.System, new Random(seed));

    [Fact]
    public void A_wait_bent_by_the_step_is_bent_by_the_runs_own_dice()
    {
        var step = new ExecutableStep
        {
            Type = "input.keyPress",
            Parameters = [Fields.Text("holdMs", "500", jitter: 0.2m)],
        };

        int Bent(int seed)
            => new StepArguments(step, new VariableStore(), 16, Dice(seed)).Number("holdMs");

        Assert.Equal(Bent(7), Bent(7));

        var values = Enumerable.Range(1, 6).Select(Bent).ToList();
        Assert.All(values, value => Assert.InRange(value, 400, 600));
        Assert.True(values.Distinct().Count() > 1, "six different dice gave one answer: " + string.Join(", ", values));
    }

    [Fact]
    public void A_click_spread_over_a_button_is_spread_by_the_runs_own_dice()
    {
        (ScreenPoints Points, ExecutableStep Step) Subject(int seed)
        {
            var devices = new FakeDeviceLayer();
            var variables = new VariableStore();
            var context = Dice(seed);
            var windows = new WindowFacts(devices, variables, new RunState(), context);
            var points = new ScreenPoints(devices, variables, windows, context);
            var step = new ExecutableStep
            {
                Type = "input.mouseClick",
                Parameters = [Fields.Text("variance", "10")],
            };
            return (points, step);
        }

        var (first, step) = Subject(7);
        var (again, _) = Subject(7);
        var centre = new ScreenPoint(100, 100);
        Assert.Equal(first.Scatter(step, centre), again.Scatter(step, centre));

        var spread = Enumerable.Range(0, 8).Select(_ => first.Scatter(step, centre)).ToList();
        Assert.All(spread, point =>
        {
            Assert.InRange(point.X, 90, 110);
            Assert.InRange(point.Y, 90, 110);
        });
    }

    [Fact]
    public void Counting_a_set_in_a_random_order_is_shuffled_by_the_runs_own_dice()
    {
        static List<ImageMatch> Hits() =>
        [
            new(0.9, new ScreenPoint(10, 10), new ScreenSize(4, 4)),
            new(0.9, new ScreenPoint(20, 10), new ScreenSize(4, 4)),
            new(0.9, new ScreenPoint(30, 10), new ScreenSize(4, 4)),
            new(0.9, new ScreenPoint(40, 10), new ScreenSize(4, 4)),
            new(0.9, new ScreenPoint(50, 10), new ScreenSize(4, 4)),
            new(0.9, new ScreenPoint(60, 10), new ScreenSize(4, 4)),
        ];

        var order = new HitOrder(MatchOrder.Random, default, 50);

        List<int> Sorted(int seed)
        {
            var hits = Hits();
            HitSort.Sort(hits, order, new Random(seed));
            return [.. hits.Select(hit => hit.Location.X)];
        }

        Assert.Equal(Sorted(7), Sorted(7));
        Assert.Contains(Enumerable.Range(1, 8), seed => !Sorted(seed).SequenceEqual(Sorted(7)));
    }

    [Fact]
    public void A_hand_like_move_is_shaped_by_the_runs_own_dice()
    {
        var from = new ScreenPoint(10, 10);
        var to = new ScreenPoint(200, 90);

        var first = MousePath.Plan(MouseRoute.Human, from, to, 20, new Random(7));
        var again = MousePath.Plan(MouseRoute.Human, from, to, 20, new Random(7));

        Assert.Equal(first, again);
        Assert.Equal(from, first[0]);
        Assert.Equal(to, first[^1]);
    }
}
