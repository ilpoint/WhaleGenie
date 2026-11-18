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
    [InlineData("$", "$")]
    [InlineData("a $ b", "a $ b")]
    [InlineData("$% off", "$% off")]
    public void A_dollar_that_does_not_start_a_name_is_text(string written, string expected)
    {
        Assert.Equal(expected, Fill(written, ("name", "Ada")));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("2金币")]
    [InlineData("_2")]
    public void A_name_may_begin_with_a_digit_or_an_underscore(string name)
    {
        Assert.True(VariableText.IsName(name));
        Assert.Equal("Ada", Fill($"${name}", (name, "Ada")));
        Assert.Equal([name], VariableText.Referenced($"${name}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".2")]
    [InlineData("2 name")]
    [InlineData("2-name")]
    public void A_name_still_needs_valid_characters(string name)
        => Assert.False(VariableText.IsName(name));

    [Fact]
    public void A_known_name_is_filled_in_where_it_stands()
    {
        Assert.Equal("Hello Ada, Ada!", Fill("Hello $name, $NAME!", ("name", "Ada")));
    }

    [Fact]
    public void A_doubled_dollar_is_one_literal_dollar()
    {
        Assert.Equal("$name and Ada", Fill("$$name and $name", ("name", "Ada")));
        Assert.Equal("$123 and Ada", Fill("$$123 and $123", ("123", "Ada")));
        Assert.Equal(["name"], VariableText.Referenced("$name but not $$name"));
    }

    [Fact]
    public void An_unknown_name_stays_as_it_was_written()
    {
        Assert.Equal("Hello $missing", Fill("Hello $missing", ("name", "Ada")));
        Assert.Equal(["missing"], VariableText.Referenced("Hello $missing"));
    }

    [Fact]
    public void A_name_in_braces_ends_where_the_braces_do()
    {
        Assert.Equal(@"C:\out\Ada.txt", Fill(@"C:\out\${name}.txt", ("name", "Ada")));
        Assert.Equal(@"C:\out\Ada.txt", Fill(@"C:\out\${file.txt}.txt", ("file.txt", "Ada")));
    }

    [Fact]
    public void A_name_in_braces_is_found_and_a_missing_one_stays_as_written()
    {
        Assert.Equal(["name"], VariableText.Referenced("a ${name} b"));
        Assert.Equal("Hello ${missing}", Fill("Hello ${missing}", ("name", "Ada")));
    }

    [Fact]
    public void Unclosed_braces_are_text()
    {
        Assert.Equal("Hello ${missing", Fill("Hello ${missing", ("name", "Ada")));
        Assert.Empty(VariableText.Referenced("Hello ${missing"));
    }

    [Fact]
    public void A_braced_expression_is_worked_out_into_text()
    {
        var filled = VariableText.Replace("1 + 2 = ${1 + 2}", _ => null,
            expression => Expression.Evaluate(expression).AsText());

        Assert.Equal("1 + 2 = 3", filled);
        Assert.Equal(["1 + 2"], VariableText.Expressions("${1 + 2}"));
        Assert.Equal(["x"], VariableText.Referenced("${$x + 1}"));
    }

    [Fact]
    public void A_brace_that_is_only_a_name_is_still_a_name()
    {
        Assert.Empty(VariableText.Expressions("${name}"));
        Assert.Equal("Ada", VariableText.Replace("${name}", _ => "Ada",
            _ => throw new InvalidOperationException("a name is not an expression")));
        Assert.Empty(VariableText.Expressions("${123}"));
        Assert.Equal("Ada", VariableText.Replace("${123}", _ => "Ada",
            _ => throw new InvalidOperationException("a numeric name is not an expression")));
    }

    [Fact]
    public void A_right_brace_inside_a_quoted_expression_does_not_end_it()
    {
        const string written = "${pick(\"}\")}";

        Assert.Equal("pick(\"}\")", VariableText.Replace(written, _ => null, expression => expression));
        Assert.Equal(["pick(\"}\")"], VariableText.Expressions(written));
    }
}
