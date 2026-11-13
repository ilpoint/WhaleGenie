using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A field builds its value out of shapes, not out of a line that has to be read back. These are
/// the cases that keep the constructors, the file and the evaluator able to do that without a text
/// language in between: each shape can be made, written down, read back and worked out.
/// </summary>
public class FormulaShapeTests
{
    [Fact]
    public void The_constructors_make_the_shapes_they_name()
    {
        Assert.IsType<TextNode>(Formula.Text("hello"));
        Assert.IsType<NumberNode>(Formula.Number(3));
        Assert.IsType<DateNode>(Formula.Date("2026-11-12"));
        Assert.IsType<ConstantNode>(Formula.Constant("true"));
        Assert.IsType<VariableNode>(Formula.Name("count", "a7k2m9"));
        Assert.IsType<CallNode>(Formula.Call("upper", Formula.Name("who")));
        Assert.IsType<UnaryNode>(Formula.Unary("not", Formula.Constant("false")));
        Assert.IsType<BinaryNode>(Formula.Binary("+", Formula.Number(1), Formula.Number(2)));
        Assert.IsType<ListNode>(Formula.List(Formula.Number(1), Formula.Number(2)));
        Assert.Same(HoleNode.Instance, Formula.Hole);
    }

    [Fact]
    public void A_run_of_one_piece_is_that_piece_and_an_empty_run_is_nothing()
    {
        var only = Formula.Text("hello");

        Assert.Equal(only, Formula.Run([only]));
        Assert.Equal(Formula.Nothing, Formula.Run([]));
    }

    [Fact]
    public void Every_shape_survives_the_file_round_trip()
    {
        ExpressionNode[] trees =
        [
            Formula.Text("plain words"),
            Formula.Number(3),
            Formula.Date("2026-11-12"),
            Formula.Constant("true"),
            Formula.Name("count", "a7k2m9"),
            Formula.Run([Formula.Text("你好，"), Formula.Name("who")]),
            Formula.List(Formula.Number(1), Formula.Text("two")),
            Formula.Call("upper", Formula.Name("who")),
            Formula.Unary("not", Formula.Constant("false")),
            Formula.Binary("+", Formula.Name("count"), Formula.Number(1)),
        ];

        foreach (var tree in trees)
        {
            var written = FormulaJson.Write(tree);
            var readBack = FormulaJson.Read(written);

            Assert.Equal(written?.ToJsonString(), FormulaJson.Write(readBack)?.ToJsonString());
        }
    }

    [Fact]
    public void A_name_keeps_its_identity_through_the_file()
    {
        var readBack = FormulaJson.Read(FormulaJson.Write(Formula.Name("count", "a7k2m9")));

        Assert.Equal(new VariableNode("count", "a7k2m9"), readBack);
    }

    [Fact]
    public void A_tree_built_without_text_evaluates()
    {
        Assert.Equal(3d, Expression.Evaluate(
            Formula.Binary("+", Formula.Number(1), Formula.Number(2))).AsNumber());
        Assert.Equal("ADA", Expression.Evaluate(
            Formula.Call("upper", Formula.Text("ada"))).AsText());
        Assert.Equal("hello world", Expression.Evaluate(
            Formula.Run([Formula.Text("hello "), Formula.Text("world")])).AsText());
    }

    [Fact]
    public void A_date_is_its_own_kind_rather_than_three_subtractions()
    {
        Assert.True(Expression.Evaluate(Formula.Date("2026-11-12")).IsDate);
        Assert.Contains("\"date\"", FormulaJson.Write(Formula.Date("2026-11-12"))!.ToJsonString());
    }

    [Fact]
    public void The_field_forms_name_four_different_questions()
    {
        Assert.Equal(4, System.Enum.GetValues<ActionValueForm>().Length);
        Assert.Equal(
            [ActionValueForm.Text, ActionValueForm.Run, ActionValueForm.Value, ActionValueForm.Name],
            System.Enum.GetValues<ActionValueForm>());
    }
}
