using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Loops and the steps that leave them, including a break inside a called macro.
public partial class MacroCheckTests
{
    [Fact]
    public void A_break_outside_a_loop_is_reported()
    {
        var loose = Step("control.break");

        var problem = Assert.Single(Inspect(loose));

        Assert.Same(loose, problem.Step);
    }

    [Fact]
    public void A_continue_outside_a_loop_is_reported()
    {
        var loose = Step("control.continue");

        var problem = Assert.Single(Inspect(loose, Set("after", "1")));

        Assert.Same(loose, problem.Step);
    }

    [Theory]
    [InlineData("control.if")]
    [InlineData("control.while")]
    [InlineData("control.waitUntil")]
    public void A_condition_block_without_a_condition_is_reported(string action)
    {
        var loose = Step(action);

        var problem = Assert.Single(Inspect(loose));

        Assert.Same(loose, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.MissingCondition"), problem.Message);
    }

    [Fact]
    public void The_loop_round_outside_a_loop_is_reported()
    {
        var loose = Set("after", "$sys.loopIndex");

        var problem = Assert.Single(Inspect(loose));

        Assert.Same(loose, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.LoopIndexOutsideLoop"), problem.Message);
        Assert.Empty(Inspect(Repeat(Set("seen", "$sys.loopIndex"))));
    }

    [Fact]
    public void A_break_inside_a_loop_is_left_alone()
        => Assert.Empty(Inspect(Repeat(Step("control.break"))));

    [Fact]
    public void A_break_inside_a_block_inside_a_loop_is_left_alone()
    {
        // An if branch and a plain block both let a break reach the loop around them, so neither
        // is a place where "no loop above it" is true.
        var ifStep = Step("control.if",
            When("condition", "true"),
            Steps("then", Step("control.sequence", Steps("steps", Step("control.break")))));

        Assert.Empty(Inspect(Repeat(ifStep)));
    }

    [Fact]
    public void A_break_in_a_called_macro_is_judged_on_its_own()
    {
        // The called macro has a loop of its own, so its break is fine even though the call
        // sits at the top of this one.
        Assert.Empty(Against("调用者", [Callable("半途")], Call("半途")));
        Assert.Empty(Against("半途", [Callable("调用者")], Repeat(Step("control.break"))));
    }

    [Fact]
    public void A_call_to_a_macro_that_is_not_there_is_reported()
    {
        var missing = Call("没有这个宏");

        var problem = Assert.Single(Against("自己", [Callable("另一个")], missing));

        Assert.Same(missing, problem.Step);
    }

    [Fact]
    public void A_call_to_a_macro_that_is_there_is_left_alone()
        => Assert.Empty(Against("自己", [Callable("另一个")], Call("另一个")));

    [Fact]
    public void A_call_to_the_macro_itself_without_a_branch_is_reported()
    {
        var call = Call("自己");

        var problem = Assert.Single(Against("自己", [], call));

        Assert.Same(call, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.UnguardedRecursion"), problem.Message);
    }
}
