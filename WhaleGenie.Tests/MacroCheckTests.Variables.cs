using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
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
        var reading = Set("副本", "$madeByScript");

        Assert.Empty(Inspect(script, reading));
    }

    [Fact]
    public void A_result_no_step_writes_is_reminded()
    {
        var own = Callable("自己", results: ["答案"], steps: [Set("别的", "1")]);

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
        var reader = Set("结果", "$没定义的 + 1");

        var problem = Assert.Single(Inspect(reader));

        Assert.Same(reader, problem.Step);
    }

    [Fact]
    public void A_doubled_dollar_is_left_alone()
    {
        var writer = Step("file.listFiles",
            Text("folder", "."),
            Text("pattern", "costs $$5 and $$未知 stays as it is"));

        Assert.Empty(Inspect(writer));
    }

    [Fact]
    public void A_value_that_starts_with_a_variable_is_read_as_one()
    {
        // The engine reads a value that starts with $ as an expression, so a name nothing defines
        // there stops the step rather than quietly becoming text.
        var writer = Step("file.writeText",
            Text("path", "notes.txt"),
            Text("fileContent", "$未知"));

        Assert.Single(Inspect(writer));
    }

    [Fact]
    public void A_variable_the_macro_defines_itself_is_left_alone()
        => Assert.Empty(Inspect(Set("计数", "0"), Set("结果", "$计数 + 1")));

    /// <summary>
    /// 宏自己的形参就是它读得到的名字：调用方把值交给这个名字，宏里的步骤照 $名字 读。少了这一条，
    /// 「提为子宏」提出来的那个宏一读自己的参数就被标成"没人定义"，而那正是提子宏这一步做出来的
    /// 形状；没声明过、别人也没交过来的名字照旧要报。
    /// </summary>
    [Fact]
    public void A_macro_may_read_the_names_it_declares()
    {
        var reading = Step("control.log", Value("message", "$谁"));
        var own = Callable("自己", parameters: ["谁"]);

        Assert.Empty(Ui.Run(() => MacroCheck.Inspect([reading], own, [own])));
        Assert.Single(Against("自己", [], reading));
    }

    [Fact]
    public void A_system_variable_is_left_alone()
        => Assert.Empty(Inspect(Set("结果", "$sys.date")));

    /// <summary>
    /// A text field names a variable only where <c>$</c> starts a readable name. Numeric names are
    /// readable, so a literal amount needs the doubled-dollar escape while an unresolved name is
    /// still reported.
    /// </summary>
    [Fact]
    public void A_text_field_leaves_prices_and_escaped_dollars_alone()
    {
        var plain = Step("file.listFiles", Text("folder", "."),
            Text("pattern", "$$100 off; price $$100; $$name"));
        Assert.Empty(Inspect(plain));

        var named = Step("file.listFiles", Text("folder", "."), Text("pattern", "$name"));
        var problem = Assert.Single(Inspect(named));
        Assert.Contains("name", problem.Message);

        var numeric = Step("file.listFiles", Text("folder", "."), Text("pattern", "$123"));
        Assert.Single(Inspect(numeric));
    }

    /// <summary>A whole value is a value, not a name: a bare word in it is just text.</summary>
    [Fact]
    public void A_bare_word_in_a_whole_value_is_not_a_variable()
        => Assert.Empty(Inspect(Step("file.writeText", Text("path", "out.txt"),
            Value("fileContent", "total"))));

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
            Text("kind", "ps1"),
            Text("script", "Write-Host $env:TEMP; $result = 1"),
            Text("scriptArguments", "-Name $name"));

        Assert.Empty(Inspect(script));
    }

    [Fact]
    public void Loading_variables_from_a_file_silences_the_check()
    {
        // A file can hold any name at all, so nothing can be said about what the macro may read.
        var load = Step("file.loadVariables", Text("path", "state.json"));

        Assert.Empty(Inspect(load, Set("结果", "$来自文件")));
    }
}
