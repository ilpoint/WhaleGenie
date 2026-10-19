using WhaleGenie.Mcp;

namespace WhaleGenie.Tests;

/// <summary>
/// An MCP client writes the file shape rather than using the editor, so the action catalogue has
/// to say which names live inside a record and what a number field means when it is left empty.
/// </summary>
public class McpActionKnowledgeTests
{
    [Fact]
    public void A_window_rule_and_its_numbers_are_self_describing()
    {
        var details = Assert.IsType<McpActionDetails>(ActionKnowledge.Find("window.move"));

        var window = Assert.Single(details.Parameters, parameter => parameter.Name == "window");
        Assert.Equal("WindowRule", window.Shape);
        Assert.Equal(
            ["handle", "title", "process", "class", "how", "caseSensitive", "trim", "multiline"],
            window.Fields);

        var x = Assert.Single(details.Parameters, parameter => parameter.Name == "x");
        Assert.Equal("Pixels", x.Unit);
        Assert.Equal("Keep", x.EmptyMeans);
        Assert.Equal(-100_000m, x.Minimum);
        Assert.Equal(100_000m, x.Maximum);

        var width = Assert.Single(details.Parameters, parameter => parameter.Name == "width");
        Assert.Equal(1m, width.Minimum);
        Assert.Equal(100_000m, width.Maximum);

        var rect = Assert.Single(details.Parameters, parameter => parameter.Name == "rect");
        Assert.Equal(["left", "top", "width", "height"], rect.Fields);
    }
}
