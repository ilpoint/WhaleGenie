using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A date is its own shape, not three numbers with two subtractions between them. It keeps the
/// moment it was written as, feeds the date functions, and survives a trip through the file.
/// </summary>
public class ExpressionDateLiteralTests
{
    [Fact]
    public void A_date_is_one_value()
    {
        var value = Expression.Evaluate(Formula.Date("2026-11-12"));

        Assert.True(value.IsDate);
        Assert.Equal("2026-11-12", value.AsText());
    }

    [Fact]
    public void A_date_with_a_time_keeps_it()
        => Assert.Equal("2026-11-12 09:30:00",
            Expression.Evaluate(Formula.Date("2026-11-12 09:30")).AsText());

    [Fact]
    public void A_date_feeds_the_date_functions()
        => Assert.Equal("2026-11-13",
            Expression.Evaluate(Formula.Call("dateAdd", Formula.Date("2026-11-12"),
                Formula.Number(1), Formula.Text("days"))).AsText());

    [Fact]
    public void A_date_survives_the_round_trip_through_json()
    {
        var tree = Formula.Date("2026-11-12");

        Assert.IsType<DateNode>(tree);
        Assert.Equal("2026-11-12", Formula.Word(tree));
        Assert.Equal(tree, FormulaJson.Read(FormulaJson.Write(tree)));
    }
}
