using WhaleGenie.Core.Expressions;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// One grammar decides what a line read as a number is worth, and the same answer comes back from
/// reading the line, judging whether a value fits a number, and working a text value out as one.
/// A comma separates items in this language, so it is not a thousands mark.
/// </summary>
public class ValueRulesTests
{
    [Theory]
    [InlineData("3", 3)]
    [InlineData("-5", -5)]
    [InlineData("3.5", 3.5)]
    [InlineData("1e3", 1000)]
    [InlineData(" 42 ", 42)]
    public void A_number_is_read_the_way_it_is_written(string text, double expected)
    {
        Assert.True(ValueRules.TryNumber(text, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("1 000")]
    [InlineData("5 kg")]
    [InlineData("")]
    public void Anything_that_is_not_one_number_is_not_read_as_one(string text)
        => Assert.False(ValueRules.TryNumber(text, out _));

    [Fact]
    public void Reading_a_value_as_a_number_and_judging_it_agree()
    {
        var written = Value.FromText("1,000");

        Assert.False(ValueRules.Accepts(ValueKind.Number, written));
        Assert.Throws<ExpressionException>(() => written.AsNumber());

        var plain = Value.FromText("1000");
        Assert.True(ValueRules.Accepts(ValueKind.Number, plain));
        Assert.Equal(1000, plain.AsNumber());
    }

    /// <summary>Two values that differ only in how they are written land on one canonical line.</summary>
    [Fact]
    public void A_number_and_the_line_that_writes_it_are_one_value()
    {
        Assert.Equal(ValueRules.Canonical(Value.FromNumber(1)),
            ValueRules.Canonical(Value.FromText("1")));
        Assert.Equal(ValueRules.Canonical(Value.FromNumber(1)),
            ValueRules.Canonical(Value.FromText("1.0")));
    }

    [Fact]
    public void Nothing_written_yet_is_allowed_for_every_kind()
    {
        Assert.True(ValueRules.Accepts(ValueKind.Number, Value.Null));
        Assert.True(ValueRules.Accepts(ValueKind.List, Value.FromText(string.Empty)));
    }
}
