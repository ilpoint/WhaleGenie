using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// The order a macro runs in: steps, sequences, values, and the loops that repeat a body.
public partial class MacroRunnerTests
{
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
        var (_, _) = await RunAsync([Set("total", Formula.Binary("+", Formula.Binary("*", Formula.Name("a"), Formula.Name("b")), Formula.Number(1)))], variables: store);

        Assert.Equal(7, N(store, "total"));
    }

    [Fact]
    public async Task A_numeric_variable_name_is_written_and_read_back()
    {
        var store = Store();
        var (result, _) = await RunAsync([Set("123", "2"), AddOne("123")], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(3, N(store, "123"));
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

    [Fact]
    public async Task While_waits_between_rounds()
    {
        var store = Store(("n", 0));
        var loop = Step("control.while",
            When("condition", Step("condition.compare",
                Param("variable", "n"), Param("compareOperator", "lessThan"), Param("compareValue", "3"))),
            Param("maxIterations", "50"),
            Param("intervalMs", "60"),
            Body("body", AddOne("n")));

        var started = Stopwatch.GetTimestamp();
        var (_, _) = await RunAsync([loop], variables: store);
        var waited = Stopwatch.GetElapsedTime(started);

        // A condition loop that runs flat out is the one thing an interval is there to stop: the
        // field would read as decoration otherwise. Three rounds at 60 ms cannot finish in less
        // than 150 ms, and the assertion is a floor rather than an equality because a busy machine
        // only ever adds to a wait.
        Assert.Equal(3, N(store, "n"));
        Assert.True(waited >= TimeSpan.FromMilliseconds(150),
            $"three rounds with a 60 ms interval took {waited.TotalMilliseconds:0} ms");
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
        var seen = Body("body", Set("last", Formula.Name("item")), Set("rounds", Formula.Binary("+", Formula.Name("rounds"), Formula.Number(1))));
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
            Body("body", Set("innerSeen", Formula.Name("sys.loopIndex"))));
        var outer = Step("control.for",
            Param("rangeFrom", "1"),
            Param("rangeTo", "3"),
            Param("stepSize", "1"),
            Param("intervalMs", "0"),
            Param("name", "i"),
            Body("body",
                Set("outerSeen", Formula.Name("sys.loopIndex")),
                inner,
                Set("restored", Formula.Name("sys.loopIndex"))));

        var (result, _) = await RunAsync([outer], variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, N(store, "outerSeen"));
        Assert.Equal(1, N(store, "innerSeen"));
        Assert.Equal(2, N(store, "restored"));
        Assert.False(store.Local.Contains(VariableStore.LoopIndexName));
    }

    /// <summary>
    /// The round number is the loop's own binding, so outside a loop there is no such name at all.
    /// Reading it there is reading a name nothing wrote, which is the same failure as any other.
    /// </summary>
    [Fact]
    public async Task Reading_the_loop_round_outside_a_loop_fails()
    {
        var (result, _) = await RunAsync([Set("after", Formula.Name("sys.loopIndex"))]);

        Assert.Equal(RunKeys.BadExpression, result.Key);
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
            Body("body", Set("innerLast", Formula.Name("item"))));
        var outer = Step("control.forEach", Param("items", "[1, 2]"),
            Param("itemVariable", "item"),
            Body("body", Set("outerBefore", Formula.Name("item")), inner, Set("outerAfter", Formula.Name("item"))));

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
            Fields.Reference("switchValue", "kind"), Param("switchMode", "equals"),
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
            Fields.Reference("items", "names"),
            Param("reverse", "true"),
            Body("body", Set("last", Formula.Name("item"))));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal("a", store.Local.Values["last"].AsText());
    }

    [Fact]
    public async Task Counting_from_one_to_three_runs_three_times()
    {
        // The counter is the loop's own: it is not seeded first, because a name the macro
        // already held would be put back when the loop ended.
        var store = Store(("sum", 0));
        var loop = Step("control.for",
            Param("rangeFrom", "1"),
            Param("rangeTo", "3"),
            Param("intervalMs", "0"),
            Param("name", "i"),
            Body("body", Set("sum", Formula.Binary("+", Formula.Name("sum"), Formula.Name("i")))));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(6, N(store, "sum"));
        Assert.Equal(3, N(store, "i"));
    }

    /// <summary>
    /// A counter is meant to stay readable after its loop, but a name the macro declared was not
    /// the loop's to keep: the declared value goes back when the loop ends. The editor refuses a
    /// macro of this shape, so this is the guard behind that refusal.
    /// </summary>
    [Fact]
    public async Task A_loop_that_counts_in_a_declared_name_puts_the_declared_value_back()
    {
        var store = Store();
        var signature = new MacroSignature([], [],
            [new MacroLocal("i", "7", ValueKind.Number, "i00001")]);
        var loop = Step("control.for",
            Param("rangeFrom", "1"),
            Param("rangeTo", "3"),
            Param("intervalMs", "0"),
            Param("name", "i"),
            Body("body"));

        var (result, _) = await RunAsync([loop], variables: store, startSignature: signature);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(7, N(store, "i"));
    }

    [Fact]
    public async Task A_counting_loop_can_go_downwards()
    {
        var store = Store(("rounds", 0));
        var loop = Step("control.for",
            Param("rangeFrom", "5"),
            Param("rangeTo", "1"),
            Param("stepSize", "-1"),
            Body("body", Set("rounds", Formula.Binary("+", Formula.Name("rounds"), Formula.Number(1)))));

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
            Body("body", Set("rounds", Formula.Binary("+", Formula.Name("rounds"), Formula.Number(1)))));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(5, N(store, "rounds"));
    }

    [Fact]
    public async Task Break_leaves_a_counting_loop()
    {
        var store = Store(("n", 0));
        var body = Body("body",
            Set("n", Formula.Binary("+", Formula.Name("n"), Formula.Number(1))),
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
            Body("body", Set("seen", Formula.Name("round"))));

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
            Body("body", Set("seen", Formula.Name("item"))));

        var (_, _) = await RunAsync([loop], variables: store);

        Assert.Equal(20, N(store, "seen"));
        Assert.False(store.TryGet("round", out _));
    }
}
