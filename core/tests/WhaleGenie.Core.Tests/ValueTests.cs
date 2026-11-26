using System;

using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The value model itself: what each kind reads as when it is asked for another, which two values
/// are the same value, and how a number is written out.
/// </summary>
public class ValueTests
{
    [Fact]
    public void A_picture_and_a_text_keep_an_empty_line_rather_than_nothing()
    {
        Assert.Equal(ValueKind.Image, Value.FromImage(null!).Kind);
        Assert.Equal(string.Empty, Value.FromImage(null!).Text);

        Assert.Equal(ValueKind.Text, Value.FromText(null!).Kind);
        Assert.Equal(string.Empty, Value.FromText(null!).Text);
    }

    [Fact]
    public void A_record_answers_for_a_field_by_any_case()
    {
        var record = Value.FromRecord([("Age", Value.FromNumber(3))]);

        Assert.True(record.HasField("age"));
        Assert.False(record.HasField("name"));
        Assert.Equal(3, record.Field("AGE")!.AsNumber());
        Assert.Null(record.Field("name"));
        Assert.Single(record.Fields);
    }

    [Fact]
    public void A_bool_reads_as_one_or_zero()
    {
        Assert.Equal(1, Value.FromBool(true).AsNumber());
        Assert.Equal(0, Value.FromBool(false).AsNumber());
    }

    [Theory]
    [InlineData(ValueKind.List)]
    [InlineData(ValueKind.Record)]
    [InlineData(ValueKind.Date)]
    [InlineData(ValueKind.Image)]
    public void A_kind_that_has_no_size_is_not_a_number(ValueKind kind)
    {
        var value = kind switch
        {
            ValueKind.List => Value.FromList([]),
            ValueKind.Record => Value.FromRecord([]),
            ValueKind.Date => Value.FromDate(DateTimeOffset.UnixEpoch),
            _ => Value.FromImage("shot"),
        };

        var failure = Assert.Throws<ExpressionException>(() => value.AsNumber());
        Assert.Contains("is not a number", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_is_true_or_false_by_its_own_kind()
    {
        Assert.False(Value.Null.AsBool());
        Assert.False(Value.FromText(string.Empty).AsBool());
        Assert.False(Value.FromText("false").AsBool());
        Assert.False(Value.FromText("0").AsBool());
        Assert.True(Value.FromText("yes").AsBool());
        Assert.False(Value.FromNumber(0).AsBool());
        Assert.True(Value.FromNumber(1).AsBool());
        Assert.False(Value.FromList([]).AsBool());
        Assert.True(Value.FromList([Value.Null]).AsBool());
        Assert.False(Value.FromRecord([]).AsBool());
        Assert.True(Value.FromRecord([("a", Value.Null)]).AsBool());
        Assert.True(Value.FromDate(DateTimeOffset.UnixEpoch).AsBool());
        Assert.True(Value.FromImage("shot").AsBool());
    }

    [Fact]
    public void A_moment_is_read_back_as_a_moment_and_a_line_only_when_it_is_written_like_one()
    {
        var moment = DateTimeOffset.Parse("2026-03-04 12:30:00 +08:00");

        Assert.True(Value.FromDate(moment).TryDate(out var read));
        Assert.Equal(moment, read);
        Assert.True(Value.FromText("2026-03-04").TryDate(out _));
        Assert.False(Value.FromText("soon").TryDate(out _));
        Assert.False(Value.FromNumber(3).TryDate(out _));
        Assert.False(Value.FromList([]).TryDate(out _));
        Assert.False(Value.FromRecord([]).TryDate(out _));
        Assert.False(Value.Null.TryDate(out _));
    }

    [Fact]
    public void A_value_is_equal_only_to_one_of_its_own_kind_and_shape()
    {
        Assert.True(Value.FromImage("Shot").Equals(Value.FromImage("shot")));
        Assert.False(Value.FromNumber(1).Equals(Value.FromText("1")));
        Assert.False(Value.FromNumber(1).Equals(Value.FromNumber(2)));
        Assert.False(Value.FromNumber(1).Equals(null));

        Assert.True(Value.FromDate(DateTimeOffset.Parse("2026-03-04 12:30:00 +08:00"))
            .Equals(Value.FromDate(DateTimeOffset.Parse("2026-03-04 04:30:00 +00:00"))));

        Assert.False(Value.FromRecord([("a", Value.Null)])
            .Equals(Value.FromRecord([("A", Value.Null)])));
        Assert.True(Value.FromRecord([("a", Value.Null)])
            .Equals(Value.FromRecord([("a", Value.Null)])));
    }

    [Fact]
    public void Two_equal_values_hash_alike()
    {
        Assert.Equal(
            Value.FromDate(DateTimeOffset.Parse("2026-03-04 12:30:00 +08:00")).GetHashCode(),
            Value.FromDate(DateTimeOffset.Parse("2026-03-04 04:30:00 +00:00")).GetHashCode());

        Assert.Equal(
            Value.FromNumber(3).GetHashCode(),
            Value.FromText("3").GetHashCode());
    }

    [Fact]
    public void A_number_is_written_without_a_trailing_zero_or_scientific_notation()
    {
        Assert.Equal("3", Value.FormatNumber(3));
        Assert.Equal("3.5", Value.FormatNumber(3.5));
        Assert.Equal("-0.25", Value.FormatNumber(-0.25));
        Assert.Equal("NaN", Value.FormatNumber(double.NaN));
        Assert.Equal("Infinity", Value.FormatNumber(double.PositiveInfinity));
        Assert.Equal("10000000000000000", Value.FormatNumber(1e16));
    }

    [Fact]
    public void A_list_and_a_record_write_themselves_in_the_words_a_person_reads()
    {
        Assert.Equal("a, b", Value.FromList([Value.FromText("a"), Value.FromText("b")]).AsText());
        Assert.Equal("{\"a\":1}", Value.FromRecord([("a", Value.FromNumber(1))]).AsText());
        Assert.Equal("shot", Value.FromImage("shot").AsText());
        Assert.Equal(string.Empty, Value.Null.AsText());
    }
}
