using System;

using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The text a shape is drawn as. The writer owns where the brackets land, so the editor can show a
/// formula the person built without the person having to think about brackets: these checks state
/// what a shape reads as, which is the display contract the page and the native windows share.
/// </summary>
public class ExpressionWriterTests
{
    private static string Written(ExpressionNode tree) => Expression.Render(tree);

    [Fact]
    public void Text_is_written_with_its_quotes()
        => Assert.Equal("\"plain\"", Written(Formula.Text("plain")));

    [Fact]
    public void The_characters_text_cannot_hold_are_written_as_what_they_read_back_as()
        => Assert.Equal("\"a\\\\b\\\"c\\nd\\re\\tf\"",
            Written(Formula.Text("a\\b\"c\nd\re\tf")));

    [Fact]
    public void A_name_a_number_a_date_and_a_constant_are_written_as_themselves()
    {
        Assert.Equal("where.age", Written(Formula.Name("where.age")));
        Assert.Equal("1.50", Written(Formula.Number("1.50", 1.5)));
        Assert.Equal("2026-03-04", Written(Formula.Date("2026-03-04")));
        Assert.Equal("true", Written(Formula.Constant("true")));
    }

    [Fact]
    public void A_hole_is_written_as_the_placeholder_it_was_given()
    {
        Assert.Equal("▢", Expression.Render(Formula.Hole));
        Assert.Equal("…", Expression.Render(Formula.Hole, "…"));
    }

    [Fact]
    public void A_list_and_a_call_keep_their_pieces_in_order()
    {
        Assert.Equal("[1, \"two\", x]",
            Written(Formula.List(Formula.Number(1), Formula.Text("two"), Formula.Name("x"))));
        Assert.Equal("max(1, 2)", Written(Formula.Call("max", Formula.Number(1), Formula.Number(2))));
    }

    [Fact]
    public void A_run_of_pieces_is_written_with_pluses()
        => Assert.Equal("\"a\" + x + \"!\"",
            Written(Formula.Run([Formula.Text("a"), Formula.Name("x"), Formula.Text("!")])));

    [Fact]
    public void A_prefix_reads_against_the_operand_it_binds()
    {
        Assert.Equal("not ready", Written(Formula.Unary("not", Formula.Name("ready"))));
        Assert.Equal("-count", Written(Formula.Unary("-", Formula.Name("count"))));
        Assert.Equal("-(a + b)",
            Written(Formula.Unary("-", Formula.Binary("+", Formula.Name("a"), Formula.Name("b")))));
    }

    [Fact]
    public void Multiplication_binds_tighter_than_addition()
    {
        Assert.Equal("a + b * c",
            Written(Formula.Binary("+", Formula.Name("a"),
                Formula.Binary("*", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("(a + b) * c",
            Written(Formula.Binary("*", Formula.Binary("+", Formula.Name("a"), Formula.Name("b")),
                Formula.Name("c"))));
    }

    [Fact]
    public void A_right_operand_that_reads_differently_keeps_its_brackets()
    {
        Assert.Equal("a - (b - c)",
            Written(Formula.Binary("-", Formula.Name("a"),
                Formula.Binary("-", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a / (b / c)",
            Written(Formula.Binary("/", Formula.Name("a"),
                Formula.Binary("/", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a + b + c",
            Written(Formula.Binary("+", Formula.Name("a"),
                Formula.Binary("+", Formula.Name("b"), Formula.Name("c")))));
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        Assert.Equal("a or b and c",
            Written(Formula.Binary("or", Formula.Name("a"),
                Formula.Binary("and", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("(a or b) and c",
            Written(Formula.Binary("and", Formula.Binary("or", Formula.Name("a"), Formula.Name("b")),
                Formula.Name("c"))));
    }

    [Fact]
    public void A_comparison_is_one_step_inside_and_or_and_keeps_its_right_brackets()
    {
        Assert.Equal("a < b and c > d",
            Written(Formula.Binary("and",
                Formula.Binary("<", Formula.Name("a"), Formula.Name("b")),
                Formula.Binary(">", Formula.Name("c"), Formula.Name("d")))));
        Assert.Equal("a < (b < c)",
            Written(Formula.Binary("<", Formula.Name("a"),
                Formula.Binary("<", Formula.Name("b"), Formula.Name("c")))));
    }

    [Fact]
    public void A_group_keeps_the_brackets_the_person_put_there()
        => Assert.Equal("(a + b)",
            Written(new GroupNode(Formula.Binary("+", Formula.Name("a"), Formula.Name("b")))));

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public void Every_comparison_holds_its_operands_the_same_way(string operation)
    {
        Assert.Equal("a " + operation + " b",
            Written(Formula.Binary(operation, Formula.Name("a"), Formula.Name("b"))));

        // A comparison on the right of another one reads differently nested, so it keeps its
        // brackets; on the left the direction does not change, so it goes without.
        Assert.Equal("a " + operation + " (b " + operation + " c)",
            Written(Formula.Binary(operation, Formula.Name("a"),
                Formula.Binary(operation, Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a " + operation + " b " + operation + " c",
            Written(Formula.Binary(operation, Formula.Binary(operation, Formula.Name("a"),
                Formula.Name("b")), Formula.Name("c"))));
    }

    [Fact]
    public void The_remainder_binds_with_the_other_multiplications()
    {
        Assert.Equal("a % (b % c)",
            Written(Formula.Binary("%", Formula.Name("a"),
                Formula.Binary("%", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a % (b + c)",
            Written(Formula.Binary("%", Formula.Name("a"),
                Formula.Binary("+", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a * (b * c)",
            Written(Formula.Binary("*", Formula.Name("a"),
                Formula.Binary("*", Formula.Name("b"), Formula.Name("c")))));
    }

    [Fact]
    public void And_and_or_need_no_brackets_on_their_right()
    {
        Assert.Equal("a and b and c",
            Written(Formula.Binary("and", Formula.Name("a"),
                Formula.Binary("and", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a or b or c",
            Written(Formula.Binary("or", Formula.Name("a"),
                Formula.Binary("or", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a or b and c",
            Written(Formula.Binary("or", Formula.Name("a"),
                Formula.Binary("and", Formula.Name("b"), Formula.Name("c")))));
        Assert.Equal("a and b or c",
            Written(Formula.Binary("or", Formula.Binary("and", Formula.Name("a"), Formula.Name("b")),
                Formula.Name("c"))));

        // The other way round the brackets are needed: or under and binds looser, so without them
        // the line would read as or first.
        Assert.Equal("(a or b) and c",
            Written(Formula.Binary("and", Formula.Binary("or", Formula.Name("a"), Formula.Name("b")),
                Formula.Name("c"))));
        Assert.Equal("a and (b or c)",
            Written(Formula.Binary("and", Formula.Name("a"),
                Formula.Binary("or", Formula.Name("b"), Formula.Name("c")))));
    }

    [Fact]
    public void A_comparison_beside_logic_needs_no_brackets_and_one_beside_addition_does()
    {
        // Comparisons bind tighter than and/or, so the line reads the same without brackets; they
        // bind looser than addition, so on the right of a plus they keep them.
        Assert.Equal("a == b and c != d",
            Written(Formula.Binary("and",
                Formula.Binary("==", Formula.Name("a"), Formula.Name("b")),
                Formula.Binary("!=", Formula.Name("c"), Formula.Name("d")))));
        Assert.Equal("a + (b == c)",
            Written(Formula.Binary("+", Formula.Name("a"),
                Formula.Binary("==", Formula.Name("b"), Formula.Name("c")))));
    }

    [Fact]
    public void A_prefix_over_logic_or_a_comparison_keeps_its_brackets()
    {
        Assert.Equal("not (a == b)",
            Written(Formula.Unary("not", Formula.Binary("==", Formula.Name("a"), Formula.Name("b")))));
        Assert.Equal("not (a and b)",
            Written(Formula.Unary("not", Formula.Binary("and", Formula.Name("a"), Formula.Name("b")))));
        Assert.Equal("--a", Written(Formula.Unary("-", Formula.Unary("-", Formula.Name("a")))));
    }

    [Fact]
    public void A_piece_of_a_list_or_a_call_is_written_as_it_is()
    {
        Assert.Equal("[a == b]",
            Written(Formula.List(Formula.Binary("==", Formula.Name("a"), Formula.Name("b")))));
        Assert.Equal("f(a == b)",
            Written(Formula.Call("f", Formula.Binary("==", Formula.Name("a"), Formula.Name("b")))));
    }
}
