using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// The shape of a value: a place, a record, a call argument, and a break inside a case.
public partial class MacroCheckTests
{
    /// <summary>
    /// A step that says where it works says it once: a record, or the numbers beside it. Neither
    /// is a step that would work at nothing, which the editor can see before the run.
    /// </summary>
    [Fact]
    public void A_place_with_neither_a_record_nor_numbers_is_reported()
    {
        var empty = Step("window.move");
        var problem = Assert.Single(Inspect(empty));
        Assert.Same(empty, problem.Step);

        Assert.Equal(Strings.Get("Editor.Problem.WindowBounds"), problem.Message);

        // A record named in the field is a whole answer as far as the steps alone can tell.
        var writes = Step("control.setVariable", Text("name", "spot"), Text("value", "1"));
        Assert.Empty(Inspect(writes, Step("window.move", Text("rect", "spot"))));

        // So is a drag that starts at a record and ends at numbers.
        Assert.Empty(Inspect(writes, Step("input.mouseDrag",
            Text("startPlace", "spot"), Text("endX", "10"), Text("endY", "10"))));
    }
    /// <summary>
    /// A window record goes into a window field as it stands: what it is compared against comes out
    /// of the record by the way the step says to match, so this is a complete answer.
    /// </summary>
    [Fact]
    public void A_window_record_can_be_the_window_a_step_names()
    {
        var info = Step("window.info", Text("window", "Notepad"), Results("window"));
        var press = Step("input.keyPress", Text("key", "F5"), Text("inputMode", "background"),
            Text("window", "window"));

        var problems = Inspect(info, press);
        Assert.DoesNotContain(problems, problem => problem.Step == press);
    }
    /// <summary>
    /// 多分支 case 里的 break 只离开这个多分支，所以不在循环里也不算错；不在循环也不在多分支里的
    /// break 仍旧标出来。
    /// </summary>
    [Fact]
    public void A_break_in_a_case_is_not_a_break_outside_a_loop()
    {
        var branch = Step("control.switch",
            Text("switchValue", "a"), Text("switchMode", "equals"),
            Steps("cases", Step("control.case", Text("values", "a"),
                Steps("body", Step("control.break")))));

        Assert.Empty(Inspect(branch));

        var alone = Step("control.sequence", Steps("steps", Step("control.break")));
        var problem = Assert.Single(Inspect(alone));
        Assert.Equal(Strings.Get("Editor.Problem.BreakOutsideLoop"), problem.Message);
    }
}
