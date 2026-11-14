using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The one scanner both readers use. These cases pin the lexical rules the editor draws with and
/// the parser runs on, including the lines that are only half written.
/// </summary>
public class ExpressionTokensTests
{
    [Fact]
    public void A_number_ends_where_the_language_stops_reading_one()
    {
        var tokens = ExpressionTokens.Scan("1e5");

        Assert.Equal(ExpressionTokenKind.Number, tokens[0].Kind);
        Assert.Equal("1", tokens[0].Text);
        Assert.Equal(ExpressionTokenKind.Name, tokens[1].Kind);
        Assert.Equal("e5", tokens[1].Text);
    }
    [Fact]
    public void A_text_keeps_its_quotes_in_its_span()
    {
        var token = ExpressionTokens.Scan("\"hi\"")[0];

        Assert.Equal(ExpressionTokenKind.Text, token.Kind);
        Assert.Equal("hi", token.Text);
        Assert.Equal(0, token.Start);
        Assert.Equal(4, token.Length);
    }

    [Fact]
    public void A_record_field_stays_part_of_the_name()
    {
        var token = ExpressionTokens.Scan("where.age")[0];

        Assert.Equal(ExpressionTokenKind.Name, token.Kind);
        Assert.Equal("where.age", token.Text);
    }

    [Fact]
    public void An_unknown_character_is_a_token_when_tolerant_and_an_error_when_strict()
    {
        var token = ExpressionTokens.Scan("?", strict: false)[0];

        Assert.Equal(ExpressionTokenKind.Unknown, token.Kind);
        Assert.Equal("?", token.Text);
        Assert.Throws<ExpressionException>(() => ExpressionTokens.Scan("?"));
    }

    [Fact]
    public void A_dollar_is_not_a_marker_in_front_of_a_name()
    {
        Assert.Equal(ExpressionTokenKind.Unknown,
            ExpressionTokens.Scan("$count", strict: false)[0].Kind);
        Assert.Throws<ExpressionException>(() => ExpressionTokens.Scan("$count"));
    }

    [Fact]
    public void An_unclosed_quote_is_text_when_tolerant_and_an_error_when_strict()
    {
        var token = ExpressionTokens.Scan("\"hi", strict: false)[0];

        Assert.Equal(ExpressionTokenKind.Text, token.Kind);
        Assert.Equal(0, token.Start);
        Assert.Equal(3, token.Length);
        Assert.Throws<ExpressionException>(() => ExpressionTokens.Scan("\"hi"));
    }

    [Fact]
    public void The_word_operators_are_words_and_the_symbols_are_symbols()
    {
        var words = ExpressionTokens.Scan("a and b");
        Assert.True(ExpressionTokens.IsOperatorWord(words[1].Text));

        var symbols = ExpressionTokens.Scan("a && b");
        Assert.Equal(ExpressionTokenKind.Symbol, symbols[1].Kind);
        Assert.Equal("&&", symbols[1].Text);
    }
}
