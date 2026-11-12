using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A date written down is one value, not three numbers with two subtractions between them. Only the
/// one shape counts: a shorter year, a missing part, or something stuck to the end stays what it
/// was written as.
/// </summary>
public class ExpressionDateLiteralTests
{
    [Fact]
    public void A_date_is_one_value()
    {
        var value = Expression.Evaluate("2026-11-12");

        Assert.True(value.IsDate);
        Assert.Equal("2026-11-12", value.AsText());
    }

    [Fact]
    public void A_date_with_a_time_keeps_it()
        => Assert.Equal("2026-11-12 09:30:00",
            Expression.Evaluate("2026-11-12 09:30").AsText());

    [Theory]
    [InlineData("2026-11", "2015")]
    [InlineData("1-2", "-1")]
    public void A_year_that_is_not_four_digits_is_still_a_subtraction(string source, string expected)
        => Assert.Equal(expected, Expression.Evaluate(source).AsText());

    [Fact]
    public void A_name_beside_a_number_is_still_a_subtraction()
    {
        var bag = new VariableBag();
        bag.SetNumber("a", 5);

        Assert.Equal(4d, Expression.Evaluate("$a-1", bag).AsNumber());
    }

    [Fact]
    public void A_date_with_something_stuck_to_it_is_not_a_date()
        => Assert.DoesNotContain(ExpressionTokens.Scan("2026-11-12.csv"),
            token => token.Kind == ExpressionTokenKind.Date);

    [Fact]
    public void A_date_feeds_the_date_functions()
        => Assert.Equal("2026-11-13",
            Expression.Evaluate("dateAdd(2026-11-12, 1, \"days\")").AsText());

    [Fact]
    public void A_date_survives_the_round_trip_through_text_and_json()
    {
        var tree = FormulaText.Read("2026-11-12", ActionReadMode.WholeValue);

        Assert.IsType<DateNode>(tree);
        Assert.Equal("2026-11-12", FormulaText.Write(tree));
        Assert.Equal(tree, FormulaJson.Read(FormulaJson.Write(tree)));
    }
}
