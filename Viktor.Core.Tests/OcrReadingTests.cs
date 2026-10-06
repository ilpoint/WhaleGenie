using Viktor.Core.Devices;
using Viktor.Core.Execution;

namespace Viktor.Core.Tests;

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
}
