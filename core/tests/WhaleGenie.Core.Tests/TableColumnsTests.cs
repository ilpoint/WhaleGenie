using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What each cell of a row is called: the name written above the column, or the letter of its place.
/// A blank column and one that repeats a name already taken keep their letter, so a record never
/// loses a cell to a name it cannot reach.
/// </summary>
public class TableColumnsTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void A_place_is_written_the_way_a_person_names_a_column(int place, string expected)
        => Assert.Equal(expected, TableColumns.Letter(place));

    [Fact]
    public void A_written_name_is_kept_and_a_blank_one_becomes_its_letter()
    {
        var names = TableColumns.Names([Value.FromText(" id "), Value.FromText(string.Empty)], 3);

        Assert.Equal(["id", "B", "C"], names);
    }

    [Fact]
    public void A_name_taken_twice_gives_the_second_column_its_letter()
    {
        var names = TableColumns.Names(
            [Value.FromText("id"), Value.FromText(string.Empty), Value.FromText("ID"),
             Value.FromText("x")], 4);

        Assert.Equal(["id", "B", "C", "x"], names);
    }

    [Fact]
    public void A_row_with_no_header_names_every_column_by_its_place()
        => Assert.Equal(["A", "B"], TableColumns.Names(null, 2));

    [Fact]
    public void A_header_shorter_than_the_row_names_the_rest_by_place()
        => Assert.Equal(["a", "B", "C"], TableColumns.Names([Value.FromText("a")], 3));

    [Fact]
    public void A_header_longer_than_the_row_is_not_read_past_the_row()
        => Assert.Equal(["a", "b"],
            TableColumns.Names([Value.FromText("a"), Value.FromText("b"), Value.FromText("c")], 2));

    [Fact]
    public void A_header_of_another_kind_is_named_by_what_it_says()
        => Assert.Equal(["3"], TableColumns.Names([Value.FromNumber(3)], 1));
}
