using System;

using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The table reader and writer: the quotes a cell may hold, the line breaks that belong to a cell,
/// the separator a step named or asked to be worked out, and the blanks a reader may keep.
/// </summary>
public class DelimitedFileTests
{
    [Theory]
    [InlineData("semicolon", ";")]
    [InlineData(";", ";")]
    [InlineData("tab", "\t")]
    [InlineData("\\t", "\t")]
    [InlineData("pipe", "|")]
    [InlineData("|", "|")]
    [InlineData("space", " ")]
    [InlineData(" ", ",")]
    [InlineData("\t", ",")]
    [InlineData("comma", ",")]
    [InlineData(",", ",")]
    [InlineData("", ",")]
    [InlineData("auto", ",")]
    [InlineData("~", "~")]
    [InlineData("nobody-writes-this", ",")]
    public void A_separator_is_read_the_way_it_is_written(string written, string expected)
        => Assert.Equal(expected, DelimitedFile.Named(written));

    [Theory]
    [InlineData("auto", true)]
    [InlineData("AUTO", true)]
    [InlineData(" auto ", true)]
    [InlineData("comma", false)]
    [InlineData("", false)]
    public void Asking_for_the_file_to_say_which_separator_it_used(string written, bool expected)
        => Assert.Equal(expected, DelimitedFile.Automatic(written));

    [Fact]
    public void A_quoted_cell_keeps_the_separator_inside_it()
    {
        var rows = DelimitedFile.Read("a,\"b,c\",d\n", ",", skipBlankLines: false, trim: false);

        var row = Assert.Single(rows);
        Assert.Equal(["a", "b,c", "d"], row);
    }

    [Fact]
    public void A_quote_inside_a_quoted_cell_is_written_twice_and_read_once()
    {
        var rows = DelimitedFile.Read("\"he said \"\"hi\"\"\",x\n", ",", false, false);

        Assert.Equal(["he said \"hi\"", "x"], Assert.Single(rows));
    }

    [Fact]
    public void A_line_break_inside_a_cell_belongs_to_the_cell()
    {
        var rows = DelimitedFile.Read("one,\"two\nlines\"\nnext,row\n", ",", false, false);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["one", "two\nlines"], rows[0]);
        Assert.Equal(["next", "row"], rows[1]);
    }

    [Fact]
    public void The_trim_switch_decides_whether_a_space_belongs_to_the_cell()
    {
        Assert.Equal(["a", "b"], Assert.Single(DelimitedFile.Read(" a , b \n", ",", false, trim: true)));
        Assert.Equal([" a ", " b "], Assert.Single(DelimitedFile.Read(" a , b \n", ",", false, trim: false)));
    }

    [Fact]
    public void The_blank_line_switch_decides_whether_an_empty_line_is_a_row()
    {
        var kept = DelimitedFile.Read("a,b\n\nc,d\n", ",", skipBlankLines: true, trim: false);
        Assert.Equal(2, kept.Count);
        Assert.Equal(["a", "b"], kept[0]);

        var blanks = DelimitedFile.Read("a,b\n\nc,d\n", ",", skipBlankLines: false, trim: false);
        Assert.Equal(3, blanks.Count);
    }

    [Fact]
    public void The_separator_is_worked_out_from_the_file_when_it_is_asked_to_be()
    {
        var rows = DelimitedFile.Read("a;b;c\n1;2;3\n", "auto", false, false);

        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void Rows_may_hold_different_numbers_of_cells()
    {
        var rows = DelimitedFile.Read("a,b\nc,d,e\n", ",", false, false);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        Assert.Equal(3, rows[1].Count);
    }

    [Fact]
    public void Text_that_does_not_look_like_a_table_is_still_the_file_the_macro_asked_for()
    {
        var rows = DelimitedFile.Read("a,\"b\n", ",", false, false);

        Assert.NotEmpty(rows);
    }

    [Fact]
    public void Rows_are_written_back_with_the_separator_and_the_line_ending_asked_for()
    {
        var text = DelimitedFile.Write(
            [["a", "b"], ["c", "d"]], ",", "\n", quoteAll: false, quoteEmpty: false);

        Assert.Equal("a,b\nc,d\n", text);
    }

    [Fact]
    public void A_cell_holding_the_separator_is_quoted_so_it_reads_back_as_one_cell()
    {
        var text = DelimitedFile.Write([["a,b"]], ",", "\n", quoteAll: false, quoteEmpty: false);

        Assert.Equal("\"a,b\"\n", text);
        Assert.Equal(["a,b"], DelimitedFile.Read(text, ",", false, false)[0]);
    }

    [Fact]
    public void Quoting_every_cell_is_a_switch_of_its_own()
    {
        var text = DelimitedFile.Write([["a", "b"]], ",", "\n", quoteAll: true, quoteEmpty: false);

        Assert.Equal("\"a\",\"b\"\n", text);
    }

    [Fact]
    public void An_empty_cell_can_be_kept_or_said_with_quotes()
    {
        Assert.Equal("a,\n", DelimitedFile.Write([["a", ""]], ",", "\n", quoteAll: false, quoteEmpty: false));
        Assert.Equal("a,\"\"\n", DelimitedFile.Write([["a", ""]], ",", "\n", quoteAll: false, quoteEmpty: true));
    }

    [Fact]
    public void The_line_ending_is_written_as_it_was_asked_for()
    {
        var text = DelimitedFile.Write([["a", "b"]], ",", "\r\n", quoteAll: false, quoteEmpty: false);

        Assert.Equal("a,b\r\n", text);
    }
}
