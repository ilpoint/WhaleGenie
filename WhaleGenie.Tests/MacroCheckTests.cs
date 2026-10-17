using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
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

    private static StepParameter When(string name, MacroStep condition)
        => new() { Name = name, Kind = ActionParameterKind.Condition, Condition = condition };

    private static StepParameter Text(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Text, Value = value };

    private static StepParameter Name(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Variable, Value = value };

    private static StepParameter Value(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Expression, Value = value };

    private static StepParameter Flag(string name, bool value)
        => new() { Name = name, Kind = ActionParameterKind.Bool, Value = value ? "true" : "false" };

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
        string[]? results = null, params MacroStep[] steps)
        => new(name,
            [.. (parameters ?? []).Select(parameter => new MacroParameter { Name = parameter })],
            [.. (results ?? []).Select(result => new MacroResult { Name = result })], steps);

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
    /// 这个项目里还有别的宏用浏览器时，说一次：整个程序只有一份浏览器与当前页，两条宏同时跑时
    /// 一条切页会改变另一条读到的页面。
    /// </summary>
    [Fact]
    public void Using_the_browser_next_to_another_macro_that_does_is_said_once()
    {
        var first = Step("browser.switchTab", Text("tabHow", "newest"));
        var second = Step("browser.goTo", Text("url", "https://example.com"));

        var problems = Ui.Run(() => MacroCheck.Inspect([first, second], null, [],
            browserShared: true));

        var problem = Assert.Single(problems);
        Assert.Same(first, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.SharedBrowser"), problem.Message);

        Assert.Empty(Ui.Run(() => MacroCheck.Inspect([first, second], null, [])));
    }

    /// <summary>
    /// 脚本说它会自己发键鼠时提醒一句：这条输入不经过互斥门，另一条宏仍可能和它插队。
    /// </summary>
    [Fact]
    public void A_script_that_declares_it_sends_input_is_marked()
    {
        var sending = Step("script.ahk",
            Text("script", "Print(\"hi\")"), Flag("sendsInput", true));

        var problem = Assert.Single(Inspect(sending));
        Assert.Equal(Strings.Get("Editor.Problem.ScriptInput"), problem.Message);

        var quiet = Step("script.ahk",
            Text("script", "Print(\"hi\")"), Flag("sendsInput", false));
        Assert.Empty(Inspect(quiet));
    }

    /// <summary>
    /// 启动那一步必须说清楚句柄写进哪个变量；等待与取消那两步必须说清楚句柄从哪来；
    /// 等全部那一步必须给出一列句柄。
    /// </summary>
    [Fact]
    public void A_background_run_without_a_handle_is_marked()
    {
        var call = Step("control.startMacro", Text("macro", "后台"));
        var wait = Step("control.waitMacro");
        var cancel = Step("control.cancelMacro");
        var waitAll = Step("control.waitAll");

        Assert.Equal(Strings.Get("Editor.Problem.MissingRunHandle"),
            Assert.Single(Against("当前", [Callable("后台")], call)).Message);
        Assert.Equal(Strings.Get("Editor.Problem.MissingRunHandle"),
            Assert.Single(Inspect(wait)).Message);
        Assert.Equal(Strings.Get("Editor.Problem.MissingRunHandle"),
            Assert.Single(Inspect(cancel)).Message);
        Assert.Equal(Strings.Get("Editor.Problem.MissingRunHandles"),
            Assert.Single(Inspect(waitAll)).Message);

        var named = Step("control.startMacro", Text("macro", "后台"),
            Name("runHandleName", "job"));
        Assert.Empty(Against("当前", [Callable("后台")], named));
    }

    /// <summary>
    /// 后台运行的结果要到等待之后才有值：在启动与等待之间读它，编辑期就标出来。
    /// </summary>
    [Fact]
    public void Reading_a_background_result_before_the_wait_is_marked()
    {
        var started = Step("control.startMacro", Text("macro", "后台"),
            Name("runHandleName", "job"), Results("结果"));
        var early = Step("control.setVariable",
            Text("name", "copy"), Text("scope", "local"), Value("value", "$结果"));
        var wait = Step("control.waitMacro", Text("runHandle", "$job"));
        var late = Step("control.setVariable",
            Text("name", "copy"), Text("scope", "local"), Value("value", "$结果"));

        var problems = Against("当前", [Callable("后台", [], ["结果"])], started, early, wait, late);

        var problem = Assert.Single(problems, candidate => candidate.Step == early);
        Assert.Equal(Strings.Format("Editor.Problem.ResultBeforeWait", "结果"), problem.Message);
        Assert.DoesNotContain(problems, candidate => candidate.Step == late);
    }

    [Fact]
    public void A_catch_condition_may_read_the_failure_it_was_given()
    {
        var asked = Step("condition.compare",
            Text("variable", "caught.key"),
            Text("compareOperator", "equals"),
            Value("compareValue", "Run.ElementNotFound"));
        var trying = Step("control.try",
            Steps("body", Pressing()),
            When("catchWhen", asked),
            Steps("catch"),
            Steps("finally"));

        Assert.Empty(Inspect(trying));
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
            When("condition", Step("condition.expression", Value("expression", "true"))),
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

    [Fact]
    public void A_call_to_the_macro_itself_inside_a_branch_or_loop_is_left_alone()
    {
        var branch = Step("control.if",
            When("condition", Step("condition.expression", Value("expression", "true"))),
            Steps("then", Call("自己")));
        var switching = Step("control.switch",
            Text("switchValue", "1"), Text("switchMode", "equals"),
            Steps("cases", Step("control.case", Text("values", "1"),
                Steps("body", Call("自己")))),
            Steps("otherwise"));

        Assert.Empty(Against("自己", [], branch));
        Assert.Empty(Against("自己", [], switching));
        Assert.Empty(Against("自己", [], Repeat(Call("自己"))));
    }

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
    public void A_dollar_that_starts_no_name_is_left_alone()
    {
        // A price is not a variable, and a doubled dollar is how a literal one is written.
        var writer = Step("file.listFiles",
            Text("folder", "."),
            Text("pattern", "costs $5 and $$未知 stays as it is"));

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
    /// A text field names a variable only where <c>$</c> starts a name. A price, an escaped dollar
    /// and a name the macro cannot resolve are three different answers, and the check must not
    /// turn the first two into undefined references.
    /// </summary>
    [Fact]
    public void A_text_field_leaves_prices_and_escaped_dollars_alone()
    {
        var plain = Step("file.listFiles", Text("folder", "."),
            Text("pattern", "$100 off; price $100; $$name"));
        Assert.Empty(Inspect(plain));

        var named = Step("file.listFiles", Text("folder", "."), Text("pattern", "$name"));
        var problem = Assert.Single(Inspect(named));
        Assert.Contains("name", problem.Message);
    }

    /// <summary>A whole value is a value, not a name: a bare word in it is just text.</summary>
    [Fact]
    public void A_bare_word_in_a_whole_value_is_not_a_variable()
        => Assert.Empty(Inspect(Step("file.writeText", Text("path", "out.txt"),
            Value("fileContent", "total"))));

    /// <summary>A field that holds a bare name reads that name, so the check resolves it too.</summary>
    [Fact]
    public void A_bare_name_field_reads_the_variable_it_names()
    {
        var problem = Assert.Single(Inspect(Step("condition.compare",
            Text("variable", "total"), Text("compareOperator", "equals"),
            Value("compareValue", "1"))));
        Assert.Contains("total", problem.Message);
    }

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

    /// <summary>
    /// A wait with no timeout is a legitimate "until it happens". Retrying it, or putting it under
    /// a block with a clock, turns it into a step whose retry or outer budget can never shorten
    /// the wait, so the editor says so before the run hangs.
    /// </summary>
    [Fact]
    public void A_wait_without_a_timeout_is_reported_when_a_retry_or_outer_clock_relies_on_it()
    {
        var retried = Step("vision.waitImage");
        retried.Meta = new StepMeta { RetryCount = 1 };
        var problem = Assert.Single(Inspect(retried));
        Assert.Same(retried, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.WaitWithoutTimeout"), problem.Message);

        var waiting = Step("vision.waitImage");
        var bounded = Step("control.repeat", Text("times", "1"), Steps("body", waiting));
        bounded.Meta = new StepMeta { TimeoutMs = 1_000 };
        Assert.Single(Inspect(bounded));

        // Neither answer on its own is a reason to mark the step: waiting for ever is legal, and
        // a timeout of its own is exactly what the check asks for.
        Assert.Empty(Inspect(Step("vision.waitImage")));
        var timed = Step("vision.waitImage");
        timed.Meta = new StepMeta { RetryCount = 1, TimeoutMs = 1_000 };
        Assert.Empty(Inspect(timed));
    }

    /// <summary>
    /// The list of waiting actions is the engine's own: every action the catalogue says waits is
    /// judged, and an action that does not wait is never asked for a timeout.
    /// </summary>
    [Fact]
    public void Every_waiting_action_is_judged_from_the_catalogue()
    {
        Ui.Run(() =>
        {
            foreach (var action in ActionCatalog.Definitions.Where(action => action.Waits))
            {
                var step = Step(action.Key);
                step.Meta = new StepMeta { RetryCount = 1 };
                Assert.Contains(MacroCheck.Inspect([step], null, []),
                    problem => problem.Step == step
                        && problem.Message == Strings.Get("Editor.Problem.WaitWithoutTimeout"));
            }

            var pressing = Pressing();
            pressing.Meta = new StepMeta { RetryCount = 1 };
            Assert.Empty(MacroCheck.Inspect([pressing], null, []));
        });
    }

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

        var label = Ui.Run(() => ActionCatalog.Find("window.move")!.Parameters
            .First(parameter => parameter.Name == "rect").LocalLabel);
        Assert.Equal(Strings.Format("Editor.Problem.NoPlace", label), problem.Message);

        // A record named in the field is a whole answer as far as the steps alone can tell.
        var writes = Step("control.setVariable", Text("name", "spot"), Text("value", "1"));
        Assert.Empty(Inspect(writes, Step("window.move", Text("rect", "$spot"))));

        // So is a drag that starts at a record and ends at numbers.
        Assert.Empty(Inspect(writes, Step("input.mouseDrag",
            Text("startPlace", "$spot"), Text("endX", "10"), Text("endY", "10"))));
    }

    /// <summary>
    /// What a name holds is known from the step that made it, so a record in a number field, a line
    /// of text where a record is needed and a record written into text are all said before the run.
    /// </summary>
    [Fact]
    public void A_value_of_the_wrong_shape_is_reported()
    {
        var find = Step("vision.findImage", Text("images", ""), Results("match"));
        var number = Step("input.mouseMove", Text("x", "$match"), Text("y", "10"));
        var text = Step("file.writeText", Text("path", "$match"), Text("text", "x"));
        var place = Step("input.mouseMove", Text("place", "$sys.date"), Text("y", "10"));

        var problems = Inspect(find, number, text, place);

        var (xLabel, placeLabel, messageLabel) = Ui.Run(() =>
        {
            var move = ActionCatalog.Find("input.mouseMove")!;
            var log = ActionCatalog.Find("file.writeText")!;
            return (move.Parameters.First(parameter => parameter.Name == "x").LocalLabel,
                move.Parameters.First(parameter => parameter.Name == "place").LocalLabel,
                log.Parameters.First(parameter => parameter.Name == "path").LocalLabel);
        });

        Assert.Contains(problems, problem => problem.Step == number
            && problem.Message == Strings.Format("Editor.Problem.RecordWhereNumber", xLabel));
        Assert.Contains(problems, problem => problem.Step == text
            && problem.Message == Strings.Format("Editor.Problem.RecordInText", "match",
                messageLabel));
        Assert.Contains(problems, problem => problem.Step == place
            && problem.Message == Strings.Format("Editor.Problem.NotARecord", placeLabel,
                "sys.date"));
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
            Text("targetWindow", "$window"));

        var problems = Inspect(info, press);
        Assert.DoesNotContain(problems, problem => problem.Step == press);
    }

    /// <summary>
    /// A call hands its values to what the called macro declared. A value whose kind is known and
    /// does not fit is worth saying at the call site rather than at the point the called macro
    /// reads it.
    /// </summary>
    [Fact]
    public void A_call_argument_of_the_wrong_kind_is_reported()
    {
        var target = new MacroCallable("整理",
            [new MacroParameter { Name = "窗口", Type = ActionReturnType.Record }], []);
        var call = Step("control.runMacro", Text("macro", "整理"),
            Text(MacroCallFields.Argument("窗口"), "$sys.date"));

        var problems = Ui.Run(() => MacroCheck.Inspect([call], null, [target]));
        var problem = Assert.Single(problems, candidate => candidate.Step == call);
        Assert.Equal(Strings.Format("Editor.Problem.ArgumentType", "窗口",
            Strings.Get("ActionType.Record")), problem.Message);
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
    /// 调用的宏里发键鼠，也算这个宏在发：调用那一步标「与别的宏互斥发输入」的提示，不必等
    /// 子宏自己的行。
    /// </summary>
    [Fact]
    public void A_call_to_a_macro_that_sends_input_counts_as_input()
    {
        var input = Step("input.keyPress", Text("key", "F5"));
        var child = Callable("输入", null, null, input);
        var call = Step("control.runMacro", Text("macro", "输入"));

        var problems = Ui.Run(() => MacroCheck.Inspect([call], null, [child], inputShared: true));

        var problem = Assert.Single(problems, candidate => candidate.Step == call);
        Assert.Equal(Strings.Get("Editor.Problem.SharedInput"), problem.Message);
    }

    /// <summary>先要按住浏览器那一页，启动一个用浏览器的后台运行会一直等下去，写宏时就标出来。</summary>
    [Fact]
    public void Starting_a_browser_run_inside_a_dialog_is_marked()
    {
        var child = Callable("用页面", null, null,
            Step("browser.click", Text("pageSelector", "#submit")));
        var start = Step("control.startMacro", Text("macro", "用页面"),
            Name("runHandleName", "job"));
        var dialog = Step("browser.dialog", Text("dialogHow", "accept"), Steps("body", start));

        var problems = Ui.Run(() => MacroCheck.Inspect([dialog], null, [child]));

        var problem = Assert.Single(problems, candidate => candidate.Step == start);
        Assert.Equal(Strings.Get("Editor.Problem.BrowserRunWhileHeld"), problem.Message);
    }

    /// <summary>A block that holds the browser door is the same answer as a dialog for a start inside it.</summary>
    [Fact]
    public void Starting_a_browser_run_inside_a_held_browser_is_marked()
    {
        var child = Callable("用页面", null, null,
            Step("browser.click", Text("pageSelector", "#submit")));
        var start = Step("control.startMacro", Text("macro", "用页面"),
            Name("runHandleName", "job"));
        var hold = Step("control.holdBrowser", Steps("steps", start));

        var problems = Ui.Run(() => MacroCheck.Inspect([hold], null, [child]));

        var problem = Assert.Single(problems, candidate => candidate.Step == start);
        Assert.Equal(Strings.Get("Editor.Problem.BrowserRunWhileHeld"), problem.Message);
    }

    /// <summary>两个宏互相调用时，沿调用图的分析要停下来，而不是一直跟下去。</summary>
    [Fact]
    public void A_circle_of_calls_does_not_make_the_analysis_run_forever()
    {
        var toB = Step("control.runMacro", Text("macro", "乙"));
        var toA = Step("control.runMacro", Text("macro", "甲"));
        var a = Callable("甲", null, null, toB);
        var b = Callable("乙", null, null, toA);

        var problems = Ui.Run(() => MacroCheck.Inspect([toA], null, [a, b], inputShared: true));

        Assert.DoesNotContain(problems,
            candidate => candidate.Message == Strings.Get("Editor.Problem.SharedInput"));
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
    [InlineData("step.abcd.error")]
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
    public void One_step_can_be_wrong_more_than_once_and_each_reason_is_said()
    {
        var step = Set("结果", "$甲 + $乙");

        var problems = Inspect(step);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Same(step, problem.Step));
    }

    [Fact]
    public void An_action_the_catalogue_does_not_know_is_marked()
    {
        var problem = Assert.Single(Inspect(Step("old.gone", Text("anything", "1"))));

        Assert.Equal(Strings.Get("Editor.Problem.UnknownAction"), problem.Message);
    }

    [Fact]
    public void A_field_the_catalogue_does_not_know_is_marked()
    {
        var problem = Assert.Single(Inspect(Step("control.delay",
            Text("ms", "1000"), Text("wait", "true"))));

        Assert.Equal(Strings.Format("Editor.Problem.UnknownParameter", "wait"), problem.Message);
    }

    [Fact]
    public void A_place_written_twice_is_marked()
    {
        var step = Step("window.move", Text("rect", "1,2,3,4"), Text("width", "800"));

        var problem = Assert.Single(Inspect(step));

        Assert.Equal(Strings.Format("Editor.Problem.PlaceAndNumbers", "Rectangle"), problem.Message);
    }

    [Fact]
    public void Two_results_with_one_name_are_marked()
    {
        var step = Step("file.path", Text("path", "a.txt"), Results("part", "part"));

        var problem = Assert.Single(Inspect(step));

        Assert.Equal(Strings.Format("Editor.Problem.DuplicateResultName", "part"), problem.Message);
    }

    [Theory]
    [InlineData("$x +", true)]
    [InlineData("$x + 1", false)]
    [InlineData("$$5", false)]
    [InlineData("plain text", false)]
    public void A_value_that_reads_as_an_expression_is_parsed_while_it_is_written(
        string value, bool marked)
    {
        var step = Step("control.setVariable", Text("name", "y"), Text("scope", "local"),
            Value("value", value));

        // x is defined first, so the only question left about the value is whether it parses.
        var problems = Inspect(Set("x", "1"), step)
            .Where(problem => ReferenceEquals(problem.Step, step))
            .ToList();

        if (!marked)
        {
            Assert.Empty(problems);
        }
        else
        {
            var before = Strings.Get("Editor.Problem.BadExpression")
                .Replace("{0}", string.Empty, StringComparison.Ordinal);
            Assert.Contains(problems, problem =>
                problem.Message.StartsWith(before, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_required_field_left_empty_is_marked()
    {
        var problem = Assert.Single(Inspect(Step("browser.goTo")));

        Assert.Equal(Strings.Format("Editor.Problem.MissingField", "Address"), problem.Message);
    }

    [Fact]
    public void A_choice_outside_the_catalogue_is_marked()
    {
        var problems = Inspect(Step("file.writeText",
            Text("path", "out.txt"), Text("writeMode", "sideways")));

        Assert.Contains(problems, problem =>
            problem.Message.Contains("sideways", StringComparison.Ordinal));
    }

    [Fact]
    public void A_required_field_that_only_applies_under_some_settings_is_left_alone()
        => Assert.Empty(Inspect(Step("data.hash",
            Text("hashSource", "text"), Text("dataText", "abc"))));
}
