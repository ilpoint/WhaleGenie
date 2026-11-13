using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Text fields fill in names where they stand. The rule is narrow on purpose: a dollar sign in
/// front of something that is not a name belongs to the text, and two dollar signs are how one
/// literal dollar is written. Reading the line turns each name into a piece of its own, so what
/// the run works out is a shape rather than a line it has to cut up again.
/// </summary>
public class FormulaTextTests
{
    private static string Fill(string text, params (string Name, string Value)[] variables)
    {
        var bag = new VariableBag();
        foreach (var (name, value) in variables)
        {
            bag.Set(name, Value.FromText(value));
        }

        return Expression.Evaluate(FormulaText.Read(text, ActionValueForm.Text), bag).AsText();
    }

    private static IReadOnlyList<string> Names(string text)
        => FormulaText.Names(text, ActionValueForm.Text);

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
        Assert.True(VariableName.IsName(name));
        Assert.Equal("Ada", Fill($"${name}", (name, "Ada")));
        Assert.Equal([name], Names($"${name}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".2")]
    [InlineData("2 name")]
    [InlineData("2-name")]
    public void A_name_still_needs_valid_characters(string name)
        => Assert.False(VariableName.IsName(name));

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
        Assert.Equal(["name"], Names("$name but not $$name"));
    }

    [Fact]
    public void A_name_nothing_defines_reads_as_the_name()
    {
        Assert.Equal("Hello missing", Fill("Hello $missing", ("name", "Ada")));
        Assert.Equal(["missing"], Names("Hello $missing"));
    }

    [Fact]
    public void A_name_in_braces_ends_where_the_braces_do()
    {
        Assert.Equal(@"C:\out\Ada.txt", Fill(@"C:\out\${name}.txt", ("name", "Ada")));
        Assert.Equal(@"C:\out\Ada.txt", Fill(@"C:\out\${file.txt}.txt", ("file.txt", "Ada")));
    }

    [Fact]
    public void A_name_in_braces_is_found_and_a_missing_one_reads_as_the_name()
    {
        Assert.Equal(["name"], Names("a ${name} b"));
        Assert.Equal("Hello missing", Fill("Hello ${missing}", ("name", "Ada")));
    }

    [Fact]
    public void Unclosed_braces_are_text()
    {
        Assert.Equal("Hello ${missing", Fill("Hello ${missing", ("name", "Ada")));
        Assert.Empty(Names("Hello ${missing"));
    }

    [Fact]
    public void A_braced_formula_is_worked_out_into_text()
    {
        Assert.Equal("1 + 2 = 3", Fill("1 + 2 = ${1 + 2}"));
        Assert.Equal(["x"], Names("${$x + 1}"));
    }

    [Fact]
    public void A_right_brace_inside_a_quoted_formula_does_not_end_it()
    {
        // A brace inside a quoted piece belongs to that piece: the place a formula stands in ends
        // at the brace that closes it, not at the first one that shows up.
        Assert.Equal("}}", Fill("${upper(\"}\")}}"));
    }

    [Fact]
    public void A_brace_that_is_only_a_name_is_still_a_name()
    {
        Assert.Equal("Ada", Fill("${name}", ("name", "Ada")));
        Assert.Equal("Ada", Fill("${123}", ("123", "Ada")));
    }

    private static string Written(string line, ActionValueForm mode)
        => FormulaText.Write(FormulaText.Read(line, mode));

    [Theory]
    [InlineData("保存")]
    [InlineData("Ctrl+Shift+A")]
    [InlineData("C:\\images\\ok.png")]
    [InlineData("#FF0000")]
    [InlineData("")]
    [InlineData("a $ b")]
    [InlineData("$% off")]
    public void A_line_of_text_is_written_back_as_itself(string line)
        => Assert.Equal(line, Written(line, ActionValueForm.Text));

    [Fact]
    public void A_dollar_in_text_is_written_back_doubled_so_it_reads_as_one()
    {
        Assert.Equal("$$123 and $123", Written("$$123 and $123", ActionValueForm.Text));
        Assert.Equal("$123 and $$name", Written("$123 and $$name", ActionValueForm.Text));
    }

    [Fact]
    public void A_name_beside_text_keeps_the_braces_that_end_it()
    {
        Assert.Equal("${name}.txt", Written("${name}.txt", ActionValueForm.Text));
        Assert.Equal("$name!", Written("$name!", ActionValueForm.Text));
    }

    [Fact]
    public void A_formula_is_written_back_so_it_reads_the_same_way()
    {
        Assert.Equal("$a + 1", Written("$a + 1", ActionValueForm.Value));
        Assert.Equal("field($record, \"x\") + $b",
            Written("field($record, \"x\") + $b", ActionValueForm.Value));
        Assert.Equal("($a + $b) * 2", Written("($a + $b) * 2", ActionValueForm.Value));
    }

    [Theory]
    [InlineData("$shot")]
    [InlineData("$a + $b")]
    [InlineData("你好，$name！")]
    [InlineData("1 + 2 = ${1 + 2}")]
    public void Writing_a_line_and_reading_it_again_gives_the_same_shape(string line)
    {
        var once = FormulaText.Read(line, ActionValueForm.Value);
        var twice = FormulaText.Read(FormulaText.Write(once), ActionValueForm.Value);

        Assert.Equal(FormulaText.Write(once), FormulaText.Write(twice));
    }
}
