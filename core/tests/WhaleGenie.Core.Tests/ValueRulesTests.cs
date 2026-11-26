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

    /// <summary>The bool words a declaration or a comparison may write.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void A_bool_is_read_the_way_it_is_written(string text, bool expected)
    {
        Assert.True(ValueRules.Accepts(ValueKind.Bool, Value.FromText(text)));
        Assert.True(ValueRules.TryParse(text, ValueKind.Bool, out var value));
        Assert.Equal(ValueKind.Bool, value.Kind);
        Assert.Equal(expected, value.Flag);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("2")]
    public void Anything_else_is_not_a_bool(string text)
    {
        Assert.False(ValueRules.Accepts(ValueKind.Bool, Value.FromText(text)));
        Assert.False(ValueRules.TryParse(text, ValueKind.Bool, out _));
    }

    /// <summary>The date shapes a text field may hold, read the same way the expression language reads them.</summary>
    [Theory]
    [InlineData("2026-03-04")]
    [InlineData("2026-03-04 12:30:00")]
    [InlineData("2026/03/04 12:30")]
    public void A_date_is_read_the_way_it_is_written(string text)
    {
        Assert.True(ValueRules.Accepts(ValueKind.Date, Value.FromText(text)));
        Assert.True(ValueRules.TryParse(text, ValueKind.Date, out var value));
        Assert.Equal(ValueKind.Date, value.Kind);
    }

    [Fact]
    public void Anything_else_is_not_a_date()
    {
        Assert.False(ValueRules.Accepts(ValueKind.Date, Value.FromText("soon")));
        Assert.False(ValueRules.TryParse("soon", ValueKind.Date, out _));
    }

    /// <summary>A picture is a handle, and a line that reads like one is still only a line.</summary>
    [Fact]
    public void A_picture_is_only_a_picture()
    {
        Assert.True(ValueRules.Accepts(ValueKind.Image, Value.FromImage("shot")));
        Assert.False(ValueRules.Accepts(ValueKind.Image, Value.FromText("shot")));
        Assert.False(ValueRules.TryParse("shot", ValueKind.Image, out _));
    }

    /// <summary>A list and a record are not written as a line, so they cannot be read from one.</summary>
    [Fact]
    public void A_list_or_a_record_is_not_read_from_a_line()
    {
        Assert.False(ValueRules.TryParse("a", ValueKind.List, out _));
        Assert.False(ValueRules.TryParse("a", ValueKind.Record, out _));
        Assert.True(ValueRules.TryParse("a", ValueKind.Text, out var text));
        Assert.Equal("a", text.AsText());
    }

    /// <summary>Every kind has the empty value a declaration starts at.</summary>
    [Fact]
    public void Every_kind_starts_at_its_own_empty_value()
    {
        Assert.Equal(ValueKind.Number, ValueRules.Empty(ValueKind.Number).Kind);
        Assert.Equal(0, ValueRules.Empty(ValueKind.Number).AsNumber());
        Assert.False(ValueRules.Empty(ValueKind.Bool).AsBool());
        Assert.Empty(ValueRules.Empty(ValueKind.List).AsList());
        Assert.Equal(string.Empty, ValueRules.Empty(ValueKind.Image).AsText());
        Assert.Equal(string.Empty, ValueRules.Empty(ValueKind.Text).AsText());
    }

    [Fact]
    public void Nothing_written_yet_is_allowed_for_every_kind()
    {
        Assert.True(ValueRules.Accepts(ValueKind.Number, Value.Null));
        Assert.True(ValueRules.Accepts(ValueKind.List, Value.FromText(string.Empty)));
    }
}
