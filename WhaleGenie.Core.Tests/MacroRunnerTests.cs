using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

public class MacroRunnerTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>See <see cref="SampleFiles.Returns"/>: the results a step hands over.</summary>
    private static ExecutableParameter Returns(params string[] results)
        => SampleFiles.Returns(results);

    /// <summary>See <see cref="SampleFiles.Hit"/>: a place, a picture or a program and all its parts.</summary>
    private static ExecutableParameter Hit(string key, string name) => SampleFiles.Hit(key, name);

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
        IMacroLibrary? macros = null, string runName = "", CancellationToken token = default)
    {
        var store = variables ?? Store();
        var silent = host as SilentRunHost ?? new SilentRunHost();
        var result = await new MacroRunner(store, host ?? silent, null, 1, macros)
        {
            RunName = runName,
        }.RunAsync(steps, token);
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

    /// <summary>
    /// Holds a set of waits to the length they were written as. Every round has to reach the short
    /// end — no wait ever comes back before it was asked for — while the far end is read off the
    /// quickest round, because a busy machine can only ever add to a wait: a round that happened to
    /// be held up says nothing about the number the macro was written with.
    /// </summary>
    private static void AssertWaitsAreSpaced(List<double> waits, double shortest, double longest)
    {
        Assert.All(waits, taken => Assert.True(taken >= shortest,
            $"waited {taken:0}ms, before the short end"));
        Assert.True(waits.Min() <= longest,
            $"the quickest of {waits.Count} waits was {waits.Min():0}ms, past the long end");
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

        Assert.Equal("Log.Start", host.Entries[0].Key);
        Assert.Equal("Log.Finished", host.Entries[^1].Key);
        Assert.Contains(host.Entries, entry => entry.Key == "Log.Set");
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
                    Param("variable", "n"), Param("compareOperator", "greaterOrEqual"), Param("compareValue", "2"))),
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
    public async Task A_break_outside_every_loop_fails_the_run()
    {
        var store = Store();
        var (result, host) = await RunAsync(
            [Step("control.break"), Set("after", "1")], variables: store);

        // Leaving the rest of the macro out and calling that "finished" would hide the mistake.
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BreakOutsideLoop", result.Key);
        Assert.False(store.TryGet("after", out _));
        Assert.Contains(host.Entries, entry => entry.Key == "Run.BreakOutsideLoop");
    }

    [Fact]
    public async Task A_continue_outside_every_loop_fails_the_run()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.continue"), Set("after", "1")], variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.ContinueOutsideLoop", result.Key);
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task A_break_inside_a_plain_block_is_still_outside_a_loop()
    {
        var (result, _) = await RunAsync(
            [Step("control.sequence", Body("steps", Step("control.break"), Set("after", "1"))),
                Set("later", "1")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BreakOutsideLoop", result.Key);
    }

    [Fact]
    public async Task A_next_round_rule_outside_a_loop_fails_the_run()
    {
        var broken = new ExecutableStep
        {
            Type = "input.keyPress",
            Parameters = [Param("key", "F5")],
            Meta = new StepMeta { OnError = StepErrorAction.NextIteration },
        };

        var (result, _) = await RunAsync([broken, Set("after", "1")]);

        // There is no round to start, so "carry on as though this step had finished" is wrong.
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.ContinueOutsideLoop", result.Key);
    }

    [Fact]
    public async Task While_stops_when_the_condition_goes_false()
    {
        var store = Store(("n", 0));
        var loop = Step("control.while",
            When("condition", Step("condition.compare",
                Param("variable", "n"), Param("compareOperator", "lessThan"), Param("compareValue", "3"))),
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
                Param("variable", "n"), Param("compareOperator", "greaterOrEqual"), Param("compareValue", "0"))),
            Param("maxIterations", "5"),
            Body("body", AddOne("n")),
            Returns("ended=ended"));

        var (_, host) = await RunAsync([loop], variables: store);

        Assert.Equal(5, N(store, "n"));
        Assert.Equal("limit", store.Local.Values["ended"].AsText());
        Assert.Contains(host.Entries,
            entry => entry.Key == LogKeys.LoopLimit && entry.Level == LogLevel.Warn);
    }

    [Fact]
    public async Task While_says_when_the_condition_ended_it()
    {
        var store = Store(("n", 0));
        var loop = Step("control.while",
            When("condition", Step("condition.compare",
                Param("variable", "n"), Param("compareOperator", "greaterThan"), Param("compareValue", "0"))),
            Param("maxIterations", "5"),
            Body("body", AddOne("n")),
            Returns("ended=ended"));

        var (_, host) = await RunAsync([loop], variables: store);

        Assert.Equal(0, N(store, "n"));
        Assert.Equal("condition", store.Local.Values["ended"].AsText());
        Assert.DoesNotContain(host.Entries, entry => entry.Key == LogKeys.LoopLimit);
    }

    [Theory]
    [InlineData("control.if")]
    [InlineData("control.while")]
    [InlineData("control.waitUntil")]
    public async Task A_condition_block_without_a_condition_fails(string action)
    {
        var (result, _) = await RunAsync([Step(action)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.MissingCondition, result.Key);
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
        Assert.False(store.Local.Contains(VariableStore.LoopIndexName));
    }

    [Fact]
    public async Task An_inner_loop_leaves_the_outer_round_in_place()
    {
        var store = Store(("outerSeen", -1), ("restored", -1));
        var inner = Step("control.forEach",
            Param("items", "[10, 20]"),
            Param("itemVariable", "item"),
            Body("body", Set("innerSeen", "$sys.loopIndex")));
        var outer = Step("control.for",
            Param("rangeFrom", "1"),
            Param("rangeTo", "3"),
            Param("stepSize", "1"),
            Param("intervalMs", "0"),
            Param("name", "i"),
            Body("body",
                Set("outerSeen", "$sys.loopIndex"),
                inner,
                Set("restored", "$sys.loopIndex")));

        var (result, _) = await RunAsync(
            [outer, Set("after", "$sys.loopIndex")], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, N(store, "outerSeen"));
        Assert.Equal(1, N(store, "innerSeen"));
        Assert.Equal(2, N(store, "restored"));
        Assert.Equal(string.Empty, store.Local.Values["after"].AsText());
        Assert.False(store.Local.Contains(VariableStore.LoopIndexName));
    }

    /// <summary>
    /// 两种结束各写各的：走完是 items / count / range，用 break 提前离开是 break。
    /// </summary>
    [Theory]
    [InlineData("control.repeat", "count", false)]
    [InlineData("control.repeat", "break", true)]
    [InlineData("control.forEach", "items", false)]
    [InlineData("control.forEach", "break", true)]
    [InlineData("control.for", "range", false)]
    [InlineData("control.for", "break", true)]
    public async Task A_loop_says_why_it_ended(string action, string ended, bool stopEarly)
    {
        var store = Store();
        var body = stopEarly
            ? Body("body", Step("control.break"))
            : Body("body", Set("ran", "1"));

        var loop = action switch
        {
            "control.repeat" => Step(action, Param("times", "3"), Param("intervalMs", "0"), body,
                Returns("ended=ended")),
            "control.forEach" => Step(action, Param("items", "[1, 2, 3]"),
                Param("itemVariable", "item"), body, Returns("ended=ended")),
            _ => Step(action, Param("rangeFrom", "1"), Param("rangeTo", "3"),
                Param("stepSize", "1"), Param("intervalMs", "0"), Param("name", "i"), body,
                Returns("ended=ended")),
        };

        var (result, _) = await RunAsync([loop], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(ended, store.Local.Values["ended"].AsText());
    }

    /// <summary>用 break 离开「当…时循环」时，结束原因写 break，而不是留空。</summary>
    [Fact]
    public async Task While_says_break_when_it_was_left_early()
    {
        var store = Store(("n", 0));
        var loop = Step("control.while",
            When("condition", Step("condition.compare", Param("variable", "n"),
                Param("compareOperator", "lessThan"), Param("compareValue", "3"))),
            Param("maxIterations", "5"),
            Body("body", Step("control.break")),
            Returns("ended=ended"));

        var (result, _) = await RunAsync([loop], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("break", store.Local.Values["ended"].AsText());
    }

    /// <summary>
    /// 内层循环用同一个变量名也不会把外层的值带走：离开内层之后，外层读到的还是它自己的那一项。
    /// </summary>
    [Fact]
    public async Task An_inner_loop_does_not_keep_the_outer_item()
    {
        var store = Store();
        var inner = Step("control.forEach", Param("items", "[10, 20]"),
            Param("itemVariable", "item"),
            Body("body", Set("innerLast", "$item")));
        var outer = Step("control.forEach", Param("items", "[1, 2]"),
            Param("itemVariable", "item"),
            Body("body", Set("outerBefore", "$item"), inner, Set("outerAfter", "$item")));

        var (result, _) = await RunAsync([outer], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, N(store, "outerBefore"));
        Assert.Equal(2, N(store, "outerAfter"));
        Assert.Equal(20, N(store, "innerLast"));
    }

    /// <summary>
    /// 多分支 case 里的 break 只离开这个多分支：后面的步骤照常执行，外层循环也不受影响。
    /// </summary>
    [Fact]
    public async Task A_break_in_a_case_leaves_the_switch_only()
    {
        var store = Store();
        store.Local.SetText("kind", "b");
        var branch = Step("control.switch",
            Param("switchValue", "$kind"), Param("switchMode", "equals"),
            Body("cases",
                Step("control.case", Param("values", "a"), Body("body", Set("ran", "a"))),
                Step("control.case", Param("values", "b"),
                    Body("body", Set("ran", "b"), Step("control.break"),
                        Set("afterBreak", "no")))),
            Body("otherwise", Set("ran", "other")));

        var (result, _) = await RunAsync([branch, Set("after", "yes")], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("b", store.Local.Values["ran"].AsText());
        Assert.False(store.Local.Contains("afterBreak"));
        Assert.Equal("yes", store.Local.Values["after"].AsText());
    }

    /// <summary>循环里的多分支写 break，循环照常跑下一轮。</summary>
    [Fact]
    public async Task A_break_in_a_case_does_not_end_the_loop_around_it()
    {
        var store = Store(("rounds", 0));
        var loop = Step("control.repeat", Param("times", "2"), Param("intervalMs", "0"),
            Body("body",
                AddOne("rounds"),
                Step("control.switch",
                    Param("switchValue", "x"), Param("switchMode", "equals"),
                    Body("cases", Step("control.case", Param("values", "x"),
                        Body("body", Step("control.break")))))));

        var (result, _) = await RunAsync([loop], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, N(store, "rounds"));
    }

    /// <summary>多分支里的 continue 交给最近的循环：这一轮提前结束，下一轮照跑。</summary>
    [Fact]
    public async Task A_continue_in_a_case_reaches_the_loop_around_it()
    {
        var store = Store(("rounds", 0));
        var loop = Step("control.repeat", Param("times", "2"), Param("intervalMs", "0"),
            Body("body",
                AddOne("rounds"),
                Step("control.switch",
                    Param("switchValue", "x"), Param("switchMode", "equals"),
                    Body("cases", Step("control.case", Param("values", "x"),
                        Body("body", Step("control.continue"))))),
                Set("after", "no")));

        var (result, _) = await RunAsync([loop], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, N(store, "rounds"));
        Assert.False(store.Local.Contains("after"));
    }

    [Fact]
    public async Task For_each_waits_between_rounds()
    {
        var store = Store(("rounds", 0));
        var loop = Step("control.forEach",
            Param("items", "[10, 20]"),
            Param("itemVariable", "item"),
            Param("intervalMs", "10000"),
            Body("body", AddOne("rounds")));

        using var stop = new CancellationTokenSource(500);
        var (result, _) = await RunAsync([loop], variables: store, token: stop.Token);

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.Equal(1, N(store, "rounds"));
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
            Param("rangeFrom", "1"),
            Param("rangeTo", "3"),
            Param("intervalMs", "0"),
            Param("name", "i"),
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
            Param("rangeFrom", "5"),
            Param("rangeTo", "1"),
            Param("stepSize", "-1"),
            Body("body", Set("rounds", "$rounds + 1")));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(5, N(store, "rounds"));
    }

    [Fact]
    public async Task A_zero_step_takes_its_direction_from_the_numbers()
    {
        var store = Store(("rounds", 0));
        var loop = Step("control.for",
            Param("rangeFrom", "5"),
            Param("rangeTo", "1"),
            Param("stepSize", "0"),
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
                    Param("variable", "i"), Param("compareOperator", "greaterOrEqual"), Param("compareValue", "2"))),
                Body("then", Step("control.break"))));

        var loop = Step("control.for", Param("rangeFrom", "1"), Param("rangeTo", "10"), body);
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
            Param("switchValue", "$kind"),
            Param("switchMode", "equals"),
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
            Param("switchValue", "$kind"),
            Param("switchMode", "equals"),
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
            Param("switchValue", "$kind"),
            Param("switchMode", "contains"),
            Body("cases", Case("run", Set("hit", "yes"))));

        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_switch_reads_a_number_against_its_case_values()
    {
        var store = Store(("n", 2));
        var branch = Step("control.switch",
            Param("switchValue", "$n"),
            Param("switchMode", "equals"),
            Body("cases",
                Case("1", Set("hit", "one")),
                Case("2; 3", Set("hit", "two or three"))));

        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("two or three", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_switch_can_answer_to_every_kind_of_comparison()
    {
        var store = Store(("n", 2));
        store.Local.Set("kind", Value.FromText("Running fast"));

        var (result, _) = await RunAsync(
        [
            Step("control.switch", Param("switchValue", "$kind"), Param("switchMode", "endsWith"),
                Body("cases",
                    Case("slow", Set("hit", "slow")),
                    Case("fast", Set("hit", "fast")))),
            Step("control.switch", Param("switchValue", "$kind"), Param("switchMode", "notEquals"),
                Body("cases", Case("Stopped", Set("other", "yes")))),
            Step("control.switch", Param("switchValue", "$n"), Param("switchMode", "greaterThan"),
                Body("cases", Case("1", Set("big", "yes")))),
            Step("control.switch", Param("switchValue", "$n"), Param("switchMode", "lessOrEqual"),
                Body("cases", Case("2", Set("small", "yes")))),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("fast", store.Local.Values["hit"].AsText());
        Assert.Equal("yes", store.Local.Values["other"].AsText());
        Assert.Equal("yes", store.Local.Values["big"].AsText());
        Assert.Equal("yes", store.Local.Values["small"].AsText());
    }

    [Fact]
    public async Task A_switch_puts_text_in_order_as_text_and_numbers_as_numbers()
    {
        var store = Store();
        store.Local.Set("word", Value.FromText("banana"));
        store.Local.SetText("ten", "10");

        var (result, _) = await RunAsync(
        [
            // "b" comes after "a", so a text branch that only knows letters still lines up.
            Step("control.switch", Param("switchValue", "$word"), Param("switchMode", "greaterThan"),
                Body("cases", Case("apple", Set("after", "yes")))),
            // 10 is bigger than 9 as a number, and would have come first read as text.
            Step("control.switch", Param("switchValue", "$ten"), Param("switchMode", "greaterThan"),
                Body("cases", Case("9", Set("bigger", "yes")))),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("yes", store.Local.Values["after"].AsText());
        Assert.Equal("yes", store.Local.Values["bigger"].AsText());
    }

    [Fact]
    public async Task A_switch_with_no_matching_case_and_no_otherwise_does_nothing()
    {
        var store = Store();
        store.Local.Set("kind", Value.FromText("z"));
        var branch = Step("control.switch",
            Param("switchValue", "$kind"),
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
                Param("variable", "n"), Param("compareOperator", "greaterThan"), Param("compareValue", "3"))),
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
                Step("condition.compare", Param("variable", "a"), Param("compareOperator", "greaterThan"), Param("compareValue", "5")),
                Step("condition.compare", Param("variable", "b"), Param("compareOperator", "equals"), Param("compareValue", "2"))));

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
            Param("variable", "name"), Param("compareOperator", "contains"), Param("compareValue", "ad"));
        var regex = Step("condition.compare",
            Param("variable", "name"), Param("compareOperator", "regex"), Param("compareValue", "^A.*a$"));

        var (_, _) = await RunAsync(
            [
                Step("control.if", When("condition", contains), Body("then", Set("c", "1"))),
                Step("control.if", When("condition", regex), Body("then", Set("r", "1"))),
            ],
            variables: store);

        Assert.Equal("1", store.Local.Values["c"].AsText());
        Assert.Equal("1", store.Local.Values["r"].AsText());
    }

    /// <summary>
    /// Case and spacing are two answers, not one: a value with a trailing space can be compared
    /// strictly while the same comparison can still be told to ignore that space.
    /// </summary>
    [Fact]
    public async Task Case_and_spacing_can_be_asked_for_separately()
    {
        var store = Store();
        store.Local.SetText("padded", "a ");
        store.Local.SetText("mixed", "Save");

        ExecutableStep Compare(string name, string variable, string value, params ExecutableParameter[] more)
            => Step("control.if", When("condition", Step("condition.compare",
                [Param("variable", variable), Param("compareOperator", "equals"),
                 Param("compareValue", value), .. more])),
                Body("then", Set(name, "1")));

        var (result, _) = await RunAsync(
            [
                Compare("strict", "padded", "a",
                    Param("caseSensitive", "true"), Param("trim", "false")),
                Compare("trimmed", "padded", "a",
                    Param("caseSensitive", "true"), Param("trim", "true")),
                Step("control.if", When("condition", Step("condition.compare",
                    Param("variable", "mixed"), Param("compareOperator", "contains"),
                    Param("compareValue", "save"))), Body("then", Set("folded", "1"))),
                Step("control.if", When("condition", Step("condition.compare",
                    Param("variable", "mixed"), Param("compareOperator", "contains"),
                    Param("compareValue", "save"), Param("caseSensitive", "true"))),
                    Body("then", Set("exactCase", "1"))),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.False(store.TryGet("strict", out _));
        Assert.Equal("1", store.Local.Values["trimmed"].AsText());
        Assert.Equal("1", store.Local.Values["folded"].AsText());
        Assert.False(store.TryGet("exactCase", out _));
    }

    /// <summary>
    /// A pattern that backtracks without end is stopped by the shared clock, and the step's own
    /// failure rule still has the last word: the run says which pattern was too expensive and then
    /// goes on to the next step.
    /// </summary>
    [Fact]
    public async Task A_pattern_that_never_finishes_becomes_a_failure_the_step_can_carry_on_from()
    {
        var store = Store();
        store.Local.SetText("long", new string('a', 4000) + "!");

        var slow = new ExecutableStep
        {
            Id = "rx01",
            Type = "control.if",
            Parameters =
            [
                When("condition", Step("condition.compare",
                    Param("variable", "long"), Param("compareOperator", "regex"),
                    Param("compareValue", "(a+)+$"))),
                Body("then", Set("never", "1")),
            ],
            Meta = new StepMeta { OnError = StepErrorAction.Continue },
        };

        var (result, _) = await RunAsync([slow, Set("after", "1")], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("1", store.Local.Values["after"].AsText());
        Assert.False(store.TryGet("never", out _));
        Assert.Equal(RunKeys.RegexTimeout, store.Local.Values["step.rx01.error"].AsText());
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
        Assert.Contains(host.Entries, entry => entry.Key == "Log.Skipped");
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
    public async Task A_number_outside_its_declared_range_stops_the_step()
    {
        var (result, _) = await RunAsync([Step("control.repeat", Param("times", "0"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.OutOfRange, result.Key);
        Assert.Equal("times: 0", result.Detail);
    }

    [Fact]
    public async Task A_required_field_with_no_answer_stops_the_step()
    {
        // The list index is required and has no default; an empty cell used to read as zero and
        // rewrite the first item without saying so.
        var (result, _) = await RunAsync(
            [Step("control.listSet", Param("name", "names"), Param("value", "x"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.MissingField, result.Key);
        Assert.Equal("listIndex", result.Detail);
    }

    [Fact]
    public async Task An_empty_required_field_keeps_the_key_its_action_gave_it()
    {
        // A path has had a word of its own since long before the general rule; the rule reads the
        // word from the field rather than replacing it.
        var (result, _) = await RunAsync([Step("file.writeText",
            Param("writeMode", "overwrite"), Param("newline", "none"), Param("encoding", "utf8"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.MissingPath, result.Key);
    }

    [Fact]
    public async Task A_required_field_that_only_applies_under_some_settings_may_stay_empty()
    {
        // The path half of a hash is required only when the source is a file.
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("data.hash", Param("hashSource", "text"), Param("dataText", "abc"),
                Returns("digest"))],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            store.Local.Values["digest"].AsText());
    }

    [Fact]
    public async Task A_choice_outside_the_catalogue_stops_the_step()
    {
        var (result, _) = await RunAsync([Step("control.switch",
            Param("switchValue", "1"), Param("switchMode", "sideways"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadOption, result.Key);
        Assert.Equal("sideways", result.Detail);
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
        Assert.Equal(2, host.Entries.Count(entry => entry.Key == "Log.Retry"));
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
        Assert.Equal(2, host.Entries.Count(entry => entry.Key == "Log.Retry"));
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
        Assert.DoesNotContain(host.Entries, entry => entry.Key == "Log.Retry");
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
        Assert.Equal(2, host.Entries.Count(entry => entry.Key == "Log.Retry"));

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
    public async Task A_run_keeps_its_status_key_apart_from_its_failure_key()
    {
        var (done, _) = await RunAsync([Set("a", "1")]);
        Assert.Equal(string.Empty, done.Key);
        Assert.Equal(StatusKeys.Finished, done.StatusKey);

        var (stopped, _) = await RunAsync([Step("control.stop")]);
        Assert.Equal(string.Empty, stopped.Key);
        Assert.Equal(StatusKeys.Stopped, stopped.StatusKey);

        var (failed, _) = await RunAsync([Step("control.fail", Param("reason", "no"))]);
        Assert.Equal(RunKeys.StepFailedByMacro, failed.Key);
        Assert.Equal(StatusKeys.Aborted, failed.StatusKey);
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

    /// <summary>
    /// Adding to a shared number is one action, so a macro does not have to read the value and
    /// write it back in two steps with a window in between for another run to fit into.
    /// </summary>
    [Fact]
    public async Task Adding_to_a_shared_number_keeps_the_new_value()
    {
        var store = Store();
        store.Global.SetNumber("shared", 10);

        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "2.5"))],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(12.5, store.Global.Values["shared"].AsNumber());
    }

    [Fact]
    public async Task Adding_to_a_name_the_project_does_not_declare_is_refused()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "1"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.GlobalMustExist, result.Key);
    }

    [Fact]
    public async Task Adding_to_a_shared_text_value_says_it_is_not_a_number()
    {
        var store = Store();
        store.Global.SetText("shared", "ten");

        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "1"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.NotANumber, result.Key);
    }

    /// <summary>
    /// Two runs adding at the same time both have their additions counted. Reading the number in
    /// one step and writing the sum in the next would let one of them overwrite the other.
    /// </summary>
    [Fact]
    public async Task Two_runs_adding_to_one_shared_number_lose_nothing()
    {
        var shared = new VariableBag();
        shared.SetNumber("shared", 0);
        var steps = Enumerable.Range(0, 200)
            .Select(_ => Step("control.incrementGlobal", Param("name", "shared"),
                Param("amount", "1")))
            .ToArray();

        var results = await Task.WhenAll(
            new MacroRunner(new VariableStore(shared)).RunAsync(steps),
            new MacroRunner(new VariableStore(shared)).RunAsync(steps));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Key));
        Assert.Equal(400, shared.Values["shared"].AsNumber());
    }

    /// <summary>
    /// 计数加完直接把新值交出来，后续条件或步骤不必再读一次全局——再读一次看到的可能已经是别的运行
    /// 改过的数。
    /// </summary>
    [Fact]
    public async Task Adding_to_a_shared_number_hands_over_the_new_count()
    {
        var store = Store();
        store.Global.SetNumber("shared", 10);

        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "5"),
                Returns("value=now"))],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(15, N(store, "now"));
        Assert.Equal(15, store.Global.Values["shared"].AsNumber());
    }

    /// <summary>
    /// 两条运行同时往同一个全局列表里追加，谁都不丢：一次追加是"读现在的列表、加一项、写回去"，
    /// 这三下在同一把锁里做完，另一条运行不会在中间插进来，也不会把对方的列表盖掉。
    /// </summary>
    [Fact]
    public async Task Two_runs_appending_to_one_shared_list_lose_nothing()
    {
        var shared = new VariableBag();
        shared.SetList("log", []);
        var steps = Enumerable.Range(0, 200)
            .Select(index => Step("control.listAdd", Param("name", "log"),
                Param("value", index.ToString())))
            .ToArray();

        var results = await Task.WhenAll(
            new MacroRunner(new VariableStore(shared)).RunAsync(steps),
            new MacroRunner(new VariableStore(shared)).RunAsync(steps));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Key));
        Assert.Equal(400, shared.Values["log"].Items.Count);
    }

    /// <summary>The same for taking items out: two runs removing from one list still remove 400.</summary>
    [Fact]
    public async Task Two_runs_removing_from_one_shared_list_lose_nothing()
    {
        var shared = new VariableBag();
        shared.SetList("log", Enumerable.Range(0, 400).Select(index => Value.FromNumber(index)));
        var steps = Enumerable.Range(0, 200)
            .Select(_ => Step("control.listRemoveAt", Param("name", "log"),
                Param("listIndex", "0")))
            .ToArray();

        var results = await Task.WhenAll(
            new MacroRunner(new VariableStore(shared)).RunAsync(steps),
            new MacroRunner(new VariableStore(shared)).RunAsync(steps));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Key));
        Assert.Empty(shared.Values["log"].Items);
    }

    /// <summary>改一个没人定义的列表仍报原来的 Run.NotAList，不新建一个。</summary>
    [Fact]
    public async Task Changing_a_list_nobody_defined_still_says_it_is_not_a_list()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.listAdd", Param("name", "nosuch"), Param("value", "a"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.NotAList, result.Key);
    }

    /// <summary>
    /// 改不动的那一次什么都不写：越界是指这一项不存在，不是把列表改坏一半再报错。
    /// </summary>
    [Fact]
    public async Task A_list_change_that_fails_leaves_the_list_as_it_was()
    {
        var store = Store();
        store.Local.SetList("names", [Value.FromText("a")]);

        var (result, _) = await RunAsync(
            [Step("control.listSet", Param("name", "names"), Param("listIndex", "9"),
                Param("value", "b"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.IndexOutOfRange, result.Key);
        Assert.Equal(["a"], store.Local.Values["names"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task A_calculation_works_out_its_expression_and_stores_the_answer()
    {
        var store = Store(("count", 4));
        var (result, _) = await RunAsync(
            [Step("control.calculate",
                Param("name", "total"), Param("scope", "local"),
                Param("value", "$count + 3"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(7, N(store, "total"));
    }

    [Fact]
    public async Task A_calculation_reads_the_arithmetic_written_in_the_expression()
    {
        var store = Store(("n", 10), ("m", 3));
        var (result, _) = await RunAsync(
            [
                Step("control.calculate", Param("name", "minus"), Param("value", "$n - $m")),
                Step("control.calculate", Param("name", "times"), Param("value", "$n * $m")),
                Step("control.calculate", Param("name", "over"), Param("value", "$n / 4")),
                Step("control.calculate", Param("name", "rest"), Param("value", "$n % $m")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(7, N(store, "minus"));
        Assert.Equal(30, N(store, "times"));
        Assert.Equal(2.5, N(store, "over"));
        Assert.Equal(1, N(store, "rest"));
    }

    [Fact]
    public async Task A_calculation_can_put_two_pieces_of_text_together()
    {
        var store = Store();
        store.Local.SetText("name", "Ada");
        var (result, _) = await RunAsync(
            [Step("control.calculate",
                Param("name", "greeting"), Param("value", "\"Hello \" + $name"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello Ada", store.Local.Values["greeting"].AsText());
    }

    [Fact]
    public async Task A_calculation_may_call_functions_from_its_expression()
    {
        var store = Store(("count", 3));
        store.Local.SetText("name", "Ada");
        var (result, _) = await RunAsync(
            [Step("control.calculate",
                Param("name", "line"), Param("value", "concat(upper($name), $count)"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("ADA3", store.Local.Values["line"].AsText());
    }

    [Fact]
    public async Task A_calculation_that_divides_by_zero_fails_the_step()
    {
        var store = Store(("zero", 0));
        var (result, _) = await RunAsync(
            [Step("control.calculate",
                Param("name", "x"), Param("value", "$zero / 0"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);
        Assert.False(store.TryGet("x", out _));
    }

    [Fact]
    public async Task Text_goes_through_Base64_and_comes_back()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [
                Step("data.base64Encode", Param("dataText", "你好, Ada"),
                    Returns("encoded")),
                Step("data.base64Decode", Param("dataText", "$encoded"),
                    Returns("decoded=back")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("你好, Ada", store.Local.Values["back"].AsText());

        // Base64 of UTF-8 bytes, which is what every service on the other end expects.
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("你好, Ada")),
            store.Local.Values["encoded"].AsText());
    }

    [Fact]
    public async Task Text_that_is_not_Base64_fails_the_step()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("data.base64Decode", Param("dataText", "not base64!!"), Returns("decoded=x"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadBase64", result.Key);
        Assert.Equal(string.Empty, store.Local.Values["x"].AsText());
    }

    [Fact]
    public async Task A_hash_is_worked_out_by_the_named_algorithm()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [
                Step("data.hash", Param("hashAlgorithm", "sha256"), Param("dataText", "abc"),
                    Returns("digest")),
                Step("data.hash", Param("hashAlgorithm", "md5"), Param("dataText", "abc"),
                    Returns("digest=md5")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            store.Local.Values["digest"].AsText());
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", store.Local.Values["md5"].AsText());
    }

    [Fact]
    public async Task Lists_can_be_created_and_changed()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [
                Step("control.listCreate", Param("name", "names"), Param("scope", "local"), Param("items", "[\"a\"]")),
                Step("control.listAdd", Param("name", "names"), Param("value", "b")),
                Step("control.listInsert", Param("name", "names"), Param("listIndex", "0"), Param("value", "z")),
                Step("control.listRemoveAt", Param("name", "names"), Param("listIndex", "-1")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("z, a", store.Local.Values["names"].AsText());
    }

    [Fact]
    public async Task One_position_of_a_list_can_be_changed_without_touching_the_others()
    {
        var store = Store();
        store.Local.Set("names", Value.FromList(
            new[] { Value.FromText("a"), Value.FromText("b"), Value.FromText("c") }));

        var (result, _) = await RunAsync(
            [
                Step("control.listSet", Param("name", "names"), Param("listIndex", "1"),
                    Param("value", "B")),
                Step("control.listSet", Param("name", "names"), Param("listIndex", "-1"),
                    Param("value", "C")),
            ],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("a, B, C", store.Local.Values["names"].AsText());
    }

    [Fact]
    public async Task Changing_a_position_the_list_has_not_got_stops_the_step()
    {
        // Quietly doing nothing would leave the file the macro is about to write unchanged, and
        // nothing in the log would say why.
        var store = Store();
        store.Local.Set("names", Value.FromList(new[] { Value.FromText("a") }));

        var (result, _) = await RunAsync(
            [Step("control.listSet", Param("name", "names"), Param("listIndex", "4"),
                Param("value", "z"))],
            variables: store);

        Assert.Equal("Run.IndexOutOfRange", result.Key);
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

    /// <summary>
    /// Emptying a list is not the same as dropping the variable: a macro that clears a list and
    /// then adds to it again would otherwise be adding to nothing.
    /// </summary>
    [Fact]
    public async Task Clearing_a_list_empties_it_and_leaves_the_variable_in_place()
    {
        var store = Store();
        store.Local.Set("names", Value.FromList(new[] { Value.FromText("a"), Value.FromText("b") }));

        var (result, _) = await RunAsync(
            [Step("control.listClear", Param("name", "names"))], variables: store);

        Assert.True(result.Succeeded);
        Assert.True(store.Local.Values["names"].IsList);
        Assert.Empty(store.Local.Values["names"].Items);
        Assert.Equal(string.Empty, store.Local.Values["names"].AsText());
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
    public async Task Text_shaped_like_a_selector_or_a_path_stays_text()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Set("selector", "[data-testid=\"row-2\"]"),
            Set("nth", "div > p:nth-of-type(2)"),
            Set("path", @"C:\Program Files (x86)\a.log"),
            Set("bracketed", "Meeting (2) notes"),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("[data-testid=\"row-2\"]", store.Local.Values["selector"].AsText());
        Assert.Equal("div > p:nth-of-type(2)", store.Local.Values["nth"].AsText());
        Assert.Equal(@"C:\Program Files (x86)\a.log", store.Local.Values["path"].AsText());
        Assert.Equal("Meeting (2) notes", store.Local.Values["bracketed"].AsText());
    }

    [Fact]
    public async Task A_log_step_writes_what_it_is_told()
    {
        var store = Store(("who", 0));
        store.Local.SetText("who", "Ada");

        var (_, host) = await RunAsync(
            [Step("control.log", Param("message", "hello $who"), Param("level", "warn"))],
            variables: store);

        var line = host.Entries.Last(entry => entry.Key == "Log.Message");
        Assert.Equal(LogLevel.Warn, line.Level);
        Assert.Equal("hello Ada", line.Arguments[0]);
    }

    [Fact]
    public async Task A_text_field_works_out_a_braced_expression()
    {
        var store = Store(("n", 3));

        var (_, host) = await RunAsync(
            [Step("control.log", Param("message", "n is ${$n + 1}"), Param("level", "info"))],
            variables: store);

        var line = host.Entries.Last(entry => entry.Key == "Log.Message");
        Assert.Equal("n is 4", line.Arguments[0]);
    }

    [Fact]
    public async Task A_broken_braced_expression_fails_the_step()
    {
        var (result, _) = await RunAsync(
            [Step("control.log", Param("message", "${1 +}"), Param("level", "info"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadExpression, result.Key);
    }

    [Fact]
    public async Task A_condition_that_needs_a_missing_device_fails_the_step()
    {
        var branch = Step("control.if",
            When("condition", Step("condition.uiaExists", Param("uiSelector", "Button[name='Save']"))),
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
        Assert.Contains(host.Entries, entry => entry.Key == "Log.UnknownCondition");
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
            Step("control.listCreate", Param("name", "order"), Param("items", "")),
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
        var library = new Library(("检查", new MacroSignature([], [new MacroReturn("结果")]),
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
    /// 不等的那次调用立刻交回句柄，后台宏在另一条运行里等着；父宏先把共享变量改成它等的值，
    /// 再按句柄等它结束并取回结果。若调用把父宏也拖住，这一步永远等不到那个值。
    /// </summary>
    [Fact]
    public async Task A_background_run_returns_a_handle_and_the_parent_carries_on()
    {
        var store = Store();
        store.Global.SetText("go", "false");
        var library = new Library(
            ("后台", new MacroSignature([], [new MacroReturn("结果")]),
            [
                Step("control.waitUntil", When("condition", Step("condition.compare",
                    Param("variable", "go"), Param("compareOperator", "equals"),
                    Param("compareValue", "true")))),
                Set("结果", "完成"),
                Step("control.setVariable",
                    Param("name", "inner"), Param("scope", "local"), Param("value", "只在里面")),
                Step("control.setVariable",
                    Param("name", "sharedSeen"), Param("scope", "global"),
                    Param("value", "看得见")),
            ]));
        store.Global.SetText("sharedSeen", "还没写");

        var running = RunAsync(
        [
            Step("control.startMacro", Param("macro", "后台"),
                Param("runHandleName", "job"), Returns("结果=结果")),
            Step("control.setVariable",
                Param("name", "go"), Param("scope", "global"), Param("value", "true")),
            Step("control.waitMacro", Param("runHandle", "$job")),
        ], variables: store, macros: library);

        var finished = await Task.WhenAny(running, Task.Delay(5000));
        Assert.Same(running, finished);

        var (result, _) = await running;
        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("完成", store.Local.Values["结果"].AsText());
        Assert.Equal("看得见", store.Global.Values["sharedSeen"].AsText());

        // 子宏的局部变量不回到调用者这里。
        Assert.False(store.Local.Contains("inner"));
    }

    [Fact]
    public async Task A_wait_that_runs_out_leaves_the_background_run_going()
    {
        var store = Store();
        var library = new Library(("慢", new MacroSignature([], [new MacroReturn("结果")]),
            [
                Step("control.delay", Param("ms", "250")),
                Set("结果", "晚了"),
            ]));

        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "慢"),
                Param("runHandleName", "job"), Returns("结果=结果")),
            new ExecutableStep
            {
                Type = "control.waitMacro",
                Parameters = [Param("runHandle", "$job")],
                Meta = new StepMeta { TimeoutMs = 40, OnError = StepErrorAction.Continue },
            },
            Step("control.waitMacro", Param("runHandle", "$job")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("晚了", store.Local.Values["结果"].AsText());
    }

    [Fact]
    public async Task A_background_run_that_fails_hands_its_key_to_the_wait()
    {
        var library = new Library(("坏", MacroSignature.Empty,
            [Step("control.fail", Param("reason", "boom"))]));

        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "坏"),
                Param("runHandleName", "job")),
            Step("control.waitMacro", Param("runHandle", "$job")),
        ], macros: library);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.StepFailedByMacro, result.Key);
        Assert.Equal("boom", result.Detail);
    }

    /// <summary>
    /// 父宏停下时，没等的后台运行也停；取消之后再过一会儿，它写的值不该出现。
    /// </summary>
    [Fact]
    public async Task Stopping_the_parent_stops_the_background_run()
    {
        var store = Store();
        var library = new Library(("长", MacroSignature.Empty,
            [
                Step("control.delay", Param("ms", "5000")),
                Step("control.setVariable",
                    Param("name", "finished"), Param("scope", "global"),
                    Param("value", "1")),
            ]));

        using var cancel = new CancellationTokenSource();
        var running = RunAsync(
        [
            Step("control.startMacro", Param("macro", "长"),
                Param("runHandleName", "job")),
            Step("control.delay", Param("ms", "5000")),
        ], variables: store, macros: library, token: cancel.Token);
        cancel.CancelAfter(100);
        var (result, _) = await running;

        Assert.Equal(RunStatus.Stopped, result.Status);

        await Task.Delay(400);
        Assert.False(store.Global.Contains("finished"));
    }

    [Fact]
    public async Task A_background_call_without_a_handle_is_refused()
    {
        var library = new Library(("后台", MacroSignature.Empty, []));
        var (result, _) = await RunAsync(
            [Step("control.startMacro", Param("macro", "后台"))],
            macros: library);

        Assert.Equal(RunKeys.MissingHandle, result.Key);
    }

    [Fact]
    public async Task A_handle_that_was_never_started_is_refused()
    {
        var (result, _) = await RunAsync(
            [Step("control.waitMacro", Param("runHandle", "run-nothere"))]);

        Assert.Equal(RunKeys.NoSuchRun, result.Key);
    }

    [Fact]
    public async Task A_handle_can_only_be_waited_for_once()
    {
        var library = new Library(("快", MacroSignature.Empty, []));
        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "快"),
                Param("runHandleName", "job")),
            Step("control.waitMacro", Param("runHandle", "$job")),
            Step("control.waitMacro", Param("runHandle", "$job")),
        ], macros: library);

        Assert.Equal(RunKeys.NoSuchRun, result.Key);
    }

    /// <summary>
    /// 句柄是一条记录，说清是哪一次运行、跑的是哪个宏、现在什么状态、会交回哪些名字：父宏读
    /// field($job, "…") 就能知道，不必等日志或猜全局变量。
    /// </summary>
    [Fact]
    public async Task A_run_handle_says_which_run_it_is_and_what_is_still_owed()
    {
        var store = Store();
        var library = new Library(("慢", new MacroSignature([], [new MacroReturn("结果")]),
            [Step("control.delay", Param("ms", "250")), Set("结果", "好")]));

        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "慢"),
                Param("runHandleName", "job"), Returns("结果=结果")),
            Step("control.setVariable", Param("name", "id"), Param("scope", "local"),
                Param("value", "field($job, \"id\")")),
            Step("control.setVariable", Param("name", "macro"), Param("scope", "local"),
                Param("value", "field($job, \"macro\")")),
            Step("control.setVariable", Param("name", "status"), Param("scope", "local"),
                Param("value", "field($job, \"status\")")),
            Step("control.setVariable", Param("name", "names"), Param("scope", "local"),
                Param("value", "field($job, \"results\")")),
            Step("control.waitMacro", Param("runHandle", "$job")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.StartsWith("run-", store.Local.Values["id"].AsText());
        Assert.Equal("慢", store.Local.Values["macro"].AsText());
        Assert.Equal("running", store.Local.Values["status"].AsText());
        Assert.Equal(["结果"],
            store.Local.Values["names"].AsList().Select(item => item.AsText()));
        Assert.Equal("好", store.Local.Values["结果"].AsText());
    }

    /// <summary>等待之后句柄改写成它结束时的样子：状态、失败键与结果名都留在里面。</summary>
    [Fact]
    public async Task A_waited_handle_says_how_the_run_ended()
    {
        var store = Store();
        var library = new Library(("快", MacroSignature.Empty, []));

        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "快"), Param("runHandleName", "job")),
            Step("control.waitMacro", Param("runHandle", "$job")),
            Step("control.setVariable", Param("name", "status"), Param("scope", "local"),
                Param("value", "field($job, \"status\")")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("done", store.Local.Values["status"].AsText());
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

    /// <summary>取消一次后台运行：它停下，之后的步骤不再写，句柄说清它已经停下。</summary>
    [Fact]
    public async Task Cancelling_a_background_run_stops_it_and_says_so()
    {
        var store = Store();
        var library = new Library(("长", MacroSignature.Empty,
            [
                Step("control.delay", Param("ms", "5000")),
                Step("control.setVariable", Param("name", "finished"), Param("scope", "global"),
                    Param("value", "1")),
            ]));

        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "长"), Param("runHandleName", "job")),
            Step("control.cancelMacro", Param("runHandle", "$job"), Returns("stopped=stopped")),
            Step("control.setVariable", Param("name", "status"), Param("scope", "local"),
                Param("value", "field($job, \"status\")")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(1, N(store, "stopped"));
        Assert.Equal("stopped", store.Local.Values["status"].AsText());
        Assert.False(store.Global.Contains("finished"));
    }

    /// <summary>
    /// 一次等齐多次后台运行：每个启动时勾的名字都写回，count 说等到几个；同一句柄在列表里
    /// 出现两次只算一次。
    /// </summary>
    [Fact]
    public async Task Waiting_for_all_background_runs_takes_back_every_result()
    {
        var store = Store();
        var library = new Library(
            ("一", new MacroSignature([], [new MacroReturn("值")]),
                [Step("control.delay", Param("ms", "40")), Set("值", "one")]),
            ("二", new MacroSignature([], [new MacroReturn("值")]),
                [Step("control.delay", Param("ms", "40")), Set("值", "two")]));

        var (result, _) = await RunAsync(
        [
            Step("control.listCreate", Param("name", "jobs"), Param("scope", "local"),
                Param("items", "")),
            Step("control.startMacro", Param("macro", "一"), Param("runHandleName", "jobOne"),
                Returns("值=first")),
            Step("control.listAdd", Param("name", "jobs"), Param("value", "$jobOne")),
            Step("control.startMacro", Param("macro", "二"), Param("runHandleName", "jobTwo"),
                Returns("值=second")),
            Step("control.listAdd", Param("name", "jobs"), Param("value", "$jobTwo")),
            Step("control.listAdd", Param("name", "jobs"), Param("value", "$jobTwo")),
            Step("control.waitAll", Param("runHandles", "$jobs"), Returns("count=waited")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, N(store, "waited"));
        Assert.Equal("one", store.Local.Values["first"].AsText());
        Assert.Equal("two", store.Local.Values["second"].AsText());
    }

    /// <summary>
    /// 后台运行失败时，失败键留在句柄里：等待那一步报同一个失败键，捕获之后读句柄也看得见。
    /// </summary>
    [Fact]
    public async Task A_failed_background_run_leaves_its_key_in_the_handle()
    {
        var store = Store();
        var library = new Library(("坏", MacroSignature.Empty,
            [Step("control.fail", Param("reason", "boom"))]));

        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "坏"), Param("runHandleName", "job")),
            Try(
                Body("body", Step("control.waitMacro", Param("runHandle", "$job"))),
                Body("catch", Set("status", "field($job, \"status\")"),
                    Set("error", "field($job, \"error\")"))),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("failed", store.Local.Values["status"].AsText());
        Assert.Equal(RunKeys.StepFailedByMacro, store.Local.Values["error"].AsText());
    }

    /// <summary>
    /// 同步调用失败时不把半截结果带回来：被调用的宏失败前写下的那一项，调用处读到的是空。
    /// </summary>
    [Fact]
    public async Task A_failed_call_brings_back_no_half_results()
    {
        var store = Store();
        var library = new Library(("坏", new MacroSignature([], [new MacroReturn("结果")]),
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

    /// <summary>后台运行在等待时报失败时，启动处勾的结果一个都不写回。</summary>
    [Fact]
    public async Task A_failed_wait_writes_none_of_the_chosen_results()
    {
        var store = Store();
        var library = new Library(("坏", new MacroSignature([], [new MacroReturn("结果")]),
            [Set("结果", "半截"), Step("control.fail", Param("reason", "boom"))]));

        var (result, _) = await RunAsync(
        [
            Step("control.startMacro", Param("macro", "坏"), Param("runHandleName", "job"),
                Returns("结果=带回")),
            Try(
                Body("body", Step("control.waitMacro", Param("runHandle", "$job"))),
                Body("catch")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["带回"].AsText());
    }

    /// <summary>
    /// 「等待全部」里有一个失败时，别的运行的结果也不写：一次等待要么把整列结果交出来，要么一项都不交。
    /// </summary>
    [Fact]
    public async Task Waiting_for_all_that_fails_writes_none_of_the_results()
    {
        var store = Store();
        var library = new Library(
            ("好", new MacroSignature([], [new MacroReturn("值")]), [Set("值", "好")]),
            ("坏", new MacroSignature([], [new MacroReturn("值")]),
                [Set("值", "半截"), Step("control.fail", Param("reason", "boom"))]));

        var (result, _) = await RunAsync(
        [
            Step("control.listCreate", Param("name", "jobs"), Param("scope", "local"),
                Param("items", "")),
            Step("control.startMacro", Param("macro", "好"), Param("runHandleName", "jobGood"),
                Returns("值=好值")),
            Step("control.listAdd", Param("name", "jobs"), Param("value", "$jobGood")),
            Step("control.startMacro", Param("macro", "坏"), Param("runHandleName", "jobBad"),
                Returns("值=坏值")),
            Step("control.listAdd", Param("name", "jobs"), Param("value", "$jobBad")),
            Try(
                Body("body", Step("control.waitAll", Param("runHandles", "$jobs"))),
                Body("catch")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["好值"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["坏值"].AsText());
    }

    /// <summary>
    /// 被调用的宏自己勾了「与别的宏互斥发输入」，父宏没勾也一样排队：门是在子宏的步骤上取的，
    /// 父宏的声明不会把它盖掉。
    /// </summary>
    [Fact]
    public async Task A_called_macro_takes_turns_with_input_even_when_its_caller_does_not()
    {
        var library = new Library(("输入", MacroSignature.Empty,
            [Step("input.keyPress", Param("key", "F5"))]));
        library.Exclusive.Add("输入");

        var holder = await InputGate.Take("another macro", CancellationToken.None);
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
                Body("catch", Set("seen", "$caught.key"), Set("detail", "$caught.detail")),
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
        Assert.Equal("Run.StepFailedByMacro", store.Local.Values["step.rrrr.error"].AsText());
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
                    Set("outer", "$caught.key"),
                    Try(
                        Body("body", Step("control.fail", Param("reason", "inner"))),
                        Body("catch", Set("inner", "$caught.key"))),
                    Set("outerAgain", "$caught.key"))),
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

    /// <summary>
    /// A random wait is the same idea as slack on a written wait, but the two ends are the
    /// macro author's: it never comes back before the short one, and it is not the same wait every
    /// time, which is the whole point of it.
    /// </summary>
    [Fact]
    public async Task A_random_wait_lands_between_the_two_numbers_it_was_given()
    {
        var step = Step("control.delayRandom", Param("minMs", "30"), Param("maxMs", "300"));

        var waits = new List<double>();
        for (var round = 0; round < 5; round++)
        {
            waits.Add(await TimeAsync([step], 1));
        }

        AssertWaitsAreSpaced(waits, 25, 1000);
        Assert.True(waits.Max() - waits.Min() > 20,
            $"every wait came out about the same: {string.Join(", ", waits.Select(w => w.ToString("0")))}");
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

        AssertWaitsAreSpaced(waits, 145, 1000);
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
            Takes("双击", ["n"], ["n"], AddOne("n"), AddOne("n")),
            Takes("清空", ["n"], ["n"], Set("n", "0")));

        var (result, _) = await RunAsync(
        [
            Call("双击", Param("arg.n", "$n"), Returns("n=n")),
            Set("中间", "$n"),
            Call("清空", Param("arg.n", "$n"), Returns("n=n")),
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
            Call("加一", Param("arg.输入", "$我的数"), Returns("结果=结果")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(42, N(store, "结果"));
    }

    [Fact]
    public async Task An_argument_the_call_leaves_out_of_an_optional_one_is_its_default()
    {
        var store = Store();
        var library = new Library(("倍数", new MacroSignature(
            [new MacroArgument("倍数", "5", false)], [new MacroReturn("结果")]), [Set("结果", "$倍数 * 2")]));

        var (result, _) = await RunAsync(
            [Call("倍数", Returns("结果=结果"))], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(10, N(store, "结果"));
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
    public async Task A_required_argument_can_be_supplied_as_an_empty_value()
    {
        var store = Store();
        var library = new Library(("甲",
            new MacroSignature([new MacroArgument("数量", "", true)], [new MacroReturn("痕迹")]),
            [Set("痕迹", "$数量")]));

        var (result, _) = await RunAsync(
        [
            Step("control.runMacro", Param("macro", "甲"), Param("arg.数量", ""),
                Param("arg.数量.given", "true"), Returns("痕迹=取回")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["取回"].AsText());
    }

    [Fact]
    public async Task An_optional_argument_can_be_supplied_as_an_empty_value()
    {
        var store = Store();
        var library = new Library(("甲",
            new MacroSignature([new MacroArgument("数量", "7", false)], [new MacroReturn("痕迹")]),
            [Set("痕迹", "$数量")]));

        var (result, _) = await RunAsync(
        [
            Step("control.runMacro", Param("macro", "甲"), Param("arg.数量", ""),
                Param("arg.数量.given", "true"), Returns("痕迹=取回")),
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
                [new MacroReturn("结果")]),
            [Set("结果", "$数量")]));

        var (result, _) = await RunAsync(
            [Step("control.runMacro", Param("macro", "甲"), Param("arg.数量", "不是数"))],
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
                new MacroArgument("同名", "", false)], []),
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
            Call("数个数", Param("arg.项目", "$项目"), Returns("个数=个数")),
        ], variables: store, macros: library);

        Assert.True(result.Succeeded);
        Assert.Equal(3, N(store, "个数"));
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

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MacroNotFound", result.Key);
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
        var library = new Library(("对账", new MacroSignature([], [new MacroReturn("看到的")]),
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


    [Fact]
    public async Task A_row_that_names_a_column_the_list_does_not_have_stops_the_step()
    {
        var returns = new ExecutableParameter
        {
            Name = "returns",
            Rows =
            [
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["key"] = "path",
                    ["name"] = "part",
                    ["bogus"] = "x",
                },
            ],
        };

        var (result, _) = await RunAsync(
            [Step("file.path", Param("operation", "name"), Param("path", "a.txt"), returns)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadRowColumn, result.Key);
        Assert.Equal("returns.bogus", result.Detail);
    }

    [Fact]
    public async Task A_row_that_leaves_out_a_required_column_stops_the_step()
    {
        var returns = new ExecutableParameter
        {
            Name = "returns",
            Rows =
            [
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["key"] = "path",
                },
            ],
        };

        var (result, _) = await RunAsync(
            [Step("file.path", Param("operation", "name"), Param("path", "a.txt"), returns)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadRowColumn, result.Key);
        Assert.Equal("returns.name", result.Detail);
    }

    /// <summary>A call to another macro of the stand-in project.</summary>
    private static ExecutableStep Call(string macro, params ExecutableParameter[] more)
        => Step("control.runMacro", [Param("macro", macro), .. more]);

    /// <summary>
    /// A macro that declares what a call has to fill in and may ask for. Every argument is
    /// required, which is what a macro that reads a value it was handed tends to want.
    /// </summary>
    private static (string Name, MacroSignature Signature, ExecutableStep[] Steps) Takes(
        string name, string[] arguments, string[] results, params ExecutableStep[] steps)
        => (name, new MacroSignature(
            [.. arguments.Select(argument => new MacroArgument(argument, string.Empty, true))],
            [.. results.Select(name => new MacroReturn(name))]), steps);

    /// <summary>The same for a macro that asks for nothing, which is a macro called for what it does.</summary>
    private static (string Name, MacroSignature Signature, ExecutableStep[] Steps) Plain(
        string name, params ExecutableStep[] steps) => (name, MacroSignature.Empty, steps);

    /// <summary>A stand-in for the macros of one project, so a call can be tested on its own.</summary>
    private sealed class Library : IMacroLibrary
    {
        private readonly Dictionary<string, (IReadOnlyList<ExecutableStep> Steps,
            MacroSignature Signature)> _byName = new(StringComparer.OrdinalIgnoreCase);

        public Library(params (string Name, MacroSignature Signature, ExecutableStep[] Steps)[] macros)
        {
            foreach (var (name, signature, steps) in macros)
            {
                _byName[name] = (steps, signature);
            }
        }

        public IReadOnlyList<string> Names => [.. _byName.Keys];

        public IReadOnlyList<ExecutableStep>? Steps(string name)
            => _byName.TryGetValue(name, out var macro) ? macro.Steps : null;

        public MacroSignature? Signature(string name)
            => _byName.TryGetValue(name, out var macro) ? macro.Signature : null;

        /// <summary>Macros that asked to take turns sending input.</summary>
        public HashSet<string> Exclusive { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ExclusiveInput(string name) => Exclusive.Contains(name);
    }

    private sealed class WatchingHost : IRunHost
    {
        public System.Collections.Generic.List<ExecutableStep> Seen { get; } = [];

        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        {
            Seen.Add(step);
            return Task.FromResult(true);
        }

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);
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

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromResult(true);

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
        {
            Questions.Add((step, reason, detail, comment));
            var answer = _index < answers.Length ? answers[_index] : StepErrorChoice.Stop;
            _index++;
            return Task.FromResult(answer);
        }

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);
    }
}
