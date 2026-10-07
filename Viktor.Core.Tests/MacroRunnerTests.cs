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

    /// <summary>
    /// A step that fails however it is run: a key press with no device layer behind it reports
    /// that the keyboard is missing. Tests say what the macro makes of a failure with it instead
    /// of leaning on a real machine.
    /// </summary>
    private static ExecutableStep Broken(StepMeta? meta = null)
        => new()
        {
            Type = "input.keyPress",
            Parameters = [Param("key", "F5")],
            Meta = meta ?? StepMeta.Empty,
        };

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

    /// <summary>
    /// The shortest of several runs at the given speed. A busy machine can only ever add time to
    /// a wait, so the shortest run is the one that shows what the macro really asked for: a run
    /// that happened to be scheduled late would otherwise fail an honest pause.
    /// </summary>
    private static async Task<double> QuickestAsync(ExecutableStep[] steps, double delayScale,
        int rounds = 5)
    {
        var shortest = double.MaxValue;
        for (var round = 0; round < rounds; round++)
        {
            shortest = Math.Min(shortest, await TimeAsync(steps, delayScale));
        }

        return shortest;
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
    public async Task Counting_from_one_to_three_runs_three_times()
    {
        var store = Store(("sum", 0), ("i", 0));
        var loop = Step("control.for",
            Param("from", "1"),
            Param("to", "3"),
            Param("intervalMs", "0"),
            Param("variable", "i"),
            Body("body", Set("sum", "$sum + $i")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(6, N(store, "sum"));
        Assert.Equal(3, N(store, "i"));
    }

    [Fact]
    public async Task A_counting_loop_can_go_downwards()
    {
        var store = Store(("rounds", 0));
        var loop = Step("control.for",
            Param("from", "5"),
            Param("to", "1"),
            Param("step", "-1"),
            Body("body", Set("rounds", "$rounds + 1")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(5, N(store, "rounds"));
    }

    [Fact]
    public async Task A_zero_step_takes_its_direction_from_the_numbers()
    {
        var store = Store(("rounds", 0));
        var loop = Step("control.for",
            Param("from", "5"),
            Param("to", "1"),
            Param("step", "0"),
            Body("body", Set("rounds", "$rounds + 1")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(5, N(store, "rounds"));
    }

    [Fact]
    public async Task Break_leaves_a_counting_loop()
    {
        var store = Store(("n", 0));
        var body = Body("body",
            Set("n", "$n + 1"),
            Step("control.if",
                When("condition", Step("condition.compare",
                    Param("variable", "i"), Param("operator", "greaterOrEqual"), Param("value", "2"))),
                Body("then", Step("control.break"))));

        var loop = Step("control.for", Param("from", "1"), Param("to", "10"), body);
        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(2, N(store, "n"));
    }

    [Fact]
    public async Task For_each_can_number_the_rounds_when_asked()
    {
        var store = Store();
        var loop = Step("control.forEach",
            Param("items", "[10, 20, 30]"),
            Param("itemVariable", "item"),
            Param("indexVariable", "round"),
            Body("body", Set("seen", "$round")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(2, N(store, "round"));
        Assert.Equal(2, N(store, "seen"));
    }

    [Fact]
    public async Task For_each_leaves_the_round_variable_alone_when_not_asked_for()
    {
        var store = Store();
        var loop = Step("control.forEach",
            Param("items", "[10, 20]"),
            Param("itemVariable", "item"),
            Body("body", Set("seen", "$item")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(20, N(store, "seen"));
        Assert.False(store.TryGet("round", out _));
    }

    private static ExecutableStep Case(string values, params ExecutableStep[] steps)
        => Step("control.case", Param("values", values), Body("body", steps));

    [Fact]
    public async Task A_switch_runs_the_first_case_that_matches()
    {
        var store = Store();
        store.Local.Set("kind", Value.FromText("b"));
        var branch = Step("control.switch",
            Param("value", "$kind"),
            Param("matchMode", "equals"),
            Body("cases",
                Case("a; b", Set("hit", "one")),
                // This case answers to "b" as well, but the first match has already run.
                Case("b; c", Set("hit", "two"))),
            Body("otherwise", Set("hit", "none")));

        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("one", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_switch_falls_through_to_the_otherwise_steps()
    {
        var store = Store();
        store.Local.Set("kind", Value.FromText("z"));
        var branch = Step("control.switch",
            Param("value", "$kind"),
            Param("matchMode", "equals"),
            Body("cases",
                Case("a; b", Set("hit", "one")),
                Case("c; d", Set("hit", "two"))),
            Body("otherwise", Set("hit", "none")));

        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("none", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_switch_can_compare_the_way_the_step_asks()
    {
        var store = Store();
        store.Local.Set("kind", Value.FromText("Running fast"));
        var branch = Step("control.switch",
            Param("value", "$kind"),
            Param("matchMode", "contains"),
            Body("cases", Case("run", Set("hit", "yes"))));

        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_switch_reads_a_number_against_its_case_values()
    {
        var store = Store(("n", 2));
        var branch = Step("control.switch",
            Param("value", "$n"),
            Param("matchMode", "equals"),
            Body("cases",
                Case("1", Set("hit", "one")),
                Case("2; 3", Set("hit", "two or three"))));

        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("two or three", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_switch_with_no_matching_case_and_no_otherwise_does_nothing()
    {
        var store = Store();
        store.Local.Set("kind", Value.FromText("z"));
        var branch = Step("control.switch",
            Param("value", "$kind"),
            Body("cases", Case("a", Set("hit", "one"))));

        var (result, _) = await RunAsync([branch], variables: store);

        Assert.True(result.Succeeded);
        Assert.False(store.TryGet("hit", out _));
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
    public async Task A_failure_inside_a_block_is_answered_by_the_block()
    {
        var store = Store();
        var loop = new ExecutableStep
        {
            Type = "control.repeat",
            Parameters = [Param("times", "2"), Body("body", Broken())],
            Meta = new StepMeta { OnError = StepErrorAction.Continue },
        };

        var (result, _) = await RunAsync([loop, Set("after", "1")], variables: store);

        // Nothing inside the block said what to do about the failure, so the block's own rule
        // decided: what is left of the block is left out, and the macro carries on after it.
        Assert.True(result.Succeeded);
        Assert.Equal("1", store.Local.Values["after"].AsText());
    }

    [Fact]
    public async Task A_block_that_is_retried_runs_its_steps_again_from_the_top()
    {
        var store = Store(("rounds", 0));
        var loop = new ExecutableStep
        {
            Type = "control.repeat",
            Parameters = [Param("times", "1"), Body("body", AddOne("rounds"), Broken())],
            Meta = new StepMeta
            {
                RetryCount = 2,
                RetryDelayMs = 0,
                OnError = StepErrorAction.Continue,
            },
        };

        var (result, host) = await RunAsync([loop], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(3, N(store, "rounds"));
        Assert.Equal(2, host.Entries.Count(entry => entry.Key == "Run.Retry"));
    }

    [Fact]
    public async Task A_step_that_handles_its_own_failure_never_reaches_the_block()
    {
        var store = Store(("rounds", 0));
        var handled = Broken(new StepMeta { OnError = StepErrorAction.Continue });
        var loop = new ExecutableStep
        {
            Type = "control.repeat",
            Parameters = [Param("times", "1"), Body("body", AddOne("rounds"), handled)],
            Meta = new StepMeta
            {
                RetryCount = 2,
                RetryDelayMs = 0,
                OnError = StepErrorAction.Continue,
            },
        };

        var (result, host) = await RunAsync([loop], variables: store);

        // The step dealt with it itself, so there is nothing left for the block to answer: the
        // block runs once and is never retried.
        Assert.True(result.Succeeded);
        Assert.Equal(1, N(store, "rounds"));
        Assert.DoesNotContain(host.Entries, entry => entry.Key == "Run.Retry");
    }

    [Fact]
    public async Task A_block_can_send_the_run_on_to_the_next_round_of_its_loop()
    {
        var store = Store(("rounds", 0));
        var group = new ExecutableStep
        {
            Type = "control.sequence",
            Parameters = [Body("steps", Broken())],
            Meta = new StepMeta { OnError = StepErrorAction.NextIteration },
        };

        var (result, _) = await RunAsync(
            [Step("control.repeat", Param("times", "3"),
                Body("body", AddOne("rounds"), group, Set("never", "1")))],
            variables: store);

        // The failure is the block's, and the block sits inside a loop, so "another round" is
        // that loop's next round: the rest of this one is left out.
        Assert.True(result.Succeeded);
        Assert.Equal(3, N(store, "rounds"));
        Assert.False(store.TryGet("never", out _));
    }

    [Fact]
    public async Task A_failure_inside_a_block_that_nobody_answers_still_stops_the_run()
    {
        var store = Store();
        var loop = new ExecutableStep
        {
            Type = "control.repeat",
            Parameters = [Param("times", "2"), Body("body", Broken())],
        };

        var (result, _) = await RunAsync([loop, Set("after", "1")], variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.NoDevice", result.Key);
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task Retrying_waits_longer_before_each_attempt_when_the_step_backs_off()
    {
        var broken = new ExecutableStep
        {
            Type = "something.unknown",
            Meta = new StepMeta
            {
                RetryCount = 2,
                RetryDelayMs = 30,
                RetryBackoff = RetryBackoff.Doubling,
                OnError = StepErrorAction.Continue,
            },
        };

        var watch = Stopwatch.StartNew();
        var (result, host) = await RunAsync([broken]);
        watch.Stop();

        Assert.True(result.Succeeded);
        Assert.Equal(2, host.Entries.Count(entry => entry.Key == "Run.Retry"));

        // 30 ms before the first retry and 60 before the second. A run can only ever take longer
        // than it asked for, so this is a floor rather than an exact figure.
        Assert.True(watch.Elapsed.TotalMilliseconds >= 85,
            $"the pauses should grow, took {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task A_failure_can_be_handed_to_the_user()
    {
        var store = Store();
        var host = new AskingHost(StepErrorChoice.Skip, StepErrorChoice.Stop);
        var broken = new ExecutableStep
        {
            Type = "something.unknown",
            Meta = new StepMeta
            {
                OnError = StepErrorAction.AskUser,
                RetryDelayMs = 0,
                Comment = "the note I wrote on this step",
            },
        };

        var (result, _) = await RunAsync([broken], host, store);

        Assert.True(result.Succeeded);
        Assert.Equal(1, host.Asked);
        Assert.Equal("something.unknown", host.Questions[0].Step);
        Assert.Equal("Run.Unsupported", host.Questions[0].Reason);
        Assert.Equal("something.unknown", host.Questions[0].Detail);

        // The note is the user's own words about why the step was there, so it travels with the
        // question: the window that asks has no other way to know it.
        Assert.Equal("the note I wrote on this step", host.Questions[0].Comment);
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
    public async Task A_step_that_runs_past_its_timeout_is_stopped_where_it_stands()
    {
        // The wait asks for five seconds and is allowed a tenth of one. The limit is what ends it,
        // so the run comes back in a moment instead of sitting out the whole delay and being told
        // off afterwards.
        var slow = new ExecutableStep
        {
            Type = "control.delay",
            Parameters = [Param("ms", "5000")],
            Meta = new StepMeta { TimeoutMs = 100 },
        };

        var watch = Stopwatch.StartNew();
        var (result, _) = await RunAsync([slow]);
        watch.Stop();

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.Timeout", result.Key);
        Assert.True(watch.Elapsed.TotalMilliseconds < 2000,
            $"the wait should have been cut short, took {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task A_blocks_timeout_cuts_short_the_wait_inside_it()
    {
        // The step inside is allowed as long as it likes; the block it sits in is not, and that is
        // the limit that ends the wait.
        var block = new ExecutableStep
        {
            Type = "control.sequence",
            Parameters = [Body("steps", Step("control.delay", Param("ms", "5000")))],
            Meta = new StepMeta { TimeoutMs = 100 },
        };

        var watch = Stopwatch.StartNew();
        var (result, _) = await RunAsync([block]);
        watch.Stop();

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.Timeout", result.Key);
        Assert.True(watch.Elapsed.TotalMilliseconds < 2000,
            $"the block should have given up on the step inside it, took {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task Stopping_a_run_is_not_read_as_a_timeout()
    {
        // A step that is allowed a minute still has to end when the user stops the macro: that is a
        // stop, and calling it a timeout would point the reader at the wrong thing.
        var waiting = new ExecutableStep
        {
            Type = "control.delay",
            Parameters = [Param("ms", "30000")],
            Meta = new StepMeta { TimeoutMs = 60000 },
        };

        using var stop = new CancellationTokenSource();
        var run = new MacroRunner(new VariableStore(), new SilentRunHost(), NullDeviceLayer.Instance)
            .RunAsync([waiting], stop.Token);

        await Task.Delay(20);
        await stop.CancelAsync();
        var result = await run;

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.NotEqual("Run.Timeout", result.Key);
    }

    [Fact]
    public async Task A_step_that_fits_inside_its_timeout_is_left_alone()
    {
        // The clock must not be so eager that an honest step fails: this one is allowed plenty.
        var quick = new ExecutableStep
        {
            Type = "control.delay",
            Parameters = [Param("ms", "20")],
            Meta = new StepMeta { TimeoutMs = 5000 },
        };

        var (result, _) = await RunAsync([quick]);

        Assert.True(result.Succeeded, result.Key);
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
        var quicker = await QuickestAsync([wait], 0.1);
        var slower = await TimeAsync([wait], 2);

        Assert.True(asWritten >= 230, $"×1 should wait about 250ms, took {asWritten:0}ms");
        Assert.True(quicker < 200, $"×0.1 should barely wait, took {quicker:0}ms");
        Assert.True(slower >= 480, $"×2 should wait about 500ms, took {slower:0}ms");

        // The macro keeps its own numbers: the factor is only bent in while it runs.
        Assert.Equal("250", wait.Text("ms"));
    }

    [Fact]
    public async Task A_length_of_time_can_be_given_slack_so_it_is_never_the_same_twice()
    {
        // 200ms with 25% of give lands somewhere between 150 and 250, and never on the same
        // number twice: this is what stops a wait from looking like a machine's.
        var varied = Step("control.delay",
            new ExecutableParameter { Name = "ms", Text = "200", Jitter = 0.25m });

        var waits = new List<double>();
        for (var round = 0; round < 5; round++)
        {
            waits.Add(await TimeAsync([varied], 1));
        }

        Assert.All(waits, taken => Assert.InRange(taken, 145, 300));
        Assert.True(waits.Max() - waits.Min() > 10,
            $"every wait came out about the same: {string.Join(", ", waits.Select(w => w.ToString("0")))}");

        // The macro keeps the number it was written with; the give only bends it while it runs.
        Assert.Equal("200", varied.Text("ms"));
    }

    [Fact]
    public async Task The_slack_of_a_time_is_bent_with_it_by_the_speed_factor()
    {
        // The factor bends the whole wait, give included: 250 ±20% at ×0.1 is at most 30ms.
        var varied = Step("control.delay",
            new ExecutableParameter { Name = "ms", Text = "250", Jitter = 0.2m });

        var quickest = await QuickestAsync([varied], 0.1, rounds: 8);

        Assert.True(quickest < 40, $"×0.1 should barely wait, took {quickest:0}ms");
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
        var quicker = await QuickestAsync([paused], 0.1);

        Assert.True(plain >= 230, $"the pause should have happened, took {plain:0}ms");
        Assert.True(quicker < 200, $"the pause should have shrunk, took {quicker:0}ms");
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
            Step("control.runMacro", Param("macro", "双击"), Param("arguments", "n=$n"),
                Param("returns", "n")),
            Set("中间", "$n"),
            Step("control.runMacro", Param("macro", "清空"), Param("arguments", "n=$n"),
                Param("returns", "n")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(2, N(store, "中间"));
        Assert.Equal(0, N(store, "n"));
    }

    [Fact]
    public async Task A_called_macro_reads_what_it_is_passed_and_gives_back_what_is_asked_for()
    {
        var store = Store(("我的数", 41));
        var library = new Library(("加一", [Set("结果", "$输入 + 1")]));

        var (result, _) = await RunAsync(
        [
            Step("control.runMacro", Param("macro", "加一"), Param("arguments", "输入=$我的数"),
                Param("returns", "结果")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(42, N(store, "结果"));
    }

    [Fact]
    public async Task A_called_macro_cannot_read_the_callers_own_values()
    {
        var library = new Library(("读", [Set("结果", "$只有调用者有 + 1")]));

        var (result, _) = await RunAsync(
        [
            Set("只有调用者有", "7"),
            Step("control.runMacro", Param("macro", "读"), Param("returns", "结果")),
        ], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);
    }

    [Fact]
    public async Task What_a_called_macro_leaves_behind_stays_inside_it()
    {
        var store = Store();
        var library = new Library(("留一手", [Set("临时", "9")]));

        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "留一手"))], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.False(store.TryGet("临时", out _));
    }

    [Fact]
    public async Task A_result_the_called_macro_never_set_comes_back_empty()
    {
        var store = Store();
        var library = new Library(("空手", [Set("别的", "1")]));

        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "空手"), Param("returns", "答案"))],
            variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.True(store.TryGet("答案", out var answer));
        Assert.Equal(string.Empty, answer.AsText());
    }

    [Fact]
    public async Task An_argument_keeps_the_kind_of_value_it_was_given()
    {
        var store = Store();
        var library = new Library(("数个数", [Set("个数", "count($项目)")]));

        var (result, _) = await RunAsync(
        [
            Set("项目", "list(1, 2, 3)"),
            Step("control.runMacro", Param("macro", "数个数"), Param("arguments", "项目=$项目"),
                Param("returns", "个数")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(3, N(store, "个数"));
    }

    [Fact]
    public async Task An_argument_line_that_is_not_a_pair_fails_the_step()
    {
        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "甲"), Param("arguments", "这一行没有等号"))],
            macros: new Library(("甲", [])));

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadMacroArgument", result.Key);
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

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);
    }

    /// <summary>Answers the questions a failure asks, then refuses to answer more.</summary>
    private sealed class AskingHost(params StepErrorChoice[] answers) : IRunHost
    {
        private int _index;

        public int Asked => _index;

        /// <summary>What the runner said each time it asked the user.</summary>
        public System.Collections.Generic.List<(string Step, string Reason, string Detail, string Comment)> Questions { get; } = [];

        public void Log(LogEntry entry)
        {
        }

        public Task BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.CompletedTask;

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
        {
            Questions.Add((step, reason, detail, comment));
            var answer = _index < answers.Length ? answers[_index] : StepErrorChoice.Stop;
            _index++;
            return Task.FromResult(answer);
        }
    }
}
