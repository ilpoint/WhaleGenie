using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Names a step reads and writes, and the ones a script or a file may bring in.
public partial class MacroCheckTests
{
    [Fact]
    public void A_script_that_can_write_variables_makes_names_open()
    {
        var script = Step("script.python", Text("script", "print('hi')"));
        var reading = Set("副本", Formula.Name("madeByScript"));

        Assert.Empty(Inspect(script, reading));
    }

    [Fact]
    public void A_result_no_step_writes_is_reminded()
    {
        var own = Callable("自己", results: ["答案"], steps: [Set("别的", Formula.Number("1", 1d))]);

        var warning = Assert.Single(MacroCheck.DeclarationProblems(own));

        Assert.Equal(Strings.Format("Editor.Problem.ResultNeverWritten", "自己", "答案"),
            warning);
    }

    [Fact]
    public void A_result_written_by_a_script_or_an_in_out_parameter_is_not_reminded()
    {
        var byScript = Callable("自己", results: ["答案"],
            steps: [Step("script.python", Text("script", "print('hi')"))]);
        var inOut = Callable("自己", parameters: ["答案"], results: ["答案"], steps: []);

        Assert.Empty(MacroCheck.DeclarationProblems(byScript));
        Assert.Empty(MacroCheck.DeclarationProblems(inOut));
    }

    [Fact]
    public void A_variable_nothing_defines_is_reported()
    {
        var reader = Set("结果", Formula.Binary("+", Formula.Name("没定义的"), Formula.Number("1", 1d)));

        var problem = Assert.Single(Inspect(reader));

        Assert.Same(reader, problem.Step);
    }
    [Fact]
    public void A_variable_the_macro_defines_itself_is_left_alone()
        => Assert.Empty(Inspect(Set("计数", Formula.Number("0", 0d)), Set("结果", Formula.Binary("+", Formula.Name("计数"), Formula.Number("1", 1d)))));

    /// <summary>
    /// 宏自己的形参就是它读得到的名字：调用方把值交给这个名字，宏里的步骤照名字读。少了这一条，
    /// 「提为子宏」提出来的那个宏一读自己的参数就被标成"没人定义"，而那正是提子宏这一步做出来的
    /// 形状；没声明过、别人也没交过来的名字照旧要报。
    /// </summary>
    [Fact]
    public void A_macro_may_read_the_names_it_declares()
    {
        var reading = Step("control.log", Value("message", Formula.Name("谁")));
        var own = Callable("自己", parameters: ["谁"]);

        Assert.Empty(Ui.Run(() => MacroCheck.Inspect([reading], own, [own], [])));
        Assert.Single(Against("自己", [], reading));
    }

    [Fact]
    public void A_system_variable_is_left_alone()
        => Assert.Empty(Inspect(Set("结果", Formula.Name("sys.date"))));
    [Fact]
    public void A_name_a_called_macro_brings_back_is_left_alone()
        => Assert.Empty(Against("自己", [Callable("另一个", results: ["答案"])],
            Call("另一个", "答案"),
            Set("结果", Formula.Binary("+", Formula.Name("答案"), Formula.Number("1", 1d)))));
    [Fact]
    public void Loading_variables_from_a_file_silences_the_check()
    {
        // A file can hold any name at all, so nothing can be said about what the macro may read.
        var load = Step("file.loadVariables", Text("path", "state.json"));

        Assert.Empty(Inspect(load, Set("结果", Formula.Name("来自文件"))));
    }
}
