using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

public class FlowStructureTests
{
    [Theory]
    [InlineData("control.sequence", "steps")]
    [InlineData("control.holdInput", "steps")]
    [InlineData("control.holdBrowser", "steps")]
    [InlineData("control.repeat", "body")]
    [InlineData("control.while", "body")]
    [InlineData("control.forEach", "body")]
    [InlineData("control.for", "body")]
    [InlineData("control.if", "then,else")]
    [InlineData("control.switch", "cases,otherwise")]
    [InlineData("control.case", "body")]
    [InlineData("control.try", "body,catch,finally")]
    public void Every_flow_container_exposes_its_declared_child_lists(string type, string names)
    {
        var step = new MacroStep { Type = type };

        step.EnsureDeclaredLists();

        Assert.Equal(names.Split(','), step.StepLists.Select(list => list.Name));
        Assert.True(step.IsExpanded);
        Assert.All(step.StepLists, list => Assert.Empty(list.Steps));
    }

    [Fact]
    public void A_condition_only_flow_action_has_no_step_list()
    {
        var step = new MacroStep { Type = "control.waitUntil" };

        step.EnsureDeclaredLists();

        Assert.Empty(step.StepLists);
    }
}
