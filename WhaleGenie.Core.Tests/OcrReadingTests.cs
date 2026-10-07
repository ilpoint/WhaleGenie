using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Pulling the numbers out of what the OCR read. A screen full of labels and a screen full of
/// amounts should be handled by the same kind of step, so the pieces that hold a number are kept
/// and cut down to the number itself.
/// </summary>
public class OcrReadingTests
{
    [Theory]
    [InlineData("¥1,234.50", "1234.50")]
    [InlineData("订单号 12345", "12345")]
    [InlineData("Amount: 12.5 kg", "12.5")]
    [InlineData("-3.75", "-3.75")]
    [InlineData("- 12", "-12")]
    [InlineData("12.", "12")]
    [InlineData("1.234.56", "1.234.56")]
    [InlineData("１２３４５", "12345")]
    [InlineData("－１２．５", "-12.5")]
    [InlineData("12", "12")]
    [InlineData("12 34", "1234")]
    public void A_number_is_read_out_of_the_text_around_it(string text, string expected)
        => Assert.Equal(expected, OcrReading.Number(text));

    [Theory]
    [InlineData("Ready")]
    [InlineData("已完成")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("¥")]
    public void Text_without_a_digit_in_it_holds_no_number(string text)
        => Assert.Null(OcrReading.Number(text));

    [Fact]
    public void Asking_for_numbers_keeps_the_pieces_that_hold_one_and_forgets_the_rest()
    {
        var spans = new List<TextSpan>
        {
            new("合计 ¥1,234.50", new ScreenPoint(10, 10), new ScreenSize(80, 12), 0.9),
            new("已完成", new ScreenPoint(10, 30), new ScreenSize(30, 12), 0.8),
            new("数量 3", new ScreenPoint(10, 50), new ScreenSize(40, 12), 0.7),
        };

        var numbers = OcrReading.Numbers(spans);

        Assert.Equal(2, numbers.Count);
        Assert.Equal("1234.50", numbers[0].Text);
        Assert.Equal("3", numbers[1].Text);

        // Where the number was read from and how sure the reading was are both still there, so a
        // macro can click on it or check the confidence.
        Assert.Equal(spans[0].Location, numbers[0].Location);
        Assert.Equal(spans[0].Confidence, numbers[0].Confidence);
        Assert.Equal(spans[0].Size, numbers[0].Size);
    }

    [Fact]
    public void Reading_a_table_puts_the_pieces_on_the_lines_and_in_the_columns_they_sit_in()
    {
        // Rows and columns as an OCR engine hands them back for a table without printed lines: one
        // piece per cell, sitting where the cell is.
        var spans = new List<TextSpan>
        {
            new("名称", new ScreenPoint(10, 10), new ScreenSize(30, 14), 0.9),
            new("数量", new ScreenPoint(120, 11), new ScreenSize(30, 14), 0.9),
            new("金额", new ScreenPoint(220, 10), new ScreenSize(30, 14), 0.9),
            new("苹果", new ScreenPoint(10, 40), new ScreenSize(30, 14), 0.9),
            new("3", new ScreenPoint(120, 41), new ScreenSize(8, 14), 0.9),
            new("12.50", new ScreenPoint(220, 40), new ScreenSize(40, 14), 0.9),
        };

        var rows = OcrReading.Rows(spans);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["名称", "数量", "金额"], rows[0]);
        Assert.Equal(["苹果", "3", "12.50"], rows[1]);
    }

    [Fact]
    public void A_line_the_engine_handed_back_whole_is_cut_at_the_wide_gaps()
    {
        // A detector that keeps a whole line in one box still leaves the columns as wide gaps in
        // the text.
        var spans = new List<TextSpan>
        {
            new("苹果    3    12.50", new ScreenPoint(10, 10), new ScreenSize(200, 14), 0.9),
            new("香蕉   10   8.00", new ScreenPoint(10, 40), new ScreenSize(200, 14), 0.9),
        };

        var rows = OcrReading.Rows(spans);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["苹果", "3", "12.50"], rows[0]);
        Assert.Equal(["香蕉", "10", "8.00"], rows[1]);
    }

    [Fact]
    public void Words_of_one_column_stay_in_one_cell()
    {
        // Two boxes of one cell, near each other, are one cell with the words in it; a single
        // space is the gap between words, not a column.
        var spans = new List<TextSpan>
        {
            new("Total", new ScreenPoint(10, 10), new ScreenSize(40, 14), 0.9),
            new("amount", new ScreenPoint(52, 10), new ScreenSize(50, 14), 0.9),
            new("99", new ScreenPoint(150, 10), new ScreenSize(20, 14), 0.9),
        };

        var rows = OcrReading.Rows(spans);

        Assert.Single(rows);
        Assert.Equal(["Total amount", "99"], rows[0]);
    }

    [Fact]
    public void A_page_of_writing_with_no_columns_comes_back_one_cell_per_line()
    {
        var spans = new List<TextSpan>
        {
            new("第一行", new ScreenPoint(10, 10), new ScreenSize(60, 14), 0.9),
            new("第二行", new ScreenPoint(10, 40), new ScreenSize(60, 14), 0.9),
        };

        Assert.Equal([["第一行"], ["第二行"]], OcrReading.Rows(spans));
    }
}
