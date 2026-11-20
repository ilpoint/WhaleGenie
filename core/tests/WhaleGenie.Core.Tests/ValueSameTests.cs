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
    [MemberData(nameof(A_value_is_compared_by_kind_and_a_line_is_read_as_the_other_sideCases))]
    public void A_value_is_compared_by_kind_and_a_line_is_read_as_the_other_side(
            ExpressionNode source, bool expected)
            => Assert.Equal(expected, Expression.Evaluate(source).AsBool());

    public static TheoryData<ExpressionNode, bool> A_value_is_compared_by_kind_and_a_line_is_read_as_the_other_sideCases() => new()
    {
        { Formula.Binary("==", Formula.Number("1", 1d), Formula.Text("1.0")), true },
        { Formula.Binary("==", Formula.Text("1"), Formula.Number("1", 1d)), true },
        { Formula.Binary("==", Formula.Text("1"), Formula.Text("1.0")), false },
        { Formula.Binary("==", Formula.Constant("true"), Formula.Text("TRUE")), true },
        { Formula.Binary("==", Formula.Constant("false"), Formula.Text("0")), true },
        { Formula.Binary("==", Formula.Call("parseDate", Formula.Text("2026-03-01"), Formula.Text("yyyy-MM-dd")), Formula.Text("2026-03-01")), true },
        { Formula.Binary("==", Formula.Call("parseDate", Formula.Text("2026-03-01"), Formula.Text("yyyy-MM-dd")), Formula.Text("2026-03-02")), false },
        { Formula.Binary("==", Formula.Number("1", 1d), Formula.Text("one")), false },
        { Formula.Binary("==", Formula.List(Formula.Number("1", 1d), Formula.Number("2", 2d)), Formula.List(Formula.Number("1", 1d), Formula.Number("2", 2d))), true },
        { Formula.Binary("==", Formula.List(Formula.Number("1", 1d), Formula.Number("2", 2d)), Formula.List(Formula.Number("1", 1d), Formula.Number("3", 3d))), false },
    };


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
            Expression.Evaluate(Formula.Call("unique", Formula.List(Formula.Number("1", 1d), Formula.Text("1"), Formula.Text("1.0"), Formula.Number("2", 2d)))).AsText());
}
