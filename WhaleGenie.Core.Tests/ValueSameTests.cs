using System;
using WhaleGenie.Core.Expressions;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Two questions that used to be one: whether two values are the same value, and whether a line can
/// stand for one. Equality is by kind; comparison reads a line as the other side's kind first, so
/// <c>1 == "1.0"</c> and <c>today() == "2026-11-12"</c> look the way they read.
/// </summary>
public class ValueSameTests
{
    [Theory]
    [InlineData("1 == \"1.0\"", true)]
    [InlineData("\"1\" == 1", true)]
    [InlineData("\"1\" == \"1.0\"", false)]
    [InlineData("true == \"TRUE\"", true)]
    [InlineData("false == \"0\"", true)]
    [InlineData("parseDate(\"2026-03-01\", \"yyyy-MM-dd\") == \"2026-03-01\"", true)]
    [InlineData("parseDate(\"2026-03-01\", \"yyyy-MM-dd\") == \"2026-03-02\"", false)]
    [InlineData("1 == \"one\"", false)]
    [InlineData("[1, 2] == [1, 2]", true)]
    [InlineData("[1, 2] == [1, 3]", false)]
    public void A_value_is_compared_by_kind_and_a_line_is_read_as_the_other_side(
        string source, bool expected)
        => Assert.Equal(expected, Expression.Evaluate(source).AsBool());

    /// <summary>
    /// Two values that are the same value hash alike, which is what lets a set answer the same
    /// question a comparison does — even when one of them was written with a different offset.
    /// </summary>
    [Fact]
    public void Equal_values_hash_alike()
    {
        var east = Value.FromDate(new DateTimeOffset(2024, 3, 1, 9, 0, 0, TimeSpan.FromHours(8)));
        var utc = Value.FromDate(new DateTimeOffset(2024, 3, 1, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal(east, utc);
        Assert.Equal(east.GetHashCode(), utc.GetHashCode());

        var first = Value.FromList([east, Value.FromText("X")]);
        var second = Value.FromList([utc, Value.FromText("x")]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Unique_uses_the_same_rule_as_equality()
        => Assert.Equal("1, 2",
            Expression.Evaluate("unique([1, \"1\", \"1.0\", 2])").AsText());
}
