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
