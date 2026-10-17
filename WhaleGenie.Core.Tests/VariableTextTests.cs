using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Text fields fill in names where they stand. The rule is narrow on purpose: a dollar sign in
/// front of something that is not a name belongs to the text, and two dollar signs are how one
/// literal dollar is written.
/// </summary>
public class VariableTextTests
{
    private static string Fill(string text, params (string Name, string Value)[] variables)
    {
        var values = variables.ToDictionary(pair => pair.Name, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        return VariableText.Replace(text, name => values.GetValueOrDefault(name));
    }

    [Theory]
    [InlineData("$100 off", "$100 off")]
    [InlineData("price is $100", "price is $100")]
    [InlineData("$", "$")]
    [InlineData("a $ b", "a $ b")]
    public void A_dollar_that_does_not_start_a_name_is_text(string written, string expected)
    {
        Assert.Equal(expected, Fill(written, ("name", "Ada")));
    }

    [Fact]
    public void A_known_name_is_filled_in_where_it_stands()
    {
        Assert.Equal("Hello Ada, Ada!", Fill("Hello $name, $NAME!", ("name", "Ada")));
    }

    [Fact]
    public void A_doubled_dollar_is_one_literal_dollar()
    {
        Assert.Equal("$name and Ada", Fill("$$name and $name", ("name", "Ada")));
        Assert.Equal(["name"], VariableText.Referenced("$name but not $$name"));
    }

    [Fact]
    public void An_unknown_name_stays_as_it_was_written()
    {
        Assert.Equal("Hello $missing", Fill("Hello $missing", ("name", "Ada")));
        Assert.Equal(["missing"], VariableText.Referenced("Hello $missing"));
    }
}
