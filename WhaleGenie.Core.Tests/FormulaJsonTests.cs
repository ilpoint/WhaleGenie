using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A value is a shape rather than a line, and the file keeps that shape. These cases are about the
/// two directions agreeing: what is written can be read back as the same shape, because a macro
/// file that quietly loses a name inside a sentence is the failure this whole representation
/// exists to prevent.
/// </summary>
public class FormulaJsonTests
{
    public static TheoryData<string> Shapes =>
    [
        "a piece of text",
        "你好，世界",
        "1 + 2 = 3",
    ];

    [Fact]
    public void Plain_text_is_written_as_the_text_it_is()
    {
        Assert.Equal("你好", FormulaJson.Write(Formula.Text("你好"))!.GetValue<string>());
        Assert.Equal(3d, FormulaJson.Write(Formula.Number("3", 3d))!.GetValue<double>());
        Assert.Null(FormulaJson.Write(Formula.Nothing));
    }

    [Fact]
    public void A_name_is_written_as_a_tagged_shape_rather_than_a_spelling()
    {
        var written = FormulaJson.Write(Formula.Name("count"))!.AsObject();

        Assert.Equal("count", written["name"]!.GetValue<string>());
        Assert.DoesNotContain("$", written.ToJsonString());
    }

    [Fact]
    public void A_name_keeps_its_id()
    {
        var written = FormulaJson.Write(Formula.Name("count", "a7k2m9"))!.AsObject();

        Assert.Equal("count", written["name"]!.GetValue<string>());
        Assert.Equal("a7k2m9", written["id"]!.GetValue<string>());
        Assert.Equal(new VariableNode("count", "a7k2m9"), FormulaJson.Read(written));
    }

    [Fact]
    public void A_name_without_an_id_writes_what_it_always_did()
    {
        var written = FormulaJson.Write(Formula.Name("count"))!.AsObject();

        Assert.Null(written["id"]);
        Assert.Equal("{\"name\":\"count\"}", written.ToJsonString());
    }

    [Fact]
    public void An_unknown_object_is_refused()
    {
        var written = JsonNode.Parse("{\"wat\":1}")!.AsObject();

        Assert.Throws<InvalidDataException>(() => FormulaJson.Read(written));
    }

    [Fact]
    public void An_id_without_a_name_is_refused()
    {
        var written = JsonNode.Parse("{\"id\":\"a7k2m9\"}")!.AsObject();

        Assert.Throws<InvalidDataException>(() => FormulaJson.Read(written));
    }

    [Fact]
    public void A_malformed_id_is_refused()
    {
        var written = JsonNode.Parse("{\"name\":\"count\",\"id\":\"nope\"}")!.AsObject();

        Assert.Throws<InvalidDataException>(() => FormulaJson.Read(written));
    }

    [Fact]
    public void A_run_keeps_its_text_and_the_name_beside_it()
    {
        var run = Formula.Run([Formula.Text("你好，"), Formula.Name("who"), Formula.Text("！")]);
        var written = FormulaJson.Write(run)!.AsObject();

        Assert.Equal(3, written["run"]!.AsArray().Count);

        var read = FormulaJson.Read(written);
        Assert.Equal(["who"], Expression.References(read));
        Assert.Equal("你好，「who」！", Formula.Speak(read));
    }

    [Fact]
    public void A_formula_keeps_its_names_and_its_working_out()
    {
        var call = Formula.Binary("+", Formula.Call("upper", Formula.Name("name")), Formula.Text("!"));
        var read = FormulaJson.Read(FormulaJson.Write(call));

        Assert.Equal(["name"], Expression.References(read));
        Assert.Equal("ADA!", Evaluate(read, ("name", "ada")));
    }

    [Fact]
    public void Reading_what_was_written_gives_the_same_shape_back()
    {
        var original = new UnaryNode("not",
            new BinaryNode("==", Formula.Name("count"), Formula.Number("3", 3d)));

        Assert.Equal(original, FormulaJson.Read(FormulaJson.Write(original)));
    }

    [Fact]
    public void Text_alone_is_never_read_back_as_a_formula()
    {
        // A sentence holding a full stop or a bracket is text: the file says "text" by writing a
        // string, so nothing has to be guessed from the characters.
        const string sentence = "调用 upper(name) 之前请先看一眼";

        Assert.Equal(sentence, Formula.Plain(FormulaJson.Read(FormulaJson.Write(Formula.Text(sentence)))));
    }

    [Fact]
    public void Speaking_a_value_leaves_no_sign_for_a_name()
    {
        var value = Formula.Run(
            [Formula.Text("取 "), Formula.Name("clipboard"), Formula.Text(" 里的 "), Formula.Name("item")]);

        Assert.Equal("取 「clipboard」 里的 「item」", Formula.Speak(value));
    }

    [Fact]
    public void A_run_of_one_piece_is_that_piece()
    {
        Assert.Equal(Formula.Text("a"), Formula.Run([Formula.Text("a")]));
        Assert.Equal(Formula.Nothing, Formula.Run([]));
        Assert.Equal(Formula.Nothing, Formula.Run([Formula.Text(string.Empty)]));
    }

    [Fact]
    public void A_json_scalar_is_the_shape_it_is_not_a_line_to_read_back()
    {
        // A number and a truth value keep their kind through the file, and a string that happens
        // to look like a formula stays the words it is. Nothing is parsed here.
        Assert.Equal(Formula.Number(3.5), FormulaJson.Read(JsonValue.Create(3.5)));
        Assert.Equal(Formula.Constant("true"), FormulaJson.Read(JsonValue.Create(true)));
        Assert.Equal(Formula.Constant("false"), FormulaJson.Read(JsonValue.Create(false)));
        Assert.Equal(Formula.Text("n + 1"), FormulaJson.Read(JsonValue.Create("n + 1")));
    }

    private static string Evaluate(ExpressionNode tree, params (string Name, string Value)[] variables)
    {
        var bag = new WhaleGenie.Core.Variables.VariableBag();
        foreach (var (name, value) in variables)
        {
            bag.Set(name, Value.FromText(value));
        }

        return Expression.Evaluate(tree, bag).AsText();
    }
}
