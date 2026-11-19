using System;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A date is a value kind with its own order and its own text, not a line that happens to look
/// like a day. What it is written as has to survive a trip through JSON, or a persistent global
/// would come back as text the next time the program started.
/// </summary>
public class ValueDateTests
{
    [Fact]
    public void A_date_is_not_a_number()
    {
        var date = Value.FromDate(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<ExpressionException>(() => date.AsNumber());
    }

    [Fact]
    public void A_day_with_no_time_says_only_the_day()
    {
        var day = Value.FromDate(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var moment = Value.FromDate(new DateTimeOffset(2024, 3, 1, 9, 30, 15, TimeSpan.Zero));

        Assert.Equal("2024-03-01", day.AsText());
        Assert.Equal("2024-03-01 09:30:15", moment.AsText());
    }

    [Fact]
    public void Two_offsets_of_one_instant_are_the_same_moment()
    {
        var east = Value.FromDate(new DateTimeOffset(2024, 3, 1, 9, 0, 0, TimeSpan.FromHours(8)));
        var utc = Value.FromDate(new DateTimeOffset(2024, 3, 1, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal(utc, east);
        Assert.Equal(utc.GetHashCode(), east.GetHashCode());
    }

    [Fact]
    public void A_date_survives_a_round_trip_through_json()
    {
        var date = Value.FromDate(new DateTimeOffset(2024, 3, 1, 9, 30, 0, TimeSpan.FromHours(8)));

        var read = JsonValues.FromJson(JsonValues.Node(date));

        Assert.True(read.IsDate);
        Assert.Equal(date.Date.UtcTicks, read.Date.UtcTicks);
    }

    /// <summary>
    /// 存下来的时刻带着它的偏移：JSON 里没有「日期」这种类型，写成一个不带偏移的时间，换一台机器
    /// 或换一个时区打开，读到的就是别的那一刻。这条钉住写出去的形状，免得有人又换回给人读的那一行。
    /// </summary>
    [Fact]
    public void A_saved_moment_is_written_with_the_offset_it_was_read_at()
    {
        var date = Value.FromDate(new DateTimeOffset(2024, 3, 1, 9, 30, 0, TimeSpan.FromHours(5.5)));

        // The stamp is read as a string, not as the text of the whole document: JSON escapes a
        // plus sign, so looking for it in the document would find an escape rather than the offset.
        var stamp = Assert.IsType<JsonObject>(JsonValues.Node(date))["$date"]!.GetValue<string>();
        Assert.Contains("+05:30", stamp, StringComparison.Ordinal);

        var read = JsonValues.FromJson(JsonValues.Node(date));
        Assert.Equal(date.Date.UtcTicks, read.Date.UtcTicks);
        Assert.Equal(date.Date.Offset, read.Date.Offset);
    }

    [Fact]
    public void A_record_with_a_date_field_keeps_the_date()
    {
        var record = Value.FromRecord([("due", Value.FromDate(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero)))]);

        var read = JsonValues.FromJson(JsonValues.Node(record));

        Assert.True(read.IsRecord);
        Assert.True(read.Field("due")!.IsDate);
    }

    /// <summary>An object that merely has a <c>$date</c> field is still a record.</summary>
    [Fact]
    public void Only_a_lone_date_field_is_read_as_a_moment()
    {
        var record = Value.FromRecord(
        [
            ("date", Value.FromText("2024-03-01")),
            ("note", Value.FromText("x")),
        ]);

        var read = JsonValues.FromJson(JsonValues.Node(record));

        Assert.True(read.IsRecord);
        Assert.False(read.IsDate);
    }
}
