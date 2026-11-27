using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The word a switch writes for the way it compares, and the way it means. A word nobody knows is
/// the plain equality a switch had before the other ways existed, and the word is read the way the
/// rest of the file is: spaces around it do not matter and case does not matter.
/// </summary>
public class SwitchModesTests
{
    [Theory]
    [InlineData("equals", "Equals")]
    [InlineData("notequals", "NotEquals")]
    [InlineData("contains", "Contains")]
    [InlineData("notcontains", "NotContains")]
    [InlineData("startswith", "StartsWith")]
    [InlineData("endswith", "EndsWith")]
    [InlineData("greaterthan", "GreaterThan")]
    [InlineData("greaterorequal", "GreaterOrEqual")]
    [InlineData("lessthan", "LessThan")]
    [InlineData("lessorequal", "LessOrEqual")]
    [InlineData("regex", "Regex")]
    public void Each_word_is_the_comparison_it_names(string mode, string expected)
        => Assert.Equal(expected, SwitchModes.Of(mode).ToString());

    [Fact]
    public void A_word_is_read_without_caring_about_case_or_the_spaces_around_it()
    {
        Assert.Equal("StartsWith", SwitchModes.Of(" StartsWith ").ToString());
        Assert.Equal("Regex", SwitchModes.Of("REGEX").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nobody-writes-this")]
    [InlineData("equal")]
    public void A_word_nobody_knows_is_plain_equality(string mode)
        => Assert.Equal("Equals", SwitchModes.Of(mode).ToString());
}
