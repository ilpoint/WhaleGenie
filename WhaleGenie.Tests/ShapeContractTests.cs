using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The value a field keeps is a shape. There is no line with a sign in front of a name, no
/// character that has to be doubled and no brace that ends a reference: a value is made in the
/// value window, and a line is only a picture of it or a line a person typed into a formula box.
/// These cases hold that line to its word.
/// </summary>
public class ShapeContractTests
{
    [Fact]
    public void A_formula_box_reads_a_bare_name_as_the_name_it_is()
    {
        var tree = Formula.Binary("+", Formula.Name("count"), Formula.Number("1", 1d));

        var sum = Assert.IsType<BinaryNode>(tree);
        Assert.Equal("count", Assert.IsType<VariableNode>(sum.Left).Name);
        Assert.Equal(1d, Assert.IsType<NumberNode>(sum.Right).Value);
    }

    [Fact]
    public void An_empty_place_is_not_a_value()
    {
        Assert.NotSame(Formula.Hole, Formula.Nothing);
        Assert.True(Formula.IsNothing(Formula.Nothing));
        Assert.False(Formula.IsNothing(Formula.Hole));
    }

    [Fact]
    public void Rendering_a_formula_puts_no_sign_in_front_of_a_name()
    {
        var tree = Formula.Call("upper", Formula.Name("title"));

        var written = Expression.Render(tree);

        Assert.Equal("upper(title)", written);
        Assert.DoesNotContain("$", written);

        // The line is only a picture. The identity a name is bound to stays in the shape, and no
        // line is ever read back into one.
    }

    [Fact]
    public void A_name_is_said_two_ways_and_neither_has_a_sign()
    {
        var name = Formula.Name("count");

        Assert.Equal("count", Formula.Word(name));
        Assert.Equal("「count」", ExpressionPhrase.Display(name));
        Assert.Equal("count", Formula.Speak(name));
        Assert.Equal("共 count", Formula.Word(Formula.Run([Formula.Text("共 "), name])));
    }

    [Fact]
    public void The_file_writes_a_name_as_a_shape_with_only_its_name()
    {
        var written = FormulaJson.Write(Formula.Name("count"));

        var shape = Assert.IsType<JsonObject>(written);
        Assert.Equal("count", shape["name"]!.GetValue<string>());
        Assert.Single(shape);
        Assert.Equal(Formula.Name("count"), FormulaJson.Read(written));
    }

    [Fact]
    public void A_line_a_person_typed_into_a_row_is_the_words_it_says()
    {
        var row = new StepParameterRow();

        row.SetLine("path", "C:\\logs\\$100 off");

        Assert.Equal(Formula.Text("C:\\logs\\$100 off"), row.Cell("path"));
        Assert.Equal("C:\\logs\\$100 off", row.Text("path"));
    }

    [Fact]
    public void References_are_read_from_the_shape_not_scanned_out_of_a_line()
    {
        var tree = Formula.Binary("+", Formula.Name("count"), Formula.Call("upper", Formula.Name("title")));

        Assert.Equal(["count", "title"], Expression.References(tree));
        Assert.Equal("count + upper(title)", Expression.Render(tree));
    }

    [Fact]
    public void A_picture_row_keeps_the_shape_the_value_window_handed_it()
    {
        var row = new ImageRowViewModel();

        row.Write(Formula.Name("shot"));

        Assert.Equal(Formula.Name("shot"), row.Shape);
        Assert.Equal(Formula.Name("shot"), row.ToRow().Cell("image"));
        Assert.DoesNotContain("$", row.Text);
    }
}
