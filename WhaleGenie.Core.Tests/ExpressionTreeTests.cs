using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The expression editor shows a formula as a tree of boxes, so the language has to be able to
/// hand that tree over and take it back. These read the text in, write it out again and check that
/// what came back is the same formula — the shape of it and the value it works out.
/// </summary>
public class ExpressionTreeTests
{
    /// <summary>
    /// Formulas written the way the language reads them. Nothing here leans on two neighbours of
    /// the same tightness regrouping, so reading and writing one is expected to hand the very same
    /// tree back.
    /// </summary>
    private static readonly string[] Formulas =
    [
        "1 + 2 * 3",
        "(1 + 2) * 3",
        "1 - 2 - 3",
        "1 - (2 - 3)",
        "-5 + 2",
        "-$count",
        "$count % 2 == 0",
        "not $a",
        "not ($a and $b)",
        "$a and $b or $c",
        "$a or $b and $c",
        "1 == (2 == 2)",
        "1 < 2 and 3 >= 4",
        "if($count > 3, \"many\", \"few\")",
        "upper($name) + \"-\" + $count",
        "field($hit, \"x\")",
        "get(list(1, 2), 0)",
        "list(1, \"a\", true)",
        "[1, 2, 3]",
        "[]",
        "now()",
        "concat(\"a\", 1, true)",
        "0.50",
        "true",
        "null",
        "\"a \\\"quote\\\" and a \\\\ backslash\"",
        "\"line\\nbreak\"",
    ];

    [Fact]
    public void Reading_a_formula_gives_the_tree_it_says()
    {
        var tree = Expression.Parse("$count + 1");

        var sum = Assert.IsType<BinaryNode>(tree);
        Assert.Equal("+", sum.Operator);
        Assert.Equal(new VariableNode("count"), sum.Left);
        Assert.Equal(new NumberNode("1", 1), sum.Right);
    }

    [Fact]
    public void Every_formula_comes_back_out_of_the_tree_the_way_it_went_in()
    {
        foreach (var formula in Formulas)
        {
            var tree = Expression.Parse(formula);
            var written = Expression.Render(tree);
            var again = Expression.Parse(written);

            Same(tree, again);
            Assert.Equal(formula, written);
        }
    }

    [Fact]
    public void Writing_a_formula_out_twice_changes_nothing_the_second_time()
    {
        // Brackets that say the same thing as the tree are dropped, so this is the second reading
        // that has to settle: what the writer makes of a tree it has already written.
        string[] regrouping =
        [
            "$a + ($b + $c)",
            "($a + $b) + $c",
            "1 * (2 * 3)",
            "1 == (2 == 2)",
            "not (not $a)",
            "$a and ($b and $c)",
        ];

        foreach (var formula in Formulas.Concat(regrouping))
        {
            var once = Expression.Render(Expression.Parse(formula));
            var twice = Expression.Render(Expression.Parse(once));

            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void A_formula_worked_out_from_the_tree_comes_to_the_same_value()
    {
        string[] sums =
        [
            "$count + 1",
            "(1 + 2) * $count",
            "1 - (2 - $count)",
            "$name + \"-\" + $count",
            "if($count > 2, upper($name), \"few\")",
            "[$count, 1, 2]",
            "append(list(1), $count)",
            "not ($count < 1)",
            "$count % 2 == 0",
            "substring($name, 1)",
        ];

        var bag = new VariableBag();
        bag.SetText("name", "ada");
        bag.SetNumber("count", 3);

        foreach (var text in sums)
        {
            var fromText = Expression.Evaluate(text, bag).AsText();
            var fromTree = Expression.Evaluate(Expression.Render(Expression.Parse(text)), bag)
                .AsText();

            Assert.Equal(fromText, fromTree);
        }
    }

    [Theory]
    [InlineData("(1 + 2) * 3", "(1 + 2) * 3")]
    [InlineData("1 + 2 * 3", "1 + 2 * 3")]
    [InlineData("1 - (2 - 3)", "1 - (2 - 3)")]
    [InlineData("1 + 2 + 3", "1 + 2 + 3")]
    [InlineData("(1 + 2) + 3", "1 + 2 + 3")]
    [InlineData("1 * (2 * 3)", "1 * (2 * 3)")]
    [InlineData("-(1 + 2)", "-(1 + 2)")]
    [InlineData("not (true and false)", "not (true and false)")]
    [InlineData("1 == (2 == 2)", "1 == (2 == 2)")]
    [InlineData("(1 < 2) < 3", "1 < 2 < 3")]
    [InlineData("10 / (2 / 5)", "10 / (2 / 5)")]
    public void Brackets_are_written_only_where_the_tree_needs_them(string source, string expected)
        => Assert.Equal(expected, Expression.Render(Expression.Parse(source)));

    [Theory]
    [InlineData("!$a", "not $a")]
    [InlineData("$a && $b", "$a and $b")]
    [InlineData("$a || $b", "$a or $b")]
    [InlineData("TRUE", "true")]
    [InlineData("0.50", "0.50")]
    [InlineData("upper($name)", "upper($name)")]
    [InlineData("[1,2,3]", "[1, 2, 3]")]
    public void One_way_of_writing_something_is_settled_on(string source, string expected)
        => Assert.Equal(expected, Expression.Render(Expression.Parse(source)));

    [Fact]
    public void Text_keeps_its_quotes_and_its_backslashes()
    {
        var tree = new TextNode("a \"b\" \\ c\nd");
        var written = Expression.Render(tree);

        Assert.Equal("\"a \\\"b\\\" \\\\ c\\nd\"", written);
        Assert.Equal(tree, Expression.Parse(written));
    }

    [Fact]
    public void A_place_nothing_was_put_in_is_written_as_a_placeholder()
    {
        var half = new BinaryNode("+", new NumberNode("1", 1), HoleNode.Instance);

        Assert.Equal("1 + ▢", Expression.Render(half));
        Assert.Equal("1 + ?", Expression.Render(half, "?"));
    }

    [Fact]
    public void Reading_a_formula_never_leaves_a_place_unfilled()
    {
        foreach (var formula in Formulas)
        {
            Assert.False(HasHole(Expression.Parse(formula)), $"a hole was read out of {formula}");
        }
    }

    [Fact]
    public void An_empty_formula_is_not_a_tree()
    {
        Assert.False(Expression.TryParse("   ", out var tree, out var error));
        Assert.Null(tree);
        Assert.Equal(ExpressionErrorCode.Syntax, error!.Code);
    }

    [Fact]
    public void A_formula_that_does_not_read_says_why()
    {
        Assert.Equal(ExpressionErrorCode.Syntax, Failure("1 +").Code);
        Assert.Equal(ExpressionErrorCode.UnknownFunction, Failure("nope(1)").Code);
        Assert.Equal(ExpressionErrorCode.ArgumentCount, Failure("upper()").Code);
    }

    private static ExpressionException Failure(string source)
    {
        Assert.False(Expression.TryParse(source, out _, out var error));
        return Assert.IsType<ExpressionException>(error);
    }

    private static bool HasHole(ExpressionNode node) => node switch
    {
        HoleNode => true,
        UnaryNode unary => HasHole(unary.Operand),
        BinaryNode binary => HasHole(binary.Left) || HasHole(binary.Right),
        CallNode call => call.Arguments.Any(HasHole),
        ListNode list => list.Items.Any(HasHole),
        _ => false,
    };

    /// <summary>
    /// Whether two trees are the same node for node. Records compare by value as far as their own
    /// fields go, but a list of children is compared by which list it is, so the walk has to be
    /// done here rather than by handing the two roots to the framework.
    /// </summary>
    private static void Same(ExpressionNode? left, ExpressionNode? right)
    {
        switch (left, right)
        {
            case (null, null):
                return;
            case (UnaryNode a, UnaryNode b):
                Assert.Equal(a.Operator, b.Operator);
                Same(a.Operand, b.Operand);
                return;
            case (BinaryNode a, BinaryNode b):
                Assert.Equal(a.Operator, b.Operator);
                Same(a.Left, b.Left);
                Same(a.Right, b.Right);
                return;
            case (CallNode a, CallNode b):
                Assert.Equal(a.Name, b.Name);
                Same(a.Arguments, b.Arguments);
                return;
            case (ListNode a, ListNode b):
                Same(a.Items, b.Items);
                return;
            case (not null, not null):
                Assert.Equal(left, right);
                return;
            default:
                Assert.Fail($"{left} and {right} are not the same shape");
                return;
        }
    }

    private static void Same(IReadOnlyList<ExpressionNode> left, IReadOnlyList<ExpressionNode> right)
    {
        Assert.Equal(left.Count, right.Count);
        for (var index = 0; index < left.Count; index++)
        {
            Same(left[index], right[index]);
        }
    }
}
