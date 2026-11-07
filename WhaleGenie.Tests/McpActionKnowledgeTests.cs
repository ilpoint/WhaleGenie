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

    /// <summary>
    /// 不可逆是两半：动作本身（删文件、删文件夹），以及某一格里某个取值（关机的睡眠、重启、关机）。
    /// 只给动作级布尔，客户端会以为「关机」这一步可以随便写；两半都给，它才复现得出运行期那一次询问。
    /// </summary>
    [Fact]
    public void A_step_that_cannot_be_undone_says_so()
    {
        Assert.True(Assert.IsType<McpActionDetails>(ActionKnowledge.Find("file.delete")).Irreversible);
        Assert.True(Assert.IsType<McpActionDetails>(
            ActionKnowledge.Find("file.deleteFolder")).Irreversible);

        // 关机本身不是不可逆的，锁屏与关显示器更不是；是那么几个取值让这一步变成不可逆。
        var power = Assert.IsType<McpActionDetails>(ActionKnowledge.Find("system.power"));
        Assert.False(power.Irreversible);
        var what = Assert.Single(power.Parameters, parameter => parameter.Name == "powerAction");
        Assert.Equal(
            ["hibernate", "restart", "shutDown", "signOut", "sleep"],
            [.. what.IrreversibleWhen.Order(StringComparer.Ordinal)]);

        // 其余动作两处都是空的，客户端不会到处看到这一句。
        var delay = Assert.IsType<McpActionDetails>(ActionKnowledge.Find("control.delay"));
        Assert.False(delay.Irreversible);
        Assert.All(delay.Parameters, parameter => Assert.Empty(parameter.IrreversibleWhen));
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
