using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// What the editor can tell about a macro before it runs. The point of the check is to catch the
/// mistakes a run would otherwise report late or hide, so the cases here are the ones it decides
/// from the steps alone — and the ones it must keep quiet about.
/// </summary>
public class MacroCheckTests
{
    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static StepParameter Steps(string name, params MacroStep[] steps)
        => new() { Name = name, Kind = ActionParameterKind.Steps, Steps = [.. steps] };

    private static StepParameter Text(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Text, Value = value };

    private static StepParameter Value(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Expression, Value = value };

    private static MacroStep Set(string name, string value)
        => Step("control.setVariable", Text("name", name), Text("scope", "local"), Value("value", value));

    private static MacroStep Repeat(params MacroStep[] body)
        => Step("control.repeat", Text("times", "3"), Steps("body", body));

    /// <summary>A call to another macro, asking for whatever of its results are named.</summary>
    private static MacroStep Call(string macro, params string[] wants)
        => Step("control.runMacro", Text("macro", macro), Results(wants));

    /// <summary>The results a step hands over, one row each, named the way the file writes them.</summary>
    private static StepParameter Results(params string[] names)
        => new()
        {
            Name = "returns",
            Kind = ActionParameterKind.Returns,
            Rows =
            [
                .. names.Select(name => new StepParameterRow
                {
                    Columns = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["key"] = name,
                        ["name"] = name,
                    },
                }),
            ],
        };

    /// <summary>What one macro of the project asks a call to fill in and offers to hand back.</summary>
    private static MacroCallable Callable(string name, string[]? parameters = null,
        string[]? results = null)
        => new(name,
            [.. (parameters ?? []).Select(parameter => new MacroParameter { Name = parameter })],
            [.. (results ?? []).Select(result => new MacroResult { Name = result })]);

    /// <summary>
    /// Runs the check on the thread the headless session owns: it reads the action catalogue,
    /// whose icons are Avalonia geometries.
    /// </summary>
    private static IReadOnlyList<MacroProblem> Inspect(params MacroStep[] steps)
        => Ui.Run(() => MacroCheck.Inspect(steps, null, []));

    private static IReadOnlyList<MacroProblem> Against(string ownName, MacroCallable[] macros,
        params MacroStep[] steps)
        => Ui.Run(() => MacroCheck.Inspect(steps, Callable(ownName), macros));

    /// <summary>
    /// A step that sends keys, written the way the file writes one. What it sends does not matter to
    /// the check — that it sends anything at all is the whole of the question.
    /// </summary>
    private static MacroStep Pressing()
        => Step("input.keyPress", Text("key", "F5"));

    /// <summary>
    /// 项目里还有别的宏也发键鼠时，说一次：两个宏同时跑，输入会写进对方中间，而单看哪一个宏都
    /// 看不出这件事。提示落在第一次发输入的那一步上，一句话就够。
    /// </summary>
    [Fact]
    public void Sending_input_next_to_another_macro_that_does_is_said_once()
    {
        var first = Pressing();
        var second = Pressing();

        var problems = Ui.Run(() => MacroCheck.Inspect([first, second], null, [], inputShared: true));

        var problem = Assert.Single(problems);
        Assert.Same(first, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.SharedInput"), problem.Message);

        // 没人跟它抢的时候什么也不说，没勾上的宏也一样。
        Assert.Empty(Inspect(first, second));
    }

    /// <summary>
    /// 只看屏幕的宏不掺和这件事：两边都读屏幕没有任何要排队的地方。
    /// </summary>
    [Fact]
    public void A_macro_that_only_looks_at_the_screen_is_not_warned_about()
    {
        var reading = Step("vision.capture");

        Assert.Empty(Ui.Run(() => MacroCheck.Inspect([reading], null, [], inputShared: true)));
    }

    /// <summary>
    /// 相对移动在后台这一档做不了：位置会算错，而且错得看不出来。步骤自己的设置就能判定，所以
    /// 写宏的时候就标出来；把输入方式改成前台或驱动级（或者根本没写这一格）就不标。
    /// </summary>
    [Fact]
    public void A_relative_move_posted_at_a_window_is_reported()
    {
        var posted = Step("input.mouseMoveRelative",
            Text("dx", "200"), Text("inputMode", "background"), Text("targetWindow", "Notepad"));

        var problem = Assert.Single(Inspect(posted));

        Assert.Same(posted, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.PostedMove"), problem.Message);

        // 前台与驱动级各自都是从指针当前位置往前走的，不标；别的动手的事也问不到这一条。
        Assert.Empty(Inspect(Step("input.mouseMoveRelative", Text("dx", "200"))));
        Assert.Empty(Inspect(Step("input.mouseMoveRelative",
            Text("dx", "200"), Text("inputMode", "driver"))));
        Assert.Empty(Inspect(Step("input.mouseMove",
            Text("x", "10"), Text("y", "20"), Text("inputMode", "background"),
            Text("targetWindow", "Notepad"))));
    }

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

    [Fact]
    public void A_break_inside_a_loop_is_left_alone()
        => Assert.Empty(Inspect(Repeat(Step("control.break"))));

    [Fact]
    public void A_break_inside_a_block_inside_a_loop_is_left_alone()
    {
        // An if branch and a plain block both let a break reach the loop around them, so neither
        // is a place where "no loop above it" is true.
        var ifStep = Step("control.if", Steps("then",
            Step("control.sequence", Steps("steps", Step("control.break")))));

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
    public void A_macro_calling_itself_is_left_alone()
        => Assert.Empty(Against("自己", [], Call("自己")));

    [Fact]
    public void A_call_that_names_no_macro_is_reported()
        => Assert.Single(Against("自己", [Callable("另一个")], Call("")));

    [Fact]
    public void A_call_that_fills_in_a_name_the_called_macro_never_declared_is_reported()
    {
        var passed = Step("control.runMacro",
            Text("macro", "另一个"), Value("arg.别的", "1"));

        var problem = Assert.Single(Against("自己", [Callable("另一个", parameters: ["次数"])], passed));

        Assert.Same(passed, problem.Step);
    }

    [Fact]
    public void A_call_that_asks_for_a_result_the_called_macro_never_offered_is_reported()
    {
        var asked = Call("另一个", "别的");

        var problem = Assert.Single(Against("自己",
            [Callable("另一个", results: ["答案"])], asked));

        Assert.Same(asked, problem.Step);
    }

    [Fact]
    public void A_variable_nothing_defines_is_reported()
    {
        var reader = Set("结果", "$没定义的 + 1");

        var problem = Assert.Single(Inspect(reader));

        Assert.Same(reader, problem.Step);
    }

    [Fact]
    public void A_dollar_in_the_middle_of_text_is_left_alone()
    {
        // A literal dollar is written into text the same way a variable is, so the check does not
        // guess: only a value that starts with $ can only have been meant as a variable.
        var writer = Step("file.writeText",
            Text("path", "notes.txt"),
            Text("text", "costs $5 and $未知 stays as it is"));

        Assert.Empty(Inspect(writer));
    }

    [Fact]
    public void A_value_that_starts_with_a_variable_is_read_as_one()
    {
        // The engine reads a value that starts with $ as an expression, so a name nothing defines
        // there stops the step rather than quietly becoming text.
        var writer = Step("file.writeText",
            Text("path", "notes.txt"),
            Text("text", "$未知 件"));

        Assert.Single(Inspect(writer));
    }

    [Fact]
    public void A_variable_the_macro_defines_itself_is_left_alone()
        => Assert.Empty(Inspect(Set("计数", "0"), Set("结果", "$计数 + 1")));

    [Fact]
    public void A_system_variable_is_left_alone()
        => Assert.Empty(Inspect(Set("结果", "$sys.date")));

    [Fact]
    public void A_loop_variable_is_left_alone()
    {
        var loop = Step("control.forEach",
            Value("items", "[1, 2]"),
            Text("itemVariable", "项"),
            Steps("body", Set("结果", "$项")));

        Assert.Empty(Inspect(loop));
    }

    [Fact]
    public void A_name_a_called_macro_brings_back_is_left_alone()
        => Assert.Empty(Against("自己", [Callable("另一个", results: ["答案"])],
            Call("另一个", "答案"),
            Set("结果", "$答案 + 1")));

    [Fact]
    public void Text_written_in_another_language_is_not_read_for_variables()
    {
        // A script's $是那个语言自己的变量，和宏的变量没有关系。
        var script = Step("script.run",
            Text("language", "powershell"),
            Text("script", "Write-Host $env:TEMP; $result = 1"),
            Text("arguments", "-Name $name"));

        Assert.Empty(Inspect(script));
    }

    [Fact]
    public void Loading_variables_from_a_file_silences_the_check()
    {
        // A file can hold any name at all, so nothing can be said about what the macro may read.
        var load = Step("file.loadVariables", Text("path", "state.json"));

        Assert.Empty(Inspect(load, Set("结果", "$来自文件")));
    }

    [Fact]
    public void One_step_can_be_wrong_more_than_once_and_each_reason_is_said()
    {
        var step = Set("结果", "$甲 + $乙");

        var problems = Inspect(step);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Same(step, problem.Step));
    }
}
