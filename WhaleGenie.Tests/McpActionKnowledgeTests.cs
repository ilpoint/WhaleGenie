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
        Assert.False(details.Hidden);

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

    [Fact]
    public void A_field_that_is_only_sometimes_required_says_so()
    {
        // A background click needs a window rule and a foreground one does not. Reported as plainly
        // "required", every client would fill in a window for a click that never wanted one.
        var click = Assert.IsType<McpActionDetails>(ActionKnowledge.Find("input.mouseClick"));

        var window = Assert.Single(click.Parameters, parameter => parameter.Name == "window");
        Assert.True(window.Required);
        Assert.True(window.Conditional);
        Assert.Equal("Run.MissingTargetWindow", window.MissingKey);

        var x = Assert.Single(click.Parameters, parameter => parameter.Name == "x");
        Assert.False(x.Required);
        Assert.False(x.Conditional);
    }

    [Fact]
    public void MCP_reports_hidden_actions_and_the_keys_each_child_list_accepts()
    {
        var choice = Assert.IsType<McpActionDetails>(ActionKnowledge.Find("control.switch"));
        var cases = Assert.Single(choice.Parameters, parameter => parameter.Name == "cases");
        var otherwise = Assert.Single(choice.Parameters, parameter => parameter.Name == "otherwise");

        Assert.False(choice.Hidden);
        Assert.Equal(["control.case"], cases.ChildKeys);
        Assert.Empty(otherwise.ChildKeys);

        Assert.True(Assert.IsType<McpActionDetails>(
            ActionKnowledge.Find("control.case")).Hidden);
        Assert.True(Assert.IsType<McpActionDetails>(
            ActionKnowledge.Find("control.sequence")).Hidden);
        Assert.True(Assert.Single(
            ActionKnowledge.Search("case", null, includeHidden: true, 0, 20).Actions,
            action => action.Key == "control.case").Hidden);
    }

    [Fact]
    public void A_page_says_what_it_left_out()
    {
        var first = ActionKnowledge.Search(null, null, false, 0, 5);
        Assert.Equal(5, first.Actions.Count);
        Assert.True(first.Total > 5);
        Assert.True(first.HasMore);
        Assert.NotEmpty(first.Categories);

        var last = ActionKnowledge.Search(null, null, false, first.Total - 3, 25);
        Assert.Equal(3, last.Actions.Count);
        Assert.False(last.HasMore);

        // A hidden action is not something a client should meet by accident: it is only valid in a
        // child list whose childKeys name it, so it is left out unless it is asked for.
        var shown = ActionKnowledge.Search(null, "Control", false, 0, 200);
        var hidden = ActionKnowledge.Search(null, "Control", true, 0, 200);
        Assert.DoesNotContain(shown.Actions, action => action.Hidden);
        Assert.Contains(hidden.Actions, action => action.Hidden);
        Assert.True(hidden.Total > shown.Total);

        // The category counts are over the whole catalogue, so the two agree when nothing is filtered.
        Assert.Equal(hidden.Total,
            hidden.Categories.Single(category => category.Name == "Control").Count);
    }
}
