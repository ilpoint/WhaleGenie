using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Viktor.Core.Devices;
using Viktor.Core.Execution;
using Viktor.Core.Expressions;
using Viktor.Core.Variables;

namespace Viktor.Core.Tests;

public class MacroRunnerTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };

    private static ExecutableParameter When(string name, ExecutableStep step)
        => new() { Name = name, Condition = step };

    private static ExecutableStep Set(string name, string value)
        => Step("control.setVariable", Param("name", name), Param("scope", "local"), Param("value", value));

    private static ExecutableStep AddOne(string name) => Set(name, "$" + name + " + 1");

    private static VariableStore Store(params (string Name, double Value)[] numbers)
    {
        var store = new VariableStore();
        foreach (var (name, value) in numbers)
        {
            store.Local.SetNumber(name, value);
        }

        return store;
    }

    private static double N(VariableStore variables, string name)
        => variables.Local.Values[name].AsNumber();

    private static async Task<(RunResult Result, SilentRunHost Host)> RunAsync(
        ExecutableStep[] steps, IRunHost? host = null, VariableStore? variables = null,
        IMacroLibrary? macros = null)
    {
        var store = variables ?? Store();
        var silent = host as SilentRunHost ?? new SilentRunHost();
        var result = await new MacroRunner(store, host ?? silent, null, 1, macros).RunAsync(steps);
        return (result, silent);
    }

    /// <summary>Runs the steps at the given speed and reports how long they took.</summary>
    private static async Task<double> TimeAsync(ExecutableStep[] steps, double delayScale)
    {
        var watch = Stopwatch.StartNew();
        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(),
            NullDeviceLayer.Instance, delayScale).RunAsync(steps);
        watch.Stop();

        Assert.True(result.Succeeded, result.Key);
        return watch.Elapsed.TotalMilliseconds;
    }

    [Fact]
    public async Task Steps_run_from_top_to_bottom()
    {
        var store = Store(("n", 0));
        var (result, _) = await RunAsync([AddOne("n"), AddOne("n")], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(2, N(store, "n"));
        Assert.Equal(2, result.Steps);
    }

    [Fact]
    public async Task The_log_records_the_start_and_the_end()
    {
        var (_, host) = await RunAsync([Set("a", "1")]);

        Assert.Equal("Run.Start", host.Entries[0].Key);
        Assert.Equal("Run.Finished", host.Entries[^1].Key);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.Set");
    }

    [Fact]
    public async Task A_sequence_runs_its_children()
    {
        var store = Store(("n", 0));
        var (_, _) = await RunAsync(
            [Step("control.sequence", Body("steps", AddOne("n"), AddOne("n"), AddOne("n")))],
            variables: store);

        Assert.Equal(3, N(store, "n"));
    }

    [Fact]
    public async Task A_variable_can_hold_an_expression_of_other_variables()
    {
        var store = Store(("a", 2), ("b", 3));
        var (_, _) = await RunAsync([Set("total", "$a * $b + 1")], variables: store);

        Assert.Equal(7, N(store, "total"));
    }

    [Fact]
    public async Task Repeat_runs_the_body_the_given_number_of_times()
    {
        var store = Store(("n", 0));
        var (_, _) = await RunAsync(
            [Step("control.repeat", Param("times", "4"), Body("body", AddOne("n")))],
            variables: store);

        Assert.Equal(4, N(store, "n"));
    }

    [Fact]
    public async Task Break_leaves_the_loop()
    {
        var store = Store(("n", 0));
        var body = Body("body",
            AddOne("n"),
            Step("control.if",
                When("condition", Step("condition.compare",
                    Param("variable", "n"), Param("operator", "greaterOrEqual"), Param("value", "2"))),
                Body("then", Step("control.break"))));

        var (_, _) = await RunAsync(
            [Step("control.repeat", Param("times", "10"), body)],
            variables: store);

        Assert.Equal(2, N(store, "n"));
    }

    [Fact]
    public async Task Continue_skips_the_rest_of_the_round()
    {
        var store = Store();
        var body = Body("body",
            Step("control.continue"),
            Set("never", "1"));

        var (_, _) = await RunAsync(
            [Step("control.repeat", Param("times", "3"), body)],
            variables: store);

        Assert.False(store.TryGet("never", out _));
    }

    [Fact]
    public async Task While_stops_when_the_condition_goes_false()
    {
        var store = Store(("n", 0));
        var loop = Step("control.while",
            When("condition", Step("condition.compare",
                Param("variable", "n"), Param("operator", "lessThan"), Param("value", "3"))),
            Param("maxIterations", "50"),
            Body("body", AddOne("n")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(3, N(store, "n"));
    }

    [Fact]
    public async Task While_gives_up_at_its_iteration_cap()
    {
        var store = Store(("n", 0));
        var loop = Step("control.while",
            When("condition", Step("condition.compare",
                Param("variable", "n"), Param("operator", "greaterOrEqual"), Param("value", "0"))),
            Param("maxIterations", "5"),
            Body("body", AddOne("n")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(5, N(store, "n"));
    }

    [Fact]
    public async Task For_each_walks_a_list_and_numbers_the_rounds()
    {
        var store = Store();
        var seen = Body("body", Set("last", "$item"), Set("rounds", "$rounds + 1"));
        store.Local.SetNumber("rounds", 0);

        var loop = Step("control.forEach",
            Param("items", "[10, 20, 30]"),
            Param("itemVariable", "item"),
            seen);

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(3, N(store, "rounds"));
        Assert.Equal(30, N(store, "last"));
        Assert.Equal(2, N(store, "sys.loopIndex"));
    }

    [Fact]
    public async Task For_each_can_walk_a_list_variable_and_reverse_it()
    {
        var store = Store();
        store.Local.Set("names", Value.FromList(
            new[] { Value.FromText("a"), Value.FromText("b") }));

        var loop = Step("control.forEach",
            Param("items", "$names"),
            Param("reverse", "true"),
            Body("body", Set("last", "$item")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal("a", store.Local.Values["last"].AsText());
    }

    [Fact]
    public async Task If_picks_the_matching_branch()
    {
        var store = Store(("n", 5));
        var branch = Step("control.if",
            When("condition", Step("condition.compare",
                Param("variable", "n"), Param("operator", "greaterThan"), Param("value", "3"))),
            Body("then", Set("result", "big")),
            Body("else", Set("result", "small")));

        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("big", store.Local.Values["result"].AsText());
    }

    [Fact]
    public async Task A_logic_group_combines_its_conditions()
    {
        var store = Store(("a", 1), ("b", 2));
        var group = Step("condition.group",
            Param("op", "or"),
            Body("conditions",
                Step("condition.compare", Param("variable", "a"), Param("operator", "greaterThan"), Param("value", "5")),
                Step("condition.compare", Param("variable", "b"), Param("operator", "equals"), Param("value", "2"))));

        var branch = Step("control.if", When("condition", group), Body("then", Set("hit", "yes")));
        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task Comparisons_work_on_text_and_numbers()
    {
        var store = Store();
        store.Local.SetText("name", "Ada");

        var contains = Step("condition.compare",
            Param("variable", "name"), Param("operator", "contains"), Param("value", "ad"));
        var regex = Step("condition.compare",
            Param("variable", "name"), Param("operator", "regexMatch"), Param("value", "^A.*a$"));

        var (_, _) = await RunAsync(
            [
                Step("control.if", When("condition", contains), Body("then", Set("c", "1"))),
                Step("control.if", When("condition", regex), Body("then", Set("r", "1"))),
            ],
            variables: store);

        Assert.Equal("1", store.Local.Values["c"].AsText());
        Assert.Equal("1", store.Local.Values["r"].AsText());
    }

    [Fact]
    public async Task A_disabled_step_is_skipped()
    {
        var store = Store();
        var skipped = new ExecutableStep
        {
            Type = "control.setVariable",
            Parameters = [Param("name", "n"), Param("scope", "local"), Param("value", "1")],
            Meta = StepMeta.Empty.WithEnabled(false),
        };

        var (result, host) = await RunAsync([skipped], variables: store);

        Assert.True(result.Succeeded);
        Assert.False(store.TryGet("n", out _));
        Assert.Contains(host.Entries, entry => entry.Key == "Run.Skipped");
    }

    [Fact]
    public async Task An_action_the_engine_cannot_run_fails_the_macro()
    {
        var (result, host) = await RunAsync([Step("something.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.Unsupported", result.Key);
        Assert.Contains(host.Entries, entry => entry.Level is LogLevel.Error);
    }

    [Fact]
    public async Task An_action_that_needs_a_missing_device_says_so()
    {
        // Input and the screen are real actions now, so without a device layer the failure
        // names what was missing rather than claiming the action is not supported.
        var (result, _) = await RunAsync([Step("input.keyPress", Param("key", "F5"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.NoDevice", result.Key);
        Assert.Equal("the keyboard", result.Detail);
    }

    [Fact]
    public async Task Carry_on_keeps_the_rest_of_the_macro_running()
    {
        var store = Store();
        var broken = new ExecutableStep
        {
            Type = "input.keyPress",
            Parameters = [Param("key", "F5")],
            Meta = new StepMeta { OnError = StepErrorAction.Continue },
        };

        var (result, _) = await RunAsync([broken, Set("after", "1")], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("1", store.Local.Values["after"].AsText());
    }

    [Fact]
    public async Task Next_iteration_starts_the_following_round()
    {
        var store = Store(("rounds", 0));
        var broken = new ExecutableStep
        {
            Type = "input.keyPress",
            Parameters = [Param("key", "F5")],
            Meta = new StepMeta { OnError = StepErrorAction.NextIteration },
        };

        var body = Body("body", AddOne("rounds"), broken, Set("never", "1"));
        var (result, _) = await RunAsync(
            [Step("control.repeat", Param("times", "3"), body)],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(3, N(store, "rounds"));
        Assert.False(store.TryGet("never", out _));
    }

    [Fact]
    public async Task Retries_are_attempted_before_the_failure_rule_applies()
    {
        var store = Store();
        var broken = new ExecutableStep
        {
            Type = "input.keyPress",
            Parameters = [Param("key", "F5")],
            Meta = new StepMeta { RetryCount = 2, RetryDelayMs = 0, OnError = StepErrorAction.Continue },
        };

        var (result, host) = await RunAsync([broken], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(2, host.Entries.Count(entry => entry.Key == "Run.Retry"));
    }

    [Fact]
    public async Task A_failure_can_be_handed_to_the_user()
    {
        var store = Store();
        var host = new AskingHost(StepErrorChoice.Skip, StepErrorChoice.Stop);
        var broken = new ExecutableStep
        {
            Type = "something.unknown",
            Meta = new StepMeta { OnError = StepErrorAction.AskUser, RetryDelayMs = 0 },
        };

        var (result, _) = await RunAsync([broken], host, store);

        Assert.True(result.Succeeded);
        Assert.Equal(1, host.Asked);
        Assert.Equal("something.unknown", host.Questions[0].Step);
        Assert.Equal("Run.Unsupported", host.Questions[0].Reason);
        Assert.Equal("something.unknown", host.Questions[0].Detail);
    }

    [Fact]
    public async Task Choosing_to_retry_runs_the_step_again()
    {
        var host = new AskingHost(StepErrorChoice.Retry, StepErrorChoice.Stop);
        var broken = new ExecutableStep
        {
            Type = "input.keyPress",
            Parameters = [Param("key", "F5")],
            Meta = new StepMeta { OnError = StepErrorAction.AskUser, RetryDelayMs = 0 },
        };

        var (result, _) = await RunAsync([broken], host, Store());

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(2, host.Asked);
    }

    [Fact]
    public async Task A_step_that_outlasts_its_timeout_fails()
    {
        var slow = new ExecutableStep
        {
            Type = "control.delay",
            Parameters = [Param("ms", "30")],
            Meta = new StepMeta { TimeoutMs = 1 },
        };

        var (result, _) = await RunAsync([slow]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.Timeout", result.Key);
    }

    [Fact]
    public async Task Stop_ends_the_macro_where_it_stands()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.stop", Param("reason", "done")), Set("after", "1")],
            variables: store);

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task A_shared_variable_must_exist_before_a_macro_can_write_to_it()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.setVariable", Param("name", "shared"), Param("scope", "global"), Param("value", "1"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.GlobalMustExist", result.Key);

        store.Global.SetNumber("shared", 0);
        var (ok, _) = await RunAsync(
            [Step("control.setVariable", Param("name", "shared"), Param("scope", "global"), Param("value", "7"))],
            variables: store);

        Assert.True(ok.Succeeded);
        Assert.Equal(7, store.Global.Values["shared"].AsNumber());
    }

    [Fact]
    public async Task Lists_can_be_created_and_changed()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [
                Step("control.listCreate", Param("name", "names"), Param("scope", "local"), Param("items", "[\"a\"]")),
                Step("control.listAdd", Param("name", "names"), Param("value", "b")),
                Step("control.listInsert", Param("name", "names"), Param("index", "0"), Param("value", "z")),
                Step("control.listRemoveAt", Param("name", "names"), Param("index", "-1")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("z, a", store.Local.Values["names"].AsText());
    }

    [Fact]
    public async Task A_list_change_through_a_variable_keeps_the_values_in_order()
    {
        var store = Store();
        store.Local.Set("names", Value.FromList(new[] { Value.FromText("a"), Value.FromText("b") }));
        store.Local.SetText("item", "c");

        var (result, _) = await RunAsync(
            [Step("control.listAdd", Param("name", "names"), Param("value", "$item"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("a, b, c", store.Local.Values["names"].AsText());
    }

    [Fact]
    public async Task A_value_written_as_an_expression_has_to_work()
    {
        var store = Store();
        var (result, _) = await RunAsync([Set("a", "$missing + 1")], variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);
    }

    [Fact]
    public async Task A_log_step_writes_what_it_is_told()
    {
        var store = Store(("who", 0));
        store.Local.SetText("who", "Ada");

        var (_, host) = await RunAsync(
            [Step("control.log", Param("message", "hello $who"), Param("level", "warn"))],
            variables: store);

        var line = host.Entries.Last(entry => entry.Key == "Run.Message");
        Assert.Equal(LogLevel.Warn, line.Level);
        Assert.Equal("hello Ada", line.Arguments[0]);
    }

    [Fact]
    public async Task A_condition_that_needs_a_missing_device_fails_the_step()
    {
        var branch = Step("control.if",
            When("condition", Step("condition.uiaExists", Param("selector", "Button[name='Save']"))),
            Body("then", Set("hit", "1")));

        var store = Store();
        var (result, host) = await RunAsync([branch], variables: store);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoDevice", result.Key);
        Assert.False(store.TryGet("hit", out _));
        Assert.Contains(host.Entries, entry => entry.Key == "Run.NoDevice");
    }

    [Fact]
    public async Task A_condition_the_engine_does_not_know_is_reported_and_false()
    {
        var branch = Step("control.if",
            When("condition", Step("condition.fromTheFuture")),
            Body("then", Set("hit", "1")));

        var store = Store();
        var (result, host) = await RunAsync([branch], variables: store);

        Assert.True(result.Succeeded);
        Assert.False(store.TryGet("hit", out _));
        Assert.Contains(host.Entries, entry => entry.Key == "Run.UnknownCondition");
    }

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
        Assert.Contains(host.Entries, entry => entry.Key == "Run.Caught");
        Assert.Contains(host.Entries, entry => entry.Key == "Run.Finally");
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
                Param("errorVariable", "why")),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("yes", store.Local.Values["handled"].AsText());
        Assert.Equal("the keyboard", store.Local.Values["why"].AsText());
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
                Param("errorVariable", "why")),
        ], variables: store);

        Assert.Equal(string.Empty, store.Local.Values["why"].AsText());
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

    [Fact]
    public async Task A_speed_factor_bends_the_waits_without_rewriting_them()
    {
        var wait = Step("control.delay", Param("ms", "250"));

        var asWritten = await TimeAsync([wait], 1);
        var quicker = await TimeAsync([wait], 0.1);
        var slower = await TimeAsync([wait], 2);

        Assert.True(asWritten >= 230, $"×1 should wait about 250ms, took {asWritten:0}ms");
        Assert.True(quicker < 120, $"×0.1 should barely wait, took {quicker:0}ms");
        Assert.True(slower >= 480, $"×2 should wait about 500ms, took {slower:0}ms");

        // The macro keeps its own numbers: the factor is only bent in while it runs.
        Assert.Equal("250", wait.Text("ms"));
    }

    [Fact]
    public async Task A_speed_factor_also_bends_the_pauses_around_a_step()
    {
        var paused = new ExecutableStep
        {
            Type = "control.log",
            Parameters = [Param("message", "hi")],
            Meta = new StepMeta { DelayAfterMs = 250 },
        };

        var plain = await TimeAsync([paused], 1);
        var quicker = await TimeAsync([paused], 0.1);

        Assert.True(plain >= 230, $"the pause should have happened, took {plain:0}ms");
        Assert.True(quicker < 120, $"the pause should have shrunk, took {quicker:0}ms");
    }

    [Fact]
    public void A_speed_factor_outside_the_allowed_range_is_brought_back_in()
    {
        // Nothing a slip of the hand can do should stop a macro dead or drag it out for ever.
        Assert.Equal(10, new MacroRunner(new VariableStore(), delayScale: 500).DelayScale);
        Assert.Equal(0.1, new MacroRunner(new VariableStore(), delayScale: 0).DelayScale);
        Assert.Equal(1, new MacroRunner(new VariableStore(), delayScale: double.NaN).DelayScale);
        Assert.Equal(1, new MacroRunner(new VariableStore()).DelayScale);
    }

    [Fact]
    public async Task Calling_another_macro_runs_its_steps()
    {
        var store = Store(("n", 0));
        var library = new Library(
            ("双击", [AddOne("n"), AddOne("n")]),
            ("清空", [Set("n", "0")]));

        var (result, _) = await RunAsync(
        [
            Step("control.runMacro", Param("macro", "双击")),
            Step("control.runMacro", Param("macro", "清空")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(0, N(store, "n"));
    }

    [Fact]
    public async Task A_called_macro_shares_the_caller_variables()
    {
        var store = Store();
        var library = new Library(("加一", [Set("结果", "$输入 + 1")]));

        var (result, _) = await RunAsync(
        [
            Set("输入", "41"),
            Step("control.runMacro", Param("macro", "加一")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(42, N(store, "结果"));
    }

    [Fact]
    public async Task Calling_a_macro_that_is_not_there_fails_the_step()
    {
        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "不存在"))], macros: new Library());

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MacroNotFound", result.Key);
    }

    [Fact]
    public async Task Calling_no_macro_at_all_fails_the_step()
    {
        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", ""))], macros: new Library(("甲", [])));

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MacroNotFound", result.Key);
    }

    [Fact]
    public async Task Macros_that_call_round_in_circles_are_stopped()
    {
        var library = new Library(("循环", [Step("control.runMacro", Param("macro", "循环"))]));

        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "循环"))], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MacroTooDeep", result.Key);
    }

    [Fact]
    public async Task A_stop_inside_a_called_macro_stops_the_whole_run()
    {
        var store = Store();
        var library = new Library(("停下", [Step("control.stop", Param("reason", "够了"))]));

        var (result, _) = await RunAsync(
        [
            Step("control.runMacro", Param("macro", "停下")),
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
        var library = new Library(("子循环",
            [Step("control.repeat", Param("times", "5"), Body("body"))]));

        var (result, _) = await RunAsync(
        [
            Set("记录", "0"),
            Step("control.repeat", Param("times", "3"), Body("body",
                Step("control.runMacro", Param("macro", "子循环")),
                Set("记录", "$记录 * 10 + $sys.loopIndex"))),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(12, N(store, "记录"));
    }

    [Fact]
    public async Task A_break_with_no_loop_of_its_own_ends_only_the_called_macro()
    {
        var store = Store();
        var library = new Library(("半途", [Step("control.break"), Set("子", "1")]));

        var (result, _) = await RunAsync(
        [
            Set("轮", "0"),
            Step("control.repeat", Param("times", "3"), Body("body",
                Step("control.runMacro", Param("macro", "半途")),
                Set("轮", "$轮 + 1"))),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(3, N(store, "轮"));
        Assert.False(store.TryGet("子", out _));
    }

    /// <summary>A stand-in for the macros of one project, so a call can be tested on its own.</summary>
    private sealed class Library : IMacroLibrary
    {
        private readonly Dictionary<string, IReadOnlyList<ExecutableStep>> _byName =
            new(StringComparer.OrdinalIgnoreCase);

        public Library(params (string Name, ExecutableStep[] Steps)[] macros)
        {
            foreach (var (name, steps) in macros)
            {
                _byName[name] = steps;
            }
        }

        public IReadOnlyList<string> Names => [.. _byName.Keys];

        public IReadOnlyList<ExecutableStep>? Steps(string name)
            => _byName.TryGetValue(name, out var steps) ? steps : null;
    }

    private sealed class WatchingHost : IRunHost
    {
        public System.Collections.Generic.List<ExecutableStep> Seen { get; } = [];

        public void Log(LogEntry entry)
        {
        }

        public Task BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        {
            Seen.Add(step);
            return Task.CompletedTask;
        }

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);
    }

    /// <summary>Answers the questions a failure asks, then refuses to answer more.</summary>
    private sealed class AskingHost(params StepErrorChoice[] answers) : IRunHost
    {
        private int _index;

        public int Asked => _index;

        /// <summary>What the runner said each time it asked the user.</summary>
        public System.Collections.Generic.List<(string Step, string Reason, string Detail)> Questions { get; } = [];

        public void Log(LogEntry entry)
        {
        }

        public Task BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.CompletedTask;

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, CancellationToken token)
        {
            Questions.Add((step, reason, detail));
            var answer = _index < answers.Length ? answers[_index] : StepErrorChoice.Stop;
            _index++;
            return Task.FromResult(answer);
        }
    }
}
