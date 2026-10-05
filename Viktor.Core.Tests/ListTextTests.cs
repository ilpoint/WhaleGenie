using Viktor.Core.Expressions;
using Viktor.Core.Variables;

namespace Viktor.Core.Tests;

public class ListTextTests
{
    [Theory]
    [InlineData("[1, 2, 3]", "1, 2, 3")]
    [InlineData("1, 2, 3", "1, 2, 3")]
    [InlineData("a，b", "a, b")]
    [InlineData("one; two", "one, two")]
    [InlineData("split(\"a,b\", \",\")", "a, b")]
    [InlineData("list()", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Text_is_read_as_a_list(string source, string expected)
    {
        Assert.True(ListText.TryParse(source, out var items, out var error));
        Assert.Null(error);
        Assert.Equal(expected, ListText.Summarize(items));
    }

    [Theory]
    [InlineData("splt(\"a\", \",\")")]
    [InlineData("[1, 2")]
    [InlineData("$missing")]
    [InlineData("upper(\"x\")")]
    public void Text_written_as_an_expression_has_to_produce_a_list(string source)
    {
        Assert.False(ListText.TryParse(source, out var items, out var error));
        Assert.NotNull(error);
        Assert.Empty(items);
    }

    [Fact]
    public void A_value_that_is_not_a_list_is_reported_as_a_kind_problem()
    {
        Assert.False(ListText.TryParse("upper(\"x\")", out _, out var error));
        Assert.Equal(ExpressionErrorCode.TypeMismatch, error!.Code);
    }
}
