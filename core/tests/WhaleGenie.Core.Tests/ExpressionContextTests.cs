using System;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The clock and the dice an expression is worked out against. A formula that reads "now" has to
/// answer the same twice for a check to be able to state what it expects, and a date written as a
/// time of day still lands on the day the caller is looking at rather than on the machine's.
/// </summary>
public class ExpressionContextTests
{
    private static readonly DateTimeOffset NoonInShanghai =
        new(2026, 3, 4, 12, 30, 45, TimeSpan.FromHours(8));

    private static EvaluationContext AtNoon() => new(new FixedClock(NoonInShanghai), new Random(11));

    /// <summary>A clock that answers one moment, in one offset, whatever the machine's own says.</summary>
    private sealed class FixedClock(DateTimeOffset moment) : TimeProvider
    {
        private static readonly TimeZoneInfo Zone =
            TimeZoneInfo.CreateCustomTimeZone("Fixed+08", TimeSpan.FromHours(8), "Fixed+08", "Fixed+08");

        public override DateTimeOffset GetUtcNow() => moment.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => Zone;
    }

    [Fact]
    public void Now_is_the_moment_the_context_was_given()
    {
        var value = Expression.Evaluate(Formula.Call("now"), null, AtNoon());

        Assert.Equal(ValueKind.Date, value.Kind);
        Assert.Equal("2026-03-04 12:30:45", value.AsText());
    }

    [Fact]
    public void Today_is_that_day_with_no_time_on_it()
    {
        var value = Expression.Evaluate(Formula.Call("today"), null, AtNoon());

        Assert.Equal("2026-03-04", value.AsText());
    }

    [Fact]
    public void A_date_left_empty_is_read_as_the_context_moment()
    {
        var value = Expression.Evaluate(
            Formula.Call("dateAdd", Formula.Text(string.Empty), Formula.Number(1), Formula.Text("day")),
            null, AtNoon());

        Assert.Equal("2026-03-05 12:30:45", value.AsText());
    }

    [Fact]
    public void A_time_of_day_lands_on_the_context_day()
    {
        var value = Expression.Evaluate(
            Formula.Call("dateAdd", Formula.Text("09:30"), Formula.Number(0), Formula.Text("minutes")),
            null, AtNoon());

        Assert.Equal("2026-03-04 09:30:00", value.AsText());
    }

    [Fact]
    public void The_dice_handed_in_are_the_ones_rolled()
    {
        var first = new EvaluationContext(new FixedClock(NoonInShanghai), new Random(7));
        var second = new EvaluationContext(new FixedClock(NoonInShanghai), new Random(7));

        foreach (var call in new[]
                 {
                     Formula.Call("random"),
                     Formula.Call("random", Formula.Number(1000)),
                     Formula.Call("random", Formula.Number(3), Formula.Number(7)),
                 })
        {
            Assert.Equal(
                Expression.Evaluate(call, null, first).AsText(),
                Expression.Evaluate(call, null, second).AsText());
        }
    }

    [Fact]
    public void A_bounded_roll_stays_inside_its_bounds()
    {
        var context = AtNoon();

        for (var turn = 0; turn < 50; turn++)
        {
            var value = Expression.Evaluate(
                Formula.Call("random", Formula.Number(3), Formula.Number(7)), null, context)
                .AsNumber();

            Assert.InRange(value, 3, 7);
            Assert.Equal(Math.Floor(value), value);
        }
    }

    [Fact]
    public void Without_a_context_the_machine_still_answers()
    {
        var value = Expression.Evaluate(Formula.Call("now"));

        Assert.Equal(ValueKind.Date, value.Kind);
    }
}
