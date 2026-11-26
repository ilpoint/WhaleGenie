using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Editing;

namespace WhaleGenie.Tests;

/// <summary>
/// The wire parts and the shape they stand for are two directions of one cell. A part that names
/// a variable is a name; a part that is text stays text, even when its kind says otherwise and
/// its name is empty.
/// </summary>
public class EditorValuePartsTests
{
    [Fact]
    public void A_run_of_text_and_a_name_comes_back_as_the_run_it_was()
    {
        var run = Formula.Run([Formula.Text("共 "), Formula.Name("count"), Formula.Text(" 件")]);
        var parts = EditorValueParts.Of(run);

        Assert.Equal(["text", "name", "text"], parts.Select(part => part.Kind));
        Assert.True(JsonNode.DeepEquals(FormulaJson.Write(run),
            FormulaJson.Write(EditorValueParts.ShapeOf(parts))));
    }

    [Fact]
    public void Empty_text_has_no_parts()
    {
        Assert.Empty(EditorValueParts.Of(Formula.Nothing));
        Assert.Empty(EditorValueParts.Of(Formula.Text(string.Empty)));
    }

    [Fact]
    public void A_part_that_names_nothing_is_text()
    {
        var shape = EditorValueParts.ShapeOf(
            [new EditorValuePartSnapshot("name", "literally text", string.Empty)]);

        Assert.Equal(Formula.Text("literally text"), shape);
    }

    [Fact]
    public void A_part_that_is_text_never_becomes_a_name()
    {
        var shape = EditorValueParts.ShapeOf(
            [new EditorValuePartSnapshot("text", "count", string.Empty)]);

        Assert.Equal(Formula.Text("count"), shape);
    }

    [Fact]
    public void A_part_that_names_a_variable_is_a_name()
    {
        var shape = EditorValueParts.ShapeOf(
            [new EditorValuePartSnapshot("name", string.Empty, "count")]);

        Assert.Equal(Formula.Name("count"), shape);
    }
}
