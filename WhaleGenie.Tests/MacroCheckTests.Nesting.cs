using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Nested loops and calls that circle back, a switch with nothing to pick, and names the run keeps.
public partial class MacroCheckTests
{
    /// <summary>
    /// 内层循环用外层的变量名时标一条：它结束之后外层的值就没有了。
    /// </summary>
    [Fact]
    public void A_nested_loop_with_the_same_variable_is_marked()
    {
        var inner = Step("control.forEach", Text("items", "[]"), Text("itemVariable", "item"),
            Steps("body"));
        var outer = Step("control.forEach", Text("items", "[]"), Text("itemVariable", "item"),
            Steps("body", inner));

        var problem = Assert.Single(Inspect(outer), candidate => candidate.Step == inner);
        Assert.Equal(Strings.Format("Editor.Problem.LoopVariableShadowed", "item"),
            problem.Message);
    }

    /// <summary>
    /// 两个宏互相调用时，沿调用图的分析要停下来，而不是一直跟下去。
    /// </summary>
    [Fact]
    public void A_circle_of_calls_does_not_make_the_analysis_run_forever()
    {
        var toB = Step("control.runMacro", Text("macro", "乙"));
        var toA = Step("control.runMacro", Text("macro", "甲"));
        var a = Callable("甲", null, null, toB);
        var b = Callable("乙", null, null, toA,
            Step("browser.switchTab", Text("tabHow", "newest")));

        var problems = Ui.Run(() => MacroCheck.Inspect([toA], null, [a, b], browserShared: true));

        // 环里仍然找得到乙的浏览器步骤：分析有结论，而且只报一次。
        var problem = Assert.Single(problems, candidate => candidate.Step == toA);
        Assert.Equal(Strings.Get("Editor.Problem.SharedBrowser"), problem.Message);
    }

    [Fact]
    public void A_switch_with_neither_a_case_nor_an_otherwise_is_reported()
    {
        var empty = Step("control.switch",
            Text("switchValue", "1"), Text("switchMode", "equals"),
            Steps("cases"), Steps("otherwise"));

        var problem = Assert.Single(Inspect(empty));
        Assert.Same(empty, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.EmptySwitch"), problem.Message);

        var withCase = Step("control.switch",
            Text("switchValue", "1"), Text("switchMode", "equals"),
            Steps("cases", Step("control.case", Text("values", "1"), Steps("body", Set("结果", "1")))),
            Steps("otherwise"));
        Assert.Empty(Inspect(withCase));

        var withOtherwise = Step("control.switch",
            Text("switchValue", "1"), Text("switchMode", "equals"),
            Steps("cases"), Steps("otherwise", Set("结果", "1")));
        Assert.Empty(Inspect(withOtherwise));
    }

    [Theory]
    [InlineData("sys.loopIndex")]
    [InlineData("sys.anything")]
    [InlineData("step.abcd.outcome")]
    public void A_variable_using_a_name_the_run_keeps_is_reported(string name)
    {
        var step = Set(name, "1");

        var problem = Assert.Single(Inspect(step));

        Assert.Same(step, problem.Step);
        Assert.Equal(Strings.Format("Editor.Problem.ReservedVariable", name), problem.Message);
    }

    [Fact]
    public void An_ordinary_variable_name_is_left_alone()
        => Assert.Empty(Inspect(Set("count", "1")));

    [Fact]
    public void A_fail_step_reports_only_user_keys_it_can_read_back()
    {
        Assert.Empty(Inspect(Step("control.fail",
            Text("failureKey", "User.NoScreen"), Text("reason", "the screen moved"))));

        var problem = Assert.Single(Inspect(Step("control.fail",
            Text("failureKey", "Run.NotMine"))));
        Assert.Equal(Strings.Format("Editor.Problem.BadUserFailureKey", "Run.NotMine"),
            problem.Message);
    }
}
