using System;
using System.Globalization;

using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Two questions about a value that comparisons and sets ask: whether it means the same as another,
/// and the one line it stands for when a set looks it up. A line beside a number, a flag or a date
/// is read as that kind first, which is what makes <c>1 == "1.0"</c> read the way it looks.
/// </summary>
public class ValueCanonicalTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-11-04 12:30:00 +08:00", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    public void Two_numbers_are_equal_when_they_are(double left, double right, bool expected)
        => Assert.Equal(expected,
            ValueRules.Same(Value.FromNumber(left), Value.FromNumber(right)));

    [Fact]
    public void A_line_beside_a_number_flag_or_date_is_read_as_that_kind()
    {
        Assert.True(ValueRules.Same(Value.FromNumber(1), Value.FromText("1")));
        Assert.True(ValueRules.Same(Value.FromText("1"), Value.FromNumber(1)));
        Assert.True(ValueRules.Same(Value.FromText("1.0"), Value.FromNumber(1)));
        Assert.True(ValueRules.Same(Value.FromText("true"), Value.FromBool(true)));
        Assert.True(ValueRules.Same(Value.FromText("1"), Value.FromBool(true)));
        Assert.True(ValueRules.Same(Value.FromText("0"), Value.FromBool(false)));
        Assert.True(ValueRules.Same(Value.FromText("2026-11-04 12:30:00 +08:00"),
            Value.FromDate(Moment)));
    }

    [Fact]
    public void A_line_that_is_not_that_kind_and_two_lines_are_not_the_same()
    {
        Assert.False(ValueRules.Same(Value.FromText("abc"), Value.FromNumber(1)));
        Assert.False(ValueRules.Same(Value.FromText("1"), Value.FromText("1.0")));
        Assert.False(ValueRules.Same(Value.FromBool(true), Value.FromNumber(1)));
        Assert.False(ValueRules.Same(Value.FromImage("shot"), Value.FromText("shot")));
    }

    [Theory]
    [InlineData("z")]
    public void The_empty_value_has_a_line_of_its_own(string expected)
        => Assert.Equal(expected, ValueRules.Canonical(Value.Null));

    [Fact]
    public void A_number_a_flag_a_date_and_a_picture_write_the_lines_a_set_looks_them_up_by()
    {
        Assert.Equal("n:3", ValueRules.Canonical(Value.FromNumber(3)));
        Assert.Equal("b:1", ValueRules.Canonical(Value.FromBool(true)));
        Assert.Equal("b:0", ValueRules.Canonical(Value.FromBool(false)));
        Assert.Equal("d:" + Moment.UtcTicks.ToString(CultureInfo.InvariantCulture),
            ValueRules.Canonical(Value.FromDate(Moment)));
        Assert.Equal("i:shot", ValueRules.Canonical(Value.FromImage("SHOT")));
    }

    [Fact]
    public void A_line_written_like_a_number_or_a_flag_takes_that_line()
    {
        Assert.Equal("n:3", ValueRules.Canonical(Value.FromText("3")));
        Assert.Equal("n:1", ValueRules.Canonical(Value.FromText("1.0")));
        Assert.Equal("b:1", ValueRules.Canonical(Value.FromText("true")));

        // A digit is a number first: the flag branch never sees "0" or "1" because the number read
        // above claims them, which is the line a set looks them up by.
        Assert.Equal("n:1", ValueRules.Canonical(Value.FromText("1")));
        Assert.Equal("n:0", ValueRules.Canonical(Value.FromText("0")));
        Assert.Equal("t:abc", ValueRules.Canonical(Value.FromText("ABC")));
    }

    [Fact]
    public void A_list_and_a_record_write_their_pieces()
    {
        Assert.Equal("l:[n:1,t:a]",
            ValueRules.Canonical(Value.FromList([Value.FromNumber(1), Value.FromText("a")])));
        Assert.Equal("r:{a=n:1}",
            ValueRules.Canonical(Value.FromRecord([("a", Value.FromNumber(1))])));
    }

    [Fact]
    public void Values_that_mean_the_same_write_the_same_line()
    {
        foreach (var (left, right) in new (Value, Value)[]
                 {
                     (Value.FromNumber(1), Value.FromText("1")),
                     (Value.FromNumber(1), Value.FromText("1.0")),
                     (Value.FromText("true"), Value.FromBool(true)),
                 })
        {
            Assert.True(ValueRules.Same(left, right));
            Assert.Equal(ValueRules.Canonical(left), ValueRules.Canonical(right));
        }
    }

    [Fact]
    public void Every_kind_fits_the_kind_it_names_and_not_another()
    {
        Assert.True(ValueRules.Accepts(ValueKind.Text, Value.FromRecord([])));
        Assert.True(ValueRules.Accepts(ValueKind.List, Value.FromList([])));
        Assert.True(ValueRules.Accepts(ValueKind.Record, Value.FromRecord([])));
        Assert.False(ValueRules.Accepts(ValueKind.List, Value.FromRecord([])));
        Assert.False(ValueRules.Accepts(ValueKind.Record, Value.FromList([])));
        Assert.True(ValueRules.Accepts(ValueKind.Date, Value.FromDate(Moment)));
        Assert.False(ValueRules.Accepts(ValueKind.Date, Value.FromText("soon")));
    }
}
