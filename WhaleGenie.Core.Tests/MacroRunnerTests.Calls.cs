using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// Calling another macro: what it is passed, what it returns, and how far it may recurse.
public partial class MacroRunnerTests
{
    [Fact]
    public async Task Calling_another_macro_runs_its_steps()
    {
        var store = Store(("n", 0));
        var library = new Library(
            Takes("双击", ["n"], ["n"], AddOne("n"), AddOne("n")),
            Takes("清空", ["n"], ["n"], Set("n", "0")));

        var (result, _) = await RunAsync(
        [
            Call("双击", Args(("n", "$n")), Returns("n=n")),
            Set("中间", "$n"),
            Call("清空", Args(("n", "$n")), Returns("n=n")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(2, N(store, "中间"));
        Assert.Equal(0, N(store, "n"));
    }

    [Fact]
    public async Task A_called_macro_reads_what_it_is_passed_and_gives_back_what_is_asked_for()
    {
        var store = Store(("我的数", 41));
        var library = new Library(Takes("加一", ["输入"], ["结果"], Set("结果", "$输入 + 1")));

        var (result, _) = await RunAsync(
        [
            Call("加一", Args(("输入", "$我的数")), Returns("结果=结果")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(42, N(store, "结果"));
    }

    [Fact]
    public async Task An_argument_the_call_leaves_out_of_an_optional_one_is_its_default()
    {
        var store = Store();
        var library = new Library(("倍数", new MacroSignature(
            [new MacroArgument("倍数", "5", false)], [new MacroReturn("结果")], []), [Set("结果", "$倍数 * 2")]));

        var (result, _) = await RunAsync(
            [Call("倍数", Returns("结果=结果"))], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(10, N(store, "结果"));
    }

    [Fact]
    public async Task An_argument_field_that_is_present_but_empty_uses_the_default()
    {
        var store = Store();
        var library = new Library(("倍数", new MacroSignature(
            [new MacroArgument("倍数", "5", false)], [new MacroReturn("结果")], []),
            [Set("结果", "$倍数 * 2")]));

        var (result, _) = await RunAsync(
            [Call("倍数", Returns("结果=结果"))],
            variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(10, N(store, "结果"));
    }

    [Fact]
    public async Task A_required_argument_field_that_is_present_but_empty_is_missing()
    {
        var library = new Library(("甲", new MacroSignature(
            [new MacroArgument("次数", "", true)], [new MacroReturn("痕迹")], []),
            [Set("痕迹", "1")]));

        var (result, _) = await RunAsync(
            [Call("甲", Args(("次数", "")))], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.MissingArgument, result.Key);
    }

    [Fact]
    public async Task A_required_argument_with_a_default_is_refused_before_the_call()
    {
        var library = new Library(("甲", new MacroSignature(
            [new MacroArgument("次数", "3", true)], [], []), []));

        var (result, _) = await RunAsync([Call("甲")], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadDeclaration, result.Key);
    }

    [Fact]
    public async Task A_default_of_the_wrong_kind_stops_the_call()
    {
        var library = new Library(("甲", new MacroSignature(
            [new MacroArgument("数量", "不是数", false, ActionReturnType.Number)], [], []), []));

        var (result, _) = await RunAsync([Call("甲")], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadArgumentType, result.Key);
    }

    [Fact]
    public async Task An_empty_value_can_be_given_to_a_number_parameter()
    {
        var store = Store();
        var library = new Library(("甲",
            new MacroSignature([new MacroArgument("数量", "", true, ActionReturnType.Number)],
                [new MacroReturn("痕迹")], []),
            [Set("痕迹", "$数量")]));

        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "甲"), ArgRow("数量", "", true),
                Returns("痕迹=取回"))], variables: store,
            macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["取回"].AsText());
    }

    [Fact]
    public async Task An_argument_the_macro_requires_stops_the_call_when_it_is_left_out()
    {
        var store = Store();
        var library = new Library(Takes("甲", ["次数"], [], Set("痕迹", "1")));

        var (result, _) = await RunAsync([Call("甲")], variables: store, macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MissingArgument", result.Key);
        Assert.Equal("次数", result.Detail);

        // Read before the call went anywhere, so the called macro never started.
        Assert.False(store.TryGet("痕迹", out _));
    }

    [Fact]
    public async Task A_value_passed_to_an_argument_the_macro_does_not_declare_stops_the_call()
    {
        var store = Store();
        var library = new Library(Takes("甲", [], [], Set("痕迹", "1")));

        // The call was written for an older declaration of "甲". The value now has nowhere to go,
        // and saying so beats running with it quietly dropped.
        var (result, _) = await RunAsync(
            [Call("甲", Args(("次数", "3")))], variables: store, macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.UndeclaredArgument", result.Key);
        Assert.Equal("次数", result.Detail);
        Assert.False(store.TryGet("痕迹", out _));
    }

    [Fact]
    public async Task An_argument_with_both_a_value_and_the_empty_marker_stops_the_call()
    {
        var store = Store();
        var library = new Library(Takes("甲", ["次数"], [], Set("痕迹", "$次数")));

        // The field says a value and the marker beside it says none. Which one the author meant is
        // a guess, so the call stops instead of picking.
        var (result, _) = await RunAsync(
            [Call("甲", ArgRow("次数", "3", true))],
            variables: store, macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.GivenWithValue", result.Key);
        Assert.Equal("次数", result.Detail);
        Assert.False(store.TryGet("痕迹", out _));
    }

    [Fact]
    public async Task A_required_argument_can_be_supplied_as_an_empty_value()
    {
        var store = Store();
        var library = new Library(("甲",
            new MacroSignature([new MacroArgument("数量", "", true)], [new MacroReturn("痕迹")], []),
            [Set("痕迹", "$数量")]));

        var (result, _) = await RunAsync(
        [
            Step("control.runMacro", Param("macro", "甲"), ArgRow("数量", "", true),
                Returns("痕迹=取回")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["取回"].AsText());
    }

    [Fact]
    public async Task An_optional_argument_can_be_supplied_as_an_empty_value()
    {
        var store = Store();
        var library = new Library(("甲",
            new MacroSignature([new MacroArgument("数量", "7", false)], [new MacroReturn("痕迹")], []),
            [Set("痕迹", "$数量")]));

        var (result, _) = await RunAsync(
        [
            Step("control.runMacro", Param("macro", "甲"), ArgRow("数量", "", true),
                Returns("痕迹=取回")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["取回"].AsText());
    }

    [Fact]
    public async Task An_argument_of_the_wrong_kind_stops_the_call()
    {
        var store = Store();
        var library = new Library(("甲",
            new MacroSignature([new MacroArgument("数量", "", true, ActionReturnType.Number)],
                [new MacroReturn("结果")], []),
            [Set("结果", "$数量")]));

        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "甲"), Args(("数量", "不是数")))],
            variables: store, macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadArgumentType, result.Key);
    }

    [Fact]
    public async Task A_result_of_the_wrong_kind_stops_the_wait()
    {
        var store = Store();
        var library = new Library(("甲", MacroSignature.Empty with
        {
            Results = [new MacroReturn("结果", ActionReturnType.Number)],
        }, [Set("结果", "不是数")]));

        var (result, _) = await RunAsync([Step("control.runMacro", Param("macro", "甲"),
                Returns("结果=取回"))], variables: store,
            macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadResultType, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["取回"].AsText());
    }

    [Fact]
    public async Task A_call_to_a_macro_with_a_bad_declaration_is_refused()
    {
        var store = Store();
        var library = new Library(("甲",
            new MacroSignature([new MacroArgument("同名", "", false),
                new MacroArgument("同名", "", false)], [], []),
            [Set("痕迹", "1")]));

        var (result, _) = await RunAsync([Call("甲")], variables: store, macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadDeclaration, result.Key);
        Assert.False(store.TryGet("痕迹", out _));
    }

    [Fact]
    public async Task A_result_the_macro_never_offered_stops_the_call_before_it_runs()
    {
        var store = Store();
        var library = new Library(Takes("甲", [], ["结果"], Set("结果", "1")));

        var (result, _) = await RunAsync(
            [Call("甲", Returns("别的=别的"))], variables: store, macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.UndeclaredResult", result.Key);
        Assert.Equal("别的", result.Detail);

        // The name was settled before the call went anywhere, so nothing of it ran.
        Assert.False(store.TryGet("结果", out _));
        Assert.Equal(string.Empty, store.Local.Values["别的"].AsText());
    }

    [Fact]
    public async Task A_called_macro_cannot_read_the_callers_own_values()
    {
        var library = new Library(Takes("读", [], ["结果"], Set("结果", "$只有调用者有 + 1")));

        var (result, _) = await RunAsync(
        [
            Set("只有调用者有", "7"),
            Call("读", Returns("结果=结果")),
        ], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);
    }

    [Fact]
    public async Task What_a_called_macro_leaves_behind_stays_inside_it()
    {
        var store = Store();
        var library = new Library(Plain("留一手", Set("临时", "9")));

        var (result, _) = await RunAsync(
            [Call("留一手")], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.False(store.TryGet("临时", out _));
    }

    [Fact]
    public async Task A_result_the_called_macro_never_set_comes_back_empty()
    {
        var store = Store();
        var library = new Library(Takes("空手", [], ["答案"], Set("别的", "1")));

        var (result, _) = await RunAsync(
            [Call("空手", Returns("答案=答案"))],
            variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.True(store.TryGet("答案", out var answer));
        Assert.Equal(string.Empty, answer.AsText());
    }

    [Fact]
    public async Task An_argument_keeps_the_kind_of_value_it_was_given()
    {
        var store = Store();
        var library = new Library(Takes("数个数", ["项目"], ["个数"], Set("个数", "count($项目)")));

        var (result, _) = await RunAsync(
        [
            Set("项目", "list(1, 2, 3)"),
            Call("数个数", Args(("项目", "$项目")), Returns("个数=个数")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(3, N(store, "个数"));
    }

    [Fact]
    public async Task A_declared_result_is_empty_until_the_called_macro_writes_it()
    {
        var store = Store();
        var library = new Library(("甲", new MacroSignature([], [new MacroReturn("结果")], []),
            [Set("副本", "$结果")]));

        var (result, _) = await RunAsync(
            [Call("甲", Returns("结果=取回"))], variables: store, macros: library);

        // The result exists as an empty value at the start of the call, so reading it is not an
        // undefined expression; the caller still receives the empty value it was never written to.
        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["取回"].AsText());
    }

    [Fact]
    public async Task An_in_out_name_keeps_the_argument_when_the_macro_does_not_write_it()
    {
        var store = Store(("source", 5));
        var library = new Library((
            "甲",
            new MacroSignature([new MacroArgument("n", string.Empty, false)],
                [new MacroReturn("n")], []),
            []));

        var (result, _) = await RunAsync(
            [Call("甲", Args(("n", "$source")), Returns("n=n"))], variables: store,
            macros: library);

        // The in-out name is the declared one: the argument arrives as n and the same n comes back.
        // The call site uses another variable as the source because the result variable is cleared
        // at the start of the attempt, before the call reads its arguments.
        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(5, N(store, "n"));
        Assert.Equal(5, N(store, "source"));
    }

    [Fact]
    public async Task A_direct_run_reads_the_parameters_it_is_given()
    {
        var store = Store();
        var signature = new MacroSignature(
            [new MacroArgument("数量", string.Empty, true)], [], []);

        var (result, _) = await RunAsync(
            [Set("结果", "$数量 * 2")], variables: store, startSignature: signature,
            startArguments: [new MacroStartArgument("数量", "5", Given: false)]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(10, N(store, "结果"));
    }

    [Fact]
    public async Task A_direct_run_uses_an_optional_default()
    {
        var store = Store();
        var signature = new MacroSignature(
            [new MacroArgument("数量", "5", false)], [], []);

        var (result, _) = await RunAsync(
            [Set("结果", "$数量 * 2")], variables: store, startSignature: signature);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(10, N(store, "结果"));
    }

    [Fact]
    public async Task A_direct_run_without_a_required_value_stops_before_the_steps()
    {
        var store = Store();
        var signature = new MacroSignature(
            [new MacroArgument("数量", string.Empty, true)], [], []);

        var (result, _) = await RunAsync(
            [Set("痕迹", "1")], variables: store, startSignature: signature);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.MissingArgument, result.Key);
        Assert.Equal(0, result.Steps);
        Assert.False(store.TryGet("痕迹", out _));
    }

    [Fact]
    public async Task A_direct_run_can_give_an_explicit_empty_value()
    {
        var store = Store();
        var signature = new MacroSignature(
            [new MacroArgument("数量", "5", false)], [], []);

        var (result, _) = await RunAsync(
            [Set("结果", "$数量")], variables: store, startSignature: signature,
            startArguments: [new MacroStartArgument("数量", string.Empty, Given: true)]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["结果"].AsText());
    }

    [Fact]
    public async Task A_direct_run_starts_with_its_declared_results_empty()
    {
        var store = Store();
        var signature = new MacroSignature([],
            [new MacroReturn("答案")], []);

        var (result, _) = await RunAsync(
            [Set("副本", "$答案")], variables: store, startSignature: signature);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["副本"].AsText());
    }

    [Fact]
    public async Task A_direct_run_with_a_bad_declaration_is_refused()
    {
        var store = Store();
        var signature = new MacroSignature(
            [new MacroArgument("数量", "3", Required: true)], [], []);

        var (result, _) = await RunAsync(
            [Set("痕迹", "1")], variables: store, startSignature: signature);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadDeclaration, result.Key);
        Assert.False(store.TryGet("痕迹", out _));
    }

    [Fact]
    public async Task Calling_a_macro_that_is_not_there_fails_the_step()
    {
        var (result, _) = await RunAsync(
            [Call("不存在")], macros: new Library());

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MacroNotFound", result.Key);
    }

    [Fact]
    public async Task Calling_no_macro_at_all_fails_the_step()
    {
        var (result, _) = await RunAsync(
            [Call("")], macros: new Library(Plain("甲")));

        // The macro a call names is a required field now, so leaving it empty is answered the same
        // way every other required field is rather than as a name that was looked for and missed.
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.MissingField, result.Key);
    }

    [Fact]
    public async Task Macros_that_call_round_in_circles_are_stopped()
    {
        var library = new Library(Plain("循环", Call("循环")));

        var (result, _) = await RunAsync(
            [Call("循环")], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MacroTooDeep", result.Key);
    }

    [Fact]
    public async Task A_stop_inside_a_called_macro_stops_the_whole_run()
    {
        var store = Store();
        var library = new Library(Plain("停下", Step("control.stop", Param("reason", "够了"))));

        var (result, _) = await RunAsync(
        [
            Call("停下"),
            Set("after", "1"),
        ], variables: store, macros: library);

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task A_called_macro_does_not_disturb_the_callers_loop_counter()
    {
        // The called macro loops five times, so if its counter were left behind the caller
        // would carry on with 4 instead of its own round number.
        var store = Store();
        var library = new Library(Plain("子循环",
            Step("control.repeat", Param("times", "5"), Body("body"))));

        var (result, _) = await RunAsync(
        [
            Set("记录", "0"),
            Step("control.repeat", Param("times", "3"), Body("body",
                Call("子循环"),
                Set("记录", "$记录 * 10 + $sys.loopIndex"))),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(12, N(store, "记录"));
    }

    /// <summary>
    /// A macro can say which macro it is — the name a log line or a file name wants — and a macro that
    /// was called says its own name rather than the caller's. What the caller reads is its own name
    /// again the moment the call is over, which is what the two reads around the call check.
    /// </summary>
    [Fact]
    public async Task A_macro_reads_its_own_name_while_it_runs()
    {
        var store = Store();
        var library = new Library(("对账", new MacroSignature([], [new MacroReturn("看到的")], []),
            [Set("看到的", "$sys.macroName")]));

        var (result, _) = await RunAsync(
        [
            Set("外手", "$sys.macroName"),
            Call("对账", Returns("看到的=里手")),
            Set("回来", "$sys.macroName"),
        ], variables: store, macros: library, runName: "外层");

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("对账", store.Local.Values["里手"].AsText());
        Assert.Equal("外层", store.Local.Values["外手"].AsText());
        Assert.Equal("外层", store.Local.Values["回来"].AsText());
    }

    [Fact]
    public async Task A_break_inside_a_called_macro_that_has_no_loop_fails_the_call()
    {
        var store = Store();
        var library = new Library(Plain("半途", Step("control.break"), Set("子", "1")));

        var (result, _) = await RunAsync(
        [
            Set("轮", "0"),
            Step("control.repeat", Param("times", "3"), Body("body",
                Call("半途"),
                Set("轮", "$轮 + 1"))),
        ], variables: store, macros: library);

        // The called macro starts outside every loop, whatever the caller is inside: its break
        // has nothing of its own to leave, and it must not cut the caller's loop short either.
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BreakOutsideLoop", result.Key);
        Assert.False(store.TryGet("子", out _));
        Assert.Equal(0, N(store, "轮"));
    }
}
