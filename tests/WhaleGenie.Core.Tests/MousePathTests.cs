using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The shape of a mouse move. A straight one stays on the line, a bent one bows out of it, and a
/// hand-like one drifts a little and eases in and out. The paths are checked as geometry, with no
/// screen anywhere near, which is what lets the feel of a move be tested at all.
/// </summary>
public class MousePathTests
{
    [Theory]
    [InlineData("", MouseRoute.Direct)]
    [InlineData("direct", MouseRoute.Direct)]
    [InlineData("Smooth", MouseRoute.Smooth)]
    [InlineData(" human ", MouseRoute.Human)]
    [InlineData("wobble", MouseRoute.Direct)]
    public void A_style_the_engine_does_not_know_travels_straight(string style, MouseRoute expected)
        => Assert.Equal(expected, MousePath.Route(style));

    [Fact]
    public void A_straight_path_is_the_line_between_the_two_points()
    {
        var from = new ScreenPoint(10, 20);
        var to = new ScreenPoint(210, 120);
        var path = MousePath.Plan(MouseRoute.Direct, from, to, 4);

        Assert.Equal(5, path.Count);
        Assert.Equal(from, path[0]);
        Assert.Equal(to, path[^1]);

        // Every stop sits on the line, which is what makes this the move the engine has always
        // made: nothing about a macro written before the styles existed changes.
        for (var index = 1; index <= 4; index++)
        {
            Assert.Equal(from.X + (50 * index), path[index].X);
            Assert.Equal(from.Y + (25 * index), path[index].Y);
        }
    }

    [Fact]
    public void A_bent_path_bows_out_of_the_line_and_still_lands_on_the_target()
    {
        var to = new ScreenPoint(400, 0);
        var path = MousePath.Plan(MouseRoute.Smooth, new ScreenPoint(0, 0), to, 20, new Random(1));

        Assert.Equal(to, path[^1]);

        // The bow is a share of the distance covered — 8% of 400 — and it is at its widest in
        // the middle, tapering back to the line at both ends.
        var bow = path.Max(point => Math.Abs(point.Y));
        Assert.True(bow >= 25, $"the path should bow out, bowed {bow}");
        Assert.True(bow <= 33, $"the bow should stay modest, bowed {bow}");
    }

    [Fact]
    public void A_very_long_move_is_not_bent_any_further_than_the_cap()
    {
        var path = MousePath.Plan(MouseRoute.Smooth, new ScreenPoint(0, 0), new ScreenPoint(5000, 0),
            40, new Random(2));

        Assert.True(path.Max(point => Math.Abs(point.Y)) <= 121);
    }

    [Fact]
    public void A_hand_like_path_eases_in_and_out_and_lands_on_the_target()
    {
        var to = new ScreenPoint(400, 0);
        var path = MousePath.Plan(MouseRoute.Human, new ScreenPoint(0, 0), to, 20, new Random(7));

        Assert.Equal(to, path[^1]);
        Assert.True(path.Max(point => Math.Abs(point.Y)) <= 125,
            "the drift on the arc should stay within a few pixels");

        // The pointer covers more ground in the middle of the move than at either end, which is
        // what a hand does: it starts moving and settles rather than snapping into motion.
        var opening = path[1].X - path[0].X;
        var middle = path[11].X - path[10].X;
        var closing = path[^1].X - path[^2].X;
        Assert.True(middle > opening, $"the middle should be quicker than the start: {middle} vs {opening}");
        Assert.True(middle > closing, $"the middle should be quicker than the finish: {middle} vs {closing}");
    }

    [Fact]
    public void A_bent_path_bulges_to_the_one_side_the_dice_picked()
    {
        // The side is the first thing the path asks its dice, so a path started on seed 1 must bow
        // to that side of the line and never cross back over it.
        var from = new ScreenPoint(0, 0);
        var to = new ScreenPoint(300, 300);
        var side = new Random(1).Next(2) == 0 ? 1 : -1;
        var path = MousePath.Plan(MouseRoute.Smooth, from, to, 20, new Random(1));

        Assert.Equal(0, Cross(from, to, path[0]));
        Assert.Equal(0, Cross(from, to, path[^1]));
        Assert.All(path.Where(point => Cross(from, to, point) != 0),
            point => Assert.Equal(side, Math.Sign(Cross(from, to, point))));
    }

    [Fact]
    public void A_bent_path_is_as_far_from_the_line_on_one_half_as_on_the_other()
    {
        var path = MousePath.Plan(MouseRoute.Smooth, new ScreenPoint(0, 0), new ScreenPoint(400, 0),
            20, new Random(3));

        // Rounding may take a pixel off either half, but the two halves are the same arc: a path
        // that eased with the wrong shape would not be symmetric this way.
        for (var index = 1; index < 10; index++)
        {
            Assert.InRange(Math.Abs(Math.Abs(path[index].Y) - Math.Abs(path[20 - index].Y)), 0, 1);
            Assert.True(Math.Abs(path[index].Y) >= Math.Abs(path[index - 1].Y),
                $"the arc should widen up to its middle: {index}");
            Assert.Equal(20 * index, path[index].X);
        }
    }

    [Fact]
    public void A_hand_like_path_stays_on_its_arc_within_a_few_pixels()
    {
        // Eight per cent of 400 is the widest a bent path bows; a hand adds at most three pixels of
        // drift either way, so nothing on the path may sit further from the line than that.
        var path = MousePath.Plan(MouseRoute.Human, new ScreenPoint(0, 0), new ScreenPoint(400, 0),
            20, new Random(7));

        Assert.All(path, point => Assert.InRange(Math.Abs(point.Y), 0, 36));
    }

    [Fact]
    public void A_move_that_goes_nowhere_is_the_point_itself()
    {
        var at = new ScreenPoint(42, 42);
        var path = MousePath.Plan(MouseRoute.Smooth, at, at, 10, new Random(1));

        Assert.Equal(11, path.Count);
        Assert.All(path, point => Assert.Equal(at, point));
    }

    /// <summary>Twice the area of the triangle the point makes with the line, whose sign is its side.</summary>
    private static long Cross(ScreenPoint from, ScreenPoint to, ScreenPoint at)
        => ((long)(to.X - from.X) * (at.Y - from.Y)) - ((long)(to.Y - from.Y) * (at.X - from.X));

    [Fact]
    public void A_bent_path_is_broken_into_finer_stops_than_a_straight_one()
    {
        Assert.Equal(20, MousePath.StepsFor(MouseRoute.Direct, 320));
        Assert.Equal(40, MousePath.StepsFor(MouseRoute.Smooth, 320));

        // Short moves still get enough stops for the arc to read as an arc, and nothing runs
        // away with the time even when the duration is huge.
        Assert.Equal(12, MousePath.StepsFor(MouseRoute.Human, 60));
        Assert.Equal(120, MousePath.StepsFor(MouseRoute.Smooth, 100_000));
    }
}
