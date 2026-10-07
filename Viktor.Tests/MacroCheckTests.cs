using Viktor.Models;

namespace Viktor.Tests;

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

    private static MacroStep Call(string macro, string returns = "")
        => Step("control.runMacro", Text("macro", macro), Text("returns", returns));

    /// <summary>
    /// Runs the check on the thread the headless session owns: it reads the action catalogue,
    /// whose icons are Avalonia geometries.
    /// </summary>
    private static IReadOnlyList<MacroProblem> Inspect(params MacroStep[] steps)
        => Ui.Run(() => MacroCheck.Inspect(steps, [], ""));

    private static IReadOnlyList<MacroProblem> Against(string ownName, string[] macros,
        params MacroStep[] steps)
        => Ui.Run(() => MacroCheck.Inspect(steps, macros, ownName));

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
        Assert.Empty(Against("调用者", ["半途"], Call("半途")));
        Assert.Empty(Against("半途", ["调用者"], Repeat(Step("control.break"))));
    }

    [Fact]
    public void A_call_to_a_macro_that_is_not_there_is_reported()
    {
        var missing = Call("没有这个宏");

        var problem = Assert.Single(Against("自己", ["另一个"], missing));

        Assert.Same(missing, problem.Step);
    }

    [Fact]
    public void A_call_to_a_macro_that_is_there_is_left_alone()
        => Assert.Empty(Against("自己", ["另一个"], Call("另一个")));

    [Fact]
    public void A_macro_calling_itself_is_left_alone()
        => Assert.Empty(Against("自己", [], Call("自己")));

    [Fact]
    public void A_call_that_names_no_macro_is_reported()
        => Assert.Single(Against("自己", ["另一个"], Call("")));

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
        => Assert.Empty(Against("自己", ["另一个"],
            Call("另一个", returns: "答案"),
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
