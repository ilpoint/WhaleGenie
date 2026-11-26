using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Editing;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The step tree's pure side: finding a row, copying it without sharing children, and the
/// clipboard array. Selection, undo and the visible rows stay in the editor commands.
/// </summary>
public class StepTreeTests
{
    [Fact]
    public void A_nested_step_is_found_in_its_own_list()
    {
        var child = new MacroStep { Type = "control.delay", Id = "child" };
        var parent = new MacroStep
        {
            Type = "control.if",
            Id = "parent",
            Parameters =
            [
                new StepParameter { Name = "then", Kind = ActionParameterKind.Steps, Steps = [child] },
            ],
        };
        var roots = new List<MacroStep> { parent };

        var found = StepTree.Find(roots, "child");
        var steps = StepTree.FindSteps(roots, ["child", "child", "parent"]);

        Assert.NotNull(found);
        Assert.Same(child, found!.Value.List[found.Value.Index]);
        Assert.Equal([child, parent], steps);
        Assert.Null(StepTree.Find(roots, "missing"));
    }

    [Fact]
    public void A_copy_owns_its_nested_steps_and_parameters()
    {
        var child = new MacroStep { Type = "control.delay", Id = "child" };
        var parent = new MacroStep
        {
            Type = "control.if",
            Id = "parent",
            Parameters =
            [
                new StepParameter
                {
                    Name = "then",
                    Kind = ActionParameterKind.Steps,
                    Steps = [child],
                },
            ],
        };

        var copy = StepTree.Clone(parent);
        copy.Parameters[0].Steps.Add(new MacroStep { Type = "control.log" });

        Assert.NotSame(parent, copy);
        Assert.NotSame(parent.Parameters[0], copy.Parameters[0]);
        Assert.NotSame(child, copy.Parameters[0].Steps[0]);
        Assert.Empty(child.Parameters);
        Assert.Single(parent.Parameters[0].Steps);
    }

    [Fact]
    public void Steps_round_trip_through_the_clipboard_array_and_move_in_their_list()
    {
        var first = new MacroStep { Type = "control.delay", Id = "first" };
        var second = new MacroStep { Type = "control.log", Id = "second" };
        var roots = new List<MacroStep> { first, second };

        var text = StepTree.Serialize(roots);
        var back = StepTree.Deserialize(text);

        Assert.NotNull(back);
        Assert.Equal(["first", "second"], back!.Select(step => step.Id));
        Assert.True(StepTree.Move(roots, 0, 1));
        Assert.Equal(["second", "first"], roots.Select(step => step.Id));
        Assert.False(StepTree.Move(roots, 0, 0));
        Assert.False(StepTree.Move(roots, 0, 9));
        Assert.Null(StepTree.Deserialize("not steps"));
    }
}
