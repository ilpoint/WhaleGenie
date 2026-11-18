using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// Handling a failure where it happens: try, catch, tidy up, and leaving the macro early.
public partial class MacroRunnerTests
{
    [Fact]
    public async Task A_cancelled_run_stops_cleanly()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var runner = new MacroRunner(Store());
        var result = await runner.RunAsync([Set("a", "1")], source.Token);

        Assert.Equal(RunStatus.Stopped, result.Status);
    }

    [Fact]
    public async Task Every_step_is_announced_before_it_runs()
    {
        var host = new WatchingHost();
        await RunAsync([Set("a", "1"), Set("b", "2")], host, Store());

        Assert.Equal(2, host.Seen.Count);
        Assert.All(host.Seen, step => Assert.Equal("control.setVariable", step.Type));
    }

    // ------------------------------------------------------------ try / catch / finally

    private static ExecutableStep Try(params ExecutableParameter[] parameters)
        => Step("control.try", parameters);

    private static ExecutableStep Fails => Step("something.unknown");

    [Fact]
    public async Task An_attempt_that_works_skips_the_catch_and_still_tidies_up()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Set("a", "1")),
                Body("catch", Set("b", "2")),
                Body("finally", Set("c", "3"))),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(1, N(store, "a"));
        Assert.False(store.TryGet("b", out _));
        Assert.Equal(3, N(store, "c"));
    }

    [Fact]
    public async Task A_failed_attempt_runs_the_catch_instead_of_stopping_the_macro()
    {
        var store = Store();
        var (result, host) = await RunAsync(
        [
            Try(
                Body("body", Fails),
                Body("catch", Set("b", "2")),
                Body("finally", Set("c", "3"))),
            Set("after", "4"),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(2, N(store, "b"));
        Assert.Equal(3, N(store, "c"));
        Assert.Equal(4, N(store, "after"));
        Assert.Contains(host.Entries, entry => entry.Key == "Log.Caught");
        Assert.Contains(host.Entries, entry => entry.Key == "Log.Finally");
    }

    [Fact]
    public async Task The_failure_reason_is_written_where_the_catch_can_read_it()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("input.keyPress", Param("key", "F5"))),
                Body("catch", Set("handled", "yes")),
                Returns("error=why", "detail=whyDetail")),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("yes", store.Local.Values["handled"].AsText());
        Assert.Equal("Run.NoDevice", store.Local.Values["why"].AsText());
        Assert.Equal("the keyboard", store.Local.Values["whyDetail"].AsText());
    }

    [Fact]
    public async Task The_failure_detail_is_empty_when_the_failure_gave_none()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("control.fail")),
                Body("catch", Set("handled", "yes")),
                Returns("error=why", "detail=whyDetail")),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("Run.StepFailedByMacro", store.Local.Values["why"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["whyDetail"].AsText());
    }

    [Fact]
    public async Task The_failure_reason_is_emptied_when_nothing_went_wrong()
    {
        var store = Store();
        store.Local.SetText("why", "left over from before");

        var (_, _) = await RunAsync(
        [
            Try(
                Body("body", Set("a", "1")),
                Returns("error=why", "detail=whyDetail")),
        ], variables: store);

        Assert.Equal(string.Empty, store.Local.Values["why"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["whyDetail"].AsText());
    }

    [Fact]
    public async Task A_failure_inside_the_catch_block_still_stops_the_macro()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Fails),
                Body("catch", Fails)),
            Set("after", "1"),
        ], variables: store);

        Assert.False(result.Succeeded);
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task Tidying_up_happens_even_when_the_macro_is_told_to_stop()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("control.stop", Param("reason", "enough"))),
                Body("catch", Set("b", "1")),
                Body("finally", Set("c", "3"))),
        ], variables: store);

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.False(store.TryGet("b", out _));
        Assert.Equal(3, N(store, "c"));
    }

    [Fact]
    public async Task Tidying_up_happens_when_the_attempt_breaks_out_of_a_loop()
    {
        var store = Store(("c", 0));
        var (result, _) = await RunAsync(
        [
            Step("control.repeat", Param("times", "3"), Param("intervalMs", "0"),
                Body("body",
                    Try(
                        Body("body", Step("control.break")),
                        Body("finally", AddOne("c"))))),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(1, N(store, "c"));
    }

    [Fact]
    public async Task A_try_inside_a_try_handles_its_own_failure()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body",
                    Try(
                        Body("body", Fails),
                        Body("catch", Set("inner", "1")))),
                Body("catch", Set("outer", "1"))),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(1, N(store, "inner"));
        Assert.False(store.TryGet("outer", out _));
    }

    [Fact]
    public async Task A_failure_while_tidying_up_wins_over_a_handled_attempt()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Fails),
                Body("catch", Set("b", "1")),
                Body("finally", Fails)),
        ], variables: store);

        Assert.False(result.Succeeded);
        Assert.Equal(1, N(store, "b"));
    }

    /// <summary>
    /// 从外面停下来时，正在跑的那一块仍然收尾一次：用户按的是停止，不是"跳过清理"，锁、文件与
    /// 按住不放的键都要按宏作者写的那样放回去。
    /// </summary>
    [Fact]
    public async Task A_run_stopped_from_outside_still_tidies_up()
    {
        var store = Store();
        using var stop = new CancellationTokenSource();
        var running = RunAsync(
        [
            Try(
                Body("body", Step("control.delay", Param("ms", "5000"))),
                Body("finally", Set("tidied", "1"))),
        ], variables: store, token: stop.Token);

        stop.CancelAfter(100);
        var (result, host) = await running;

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.Equal(1, N(store, "tidied"));
        Assert.Contains(host.Entries, entry => entry.Key == "Log.Finally");
    }

    /// <summary>
    /// 嵌套的收尾按内到外各跑一次，共用同一份停止预算：两个收尾都该在五秒以内跑完，先里后外。
    /// </summary>
    [Fact]
    public async Task Nested_clean_up_runs_from_the_inside_out()
    {
        var store = Store();
        using var stop = new CancellationTokenSource();
        var running = RunAsync(
        [
            Step("control.setVariable", Param("name", "order"), Param("value", Formula.List())),
            Try(
                Body("body",
                    Try(
                        Body("body", Step("control.delay", Param("ms", "5000"))),
                        Body("finally",
                            Step("control.listAdd", Param("name", "order"), Param("value", "in"))))),
                Body("finally",
                    Step("control.listAdd", Param("name", "order"), Param("value", "out")))),
        ], variables: store, token: stop.Token);

        stop.CancelAfter(100);
        var (result, _) = await running;

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.Equal(["in", "out"],
            store.Local.Values["order"].Items.Select(item => item.AsText()));
    }

    /// <summary>
    /// 收尾里的等待不会自己拿到一份新时钟：整次停止只有五秒的清理预算，收尾里的十秒等待在预算到点
    /// 时被取消，停止照旧结束，日志里说清收尾没做完。
    /// </summary>
    [Fact]
    public async Task Clean_up_that_runs_too_long_ends_the_stop_within_five_seconds()
    {
        var store = Store();
        using var stop = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        var running = RunAsync(
        [
            Try(
                Body("body", Step("control.delay", Param("ms", "5000"))),
                Body("finally",
                    Set("started", "1"),
                    Step("control.delay", Param("ms", "10000")),
                    Set("finished", "1"))),
        ], variables: store, token: stop.Token);

        stop.CancelAfter(100);
        var (result, host) = await running;
        watch.Stop();

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.True(watch.Elapsed > TimeSpan.FromSeconds(4), $"ended after {watch.Elapsed}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(9), $"ended after {watch.Elapsed}");
        Assert.Equal(1, N(store, "started"));
        Assert.False(store.TryGet("finished", out _));
        Assert.Contains(host.Entries, entry => entry.Key == "Log.CleanupFailed");
    }

    /// <summary>
    /// 收尾本身失败时不改变这次运行的结局：它仍是"被停止"，失败原因仍是空，失败只写进日志。
    /// </summary>
    [Fact]
    public async Task Clean_up_that_fails_leaves_the_stop_as_it_was()
    {
        var store = Store();
        using var stop = new CancellationTokenSource();
        var running = RunAsync(
        [
            Try(
                Body("body", Step("control.delay", Param("ms", "5000"))),
                Body("finally", Fails)),
        ], variables: store, token: stop.Token);

        stop.CancelAfter(100);
        var (result, host) = await running;

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.Equal(string.Empty, result.Key);
        Assert.Contains(host.Entries, entry => entry.Key == "Log.CleanupFailed");
    }

    [Fact]
    public async Task A_return_ends_the_macro_and_the_caller_carries_on()
    {
        var store = Store();
        var library = new Library(("检查", new MacroSignature([], [new MacroReturn("结果")], []),
            [Set("结果", "early"), Step("control.return"), Set("结果", "late")]));

        var (result, _) = await RunAsync(
        [
            Call("检查", Returns("结果=结果")),
            Set("after", "1"),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("early", store.Local.Values["结果"].AsText());
        Assert.Equal(1, N(store, "after"));
    }

    /// <summary>
    /// 同步调用也受这一步的「超时」约束：被调用的宏比预算久，这一步报 Run.Timeout，而不是
    /// 一直陪着它跑下去。
    /// </summary>
    [Fact]
    public async Task A_synchronous_call_that_outlasts_its_budget_times_out()
    {
        var library = new Library(("慢", MacroSignature.Empty,
            [Step("control.delay", Param("ms", "2000"))]));

        var (result, _) = await RunAsync(
        [
            new ExecutableStep
            {
                Type = "control.runMacro",
                Parameters = [Param("macro", "慢")],
                Meta = new StepMeta { TimeoutMs = 60 },
            },
        ], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.Timeout, result.Key);
    }

    /// <summary>
    /// 同步调用失败时不把半截结果带回来：被调用的宏失败前写下的那一项，调用处读到的是空。
    /// </summary>
    [Fact]
    public async Task A_failed_call_brings_back_no_half_results()
    {
        var store = Store();
        var library = new Library(("坏", new MacroSignature([], [new MacroReturn("结果")], []),
            [Set("结果", "半截"), Step("control.fail", Param("reason", "boom"))]));

        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("control.runMacro", Param("macro", "坏"),
                    Returns("结果=带回"))),
                Body("catch")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["带回"].AsText());
    }

    /// <summary>
    /// 互斥写在子宏里，就由子宏那一段去等：调用处没有别的地方可以把它关掉，一次运行也不会因为
    /// 某一步会发键鼠就自动排队。
    /// </summary>
    [Fact]
    public async Task A_hold_inside_a_called_macro_waits_at_the_door()
    {
        var library = new Library(("输入", MacroSignature.Empty,
        [
            Step("control.holdInput", Body("steps", Step("input.keyPress", Param("key", "F5")))),
        ]));

        var holder = await SharedDoors.Input.Take("another macro", CancellationToken.None);
        Task<RunResult> running;
        try
        {
            running = new MacroRunner(new VariableStore(), new SilentRunHost(), null, 1, library)
                .RunAsync([Step("control.runMacro", Param("macro", "输入"))]);

            await Task.Delay(120);
            Assert.False(running.IsCompleted);
        }
        finally
        {
            holder.Dispose();
        }

        var result = await running;
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.NoDevice, result.Key);
    }

    [Fact]
    public async Task A_return_at_the_top_finishes_the_run()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Set("before", "1"),
            Step("control.return"),
            Set("after", "1"),
        ], variables: store);

        Assert.Equal(RunStatus.Completed, result.Status);
        Assert.Equal(1, N(store, "before"));
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task A_return_leaves_every_enclosing_loop()
    {
        var store = Store(("n", 0));
        var (result, _) = await RunAsync(
        [
            Step("control.repeat", Param("times", "3"),
                Body("body", AddOne("n"), Step("control.return"))),
            Set("after", "1"),
        ], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(1, N(store, "n"));
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task A_return_inside_a_called_macro_only_ends_that_call()
    {
        var store = Store(("n", 0));
        var library = new Library(Plain("子", Step("control.return"), Set("late", "1")));

        var (result, _) = await RunAsync(
        [
            Step("control.repeat", Param("times", "2"),
                Body("body", Call("子"), AddOne("n"))),
        ], variables: store, macros: library);

        // The caller's loop is not cut short by a return that belongs to the macro it called.
        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, N(store, "n"));
    }

    [Fact]
    public async Task A_try_catches_only_the_failure_its_condition_names()
    {
        var store = Store();
        store.Local.SetText("caught.key", "left alone");

        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("control.fail", Param("reason", "boom"))),
                When("catchWhen", Step("condition.compare",
                    Param("variable", "caught.key"),
                    Param("compareOperator", "equals"),
                    Param("compareValue", "Run.StepFailedByMacro"))),
                Body("catch", Set("seen", Formula.Name("caught.key")), Set("detail", Formula.Name("caught.detail"))),
                Body("finally", Set("tidied", "1"))),
            Set("after", "1"),
        ], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("Run.StepFailedByMacro", store.Local.Values["seen"].AsText());
        Assert.Equal("boom", store.Local.Values["detail"].AsText());
        Assert.Equal(1, N(store, "after"));
        Assert.Equal(1, N(store, "tidied"));

        // The names belong to the catch block. What the macro held before it is what it holds
        // again after it, rather than a failure that keeps looking current.
        Assert.Equal("left alone", store.Local.Values["caught.key"].AsText());
        Assert.False(store.TryGet("caught.detail", out _));
    }

    [Fact]
    public async Task A_catch_can_branch_on_a_user_failure_key()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("control.fail", Param("failureKey", "User.NoScreen"),
                    Param("reason", "the screen moved"))),
                When("catchWhen", Step("condition.compare",
                    Param("variable", "caught.key"),
                    Param("compareOperator", "equals"),
                    Param("compareValue", "User.NoScreen"))),
                Body("catch", Set("seen", Formula.Name("caught.key")))),
        ], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("User.NoScreen", store.Local.Values["seen"].AsText());
    }

    [Fact]
    public async Task A_failure_a_catch_condition_does_not_name_keeps_its_key_and_detail()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("control.fail", Param("reason", "boom"))),
                When("catchWhen", Step("condition.compare",
                    Param("variable", "caught.key"),
                    Param("compareOperator", "equals"),
                    Param("compareValue", "Run.NoDevice"))),
                Body("catch", Set("handled", "1")),
                Body("finally", Set("tidied", "1"))),
            Set("after", "1"),
        ], variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.StepFailedByMacro", result.Key);
        Assert.Equal("boom", result.Detail);
        Assert.False(store.TryGet("handled", out _));
        Assert.False(store.TryGet("after", out _));
        Assert.Equal(1, N(store, "tidied"));
    }

    [Fact]
    public async Task A_rethrow_hands_on_the_same_key_and_detail()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Step("control.fail", Param("reason", "boom"))),
                Body("catch", new ExecutableStep { Type = "control.rethrow", Id = "rrrr" },
                    Set("afterRethrow", "1"))),
        ], variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.StepFailedByMacro", result.Key);
        Assert.Equal("boom", result.Detail);
        Assert.False(store.TryGet("afterRethrow", out _));
    }

    [Fact]
    public async Task Nested_catches_see_their_own_failure_and_give_the_outer_one_back()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Fails),
                Body("catch",
                    Set("outer", Formula.Name("caught.key")),
                    Try(
                        Body("body", Step("control.fail", Param("reason", "inner"))),
                        Body("catch", Set("inner", Formula.Name("caught.key")))),
                    Set("outerAgain", Formula.Name("caught.key")))),
        ], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("Run.Unsupported", store.Local.Values["outer"].AsText());
        Assert.Equal("Run.StepFailedByMacro", store.Local.Values["inner"].AsText());
        Assert.Equal("Run.Unsupported", store.Local.Values["outerAgain"].AsText());
    }

    [Fact]
    public async Task A_rethrow_with_no_catch_behind_it_says_so()
    {
        var (result, _) = await RunAsync([Step("control.rethrow")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.NothingToRethrow", result.Key);
    }

    [Fact]
    public async Task A_rethrow_inside_a_called_macro_cannot_reach_the_callers_catch()
    {
        var library = new Library(Plain("子", Step("control.rethrow")));
        var (result, _) = await RunAsync(
        [
            Try(
                Body("body", Fails),
                Body("catch", Call("子"))),
        ], macros: library);

        // The caller has caught Run.Unsupported, but that belongs to the caller. The called
        // macro has no failure of its own to hand on, so the call fails on its own terms.
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.NothingToRethrow", result.Key);
    }
}
