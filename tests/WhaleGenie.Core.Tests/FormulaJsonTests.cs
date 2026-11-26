using System.Collections.Generic;
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
    public void A_name_is_written_as_its_name_and_nothing_else()
    {
        var written = FormulaJson.Write(Formula.Name("count"))!.AsObject();

        Assert.Equal("count", written["name"]!.GetValue<string>());
        Assert.Single(written);
        Assert.Equal(new VariableNode("count"), FormulaJson.Read(written));
    }

    [Fact]
    public void An_unknown_object_is_refused()
    {
        var written = JsonNode.Parse("{\"wat\":1}")!.AsObject();

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
        Assert.Equal("你好，「who」！", Formula.Speak(read, "「", "」"));
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

        Assert.Equal("取 「clipboard」 里的 「item」", Formula.Speak(value, "「", "」"));
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

    /// <summary>
    /// Every shape a field can hold, so the round trip is checked over the language rather than
    /// over one sample. The empty value is in here too: a field nobody filled in is still a field.
    /// </summary>
    public static IEnumerable<ExpressionNode> EveryShape()
    {
        yield return Formula.Nothing;
        yield return Formula.Text("a piece of text");
        yield return Formula.Text("你好，世界");
        yield return Formula.Number(20);
        yield return Formula.Number("1.0", 1d);
        yield return new DateNode("2026-11-26");
        yield return Formula.Name("区域");
        yield return Formula.Run([Formula.Text("取 "), Formula.Name("item"), Formula.Text(" 里的值")]);
        yield return Formula.List(Formula.Number(1), Formula.Text("a"), Formula.Name("n"));
        yield return Formula.Call("upper", Formula.Name("name"));
        yield return Formula.Unary("not", Formula.Name("flag"));
        yield return Formula.Binary("+", Formula.Name("x"), Formula.Number(10));
        yield return new GroupNode(Formula.Binary("+", Formula.Name("x"), Formula.Number(10)));
    }

    [Fact]
    public void Every_shape_comes_back_from_the_file_as_the_same_value()
    {
        // The file is the only copy of the work once the program has closed. What is written has
        // to be what comes back, shape for shape; a line that reads the same is not the same
        // answer when the shape was a name or a formula.
        foreach (var value in EveryShape())
        {
            var written = FormulaJson.Write(value);
            var read = FormulaJson.Read(written);
            Assert.True(JsonNode.DeepEquals(written, FormulaJson.Write(read)),
                value.GetType().Name + " did not come back: "
                + (written?.ToJsonString() ?? "nothing"));
        }
    }

    [Fact]
    public void A_number_is_read_as_a_number_whatever_json_type_it_was_built_with()
    {
        // A node built in memory keeps the CLR type it was made with, and asking one of them for
        // the value turns the others into text. The decimal case is the one a save plan copies
        // through, and it used to come back as a string.
        var nodes = new JsonNode[]
        {
            JsonValue.Create(20),
            JsonValue.Create(20L),
            JsonValue.Create(20m),
            JsonValue.Create(20d),
            JsonValue.Create(20f),
            JsonNode.Parse("20")!,
            JsonNode.Parse("20.5")!,
            JsonNode.Parse("1e3")!,
        };

        foreach (var node in nodes)
        {
            var read = FormulaJson.Read(node);
            Assert.True(read is NumberNode, node.ToJsonString() + " came back as "
                + read.GetType().Name);
        }
    }

    [Fact]
    public void Reading_a_scalar_and_writing_it_back_keeps_the_json_it_was()
    {
        foreach (var json in new[]
                 {
                     "\"你好\"",
                     "\"n + 1\"",
                     "20",
                     "20.5",
                     "true",
                     "false",
                     "{\"name\":\"区域\"}",
                     "{\"run\":[\"取 \",{\"name\":\"item\"}]}",
                 })
        {
            var node = JsonNode.Parse(json);
            Assert.True(JsonNode.DeepEquals(node, FormulaJson.Write(FormulaJson.Read(node))),
                json);
        }
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
