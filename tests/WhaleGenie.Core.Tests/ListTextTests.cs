using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

public class ListTextTests
{
    [Theory]
    [MemberData(nameof(ListCases))]
    public void A_shape_is_read_as_a_list(ExpressionNode source, string expected)
    {
        Assert.True(ListText.TryRead(source, null, out var items, out var error));
        Assert.Null(error);
        Assert.Equal(expected, ListText.Summarize(items));
    }

    public static TheoryData<ExpressionNode, string> ListCases() => new()
    {
        { Formula.List(Formula.Number(1), Formula.Number(2), Formula.Number(3)), "1, 2, 3" },
        { Formula.Text("1, 2, 3"), "1, 2, 3" },
        { Formula.Text("a，b"), "a, b" },
        { Formula.Text("one; two"), "one, two" },
        { Formula.Call("split", Formula.Text("a,b"), Formula.Text(",")), "a, b" },
        { Formula.Call("list"), "" },
        { Formula.Nothing, "" },
        { Formula.Text("   "), "" },
    };

    [Fact]
    public void A_value_that_is_not_a_list_is_reported_as_a_kind_problem()
    {
        Assert.False(ListText.TryRead(Formula.Call("upper", Formula.Text("x")), null,
            out _, out var error));
        Assert.Equal(ExpressionErrorCode.TypeMismatch, error!.Code);
    }

    [Fact]
    public void A_line_of_words_is_split_without_being_read_as_a_formula()
        => Assert.Equal(["one", "two"],
            ListText.Split("one; two").Select(item => item.AsText()));
}
