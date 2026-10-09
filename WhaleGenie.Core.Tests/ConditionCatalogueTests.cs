using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The conditions a step can ask. Each one has to answer two ways round — the thing it asks for is
/// there, and it is not — because a condition that is always true is worse than no condition at all.
/// </summary>
public class ConditionCatalogueTests
{
    private static ExecutableStep Condition(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static ExecutableStep Set(string id, string name, string value)
        => new()
        {
            Type = "control.setVariable",
            Id = id,
            Parameters = [Param("name", name), Param("scope", "local"), Param("value", value)],
        };

    /// <summary>An <c>if</c> that writes "yes" when a condition holds, so a check can read it back.</summary>
    private static ExecutableStep Asked(ExecutableStep condition)
        => new()
        {
            Type = "control.if",
            Parameters =
            [
                new ExecutableParameter { Name = "condition", Condition = condition },
                new ExecutableParameter { Name = "then", Steps = [Set("then", "answer", "yes")] },
            ],
        };

    private static async Task<(bool Holds, RunResult Result)> AskAsync(ExecutableStep condition,
        VariableStore? variables = null, IDeviceLayer? devices = null)
    {
        var store = variables ?? new VariableStore();
        store.Local.Remove("answer");
        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync([Asked(condition)]);
        return (store.Local.TryGet("answer", out var answer) && answer.AsText() == "yes", result);
    }

    /// <summary>A step that fails however it is run, which is what the failure states are asked about.</summary>
    private static ExecutableStep Broken(string id, StepErrorAction action)
        => new()
        {
            Type = "input.keyPress",
            Id = id,
            Parameters = [Param("key", "F5")],
            Meta = new StepMeta { OnError = action },
        };

    [Fact]
    public async Task A_step_that_did_its_work_answers_success()
    {
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost()).RunAsync(
        [
            Set("aaaa", "n", "1"),
            Asked(Condition("condition.stepResult", Param("step", "aaaa"), Param("expected", "ok"))),
        ]);

        Assert.True(result.Succeeded, result.Detail);
        Assert.Equal("yes", store.Local.Values["answer"].AsText());
    }

    [Fact]
    public async Task A_step_that_failed_answers_failed()
    {
        var store = new VariableStore();
        await new MacroRunner(store, new SilentRunHost()).RunAsync(
        [
            Broken("bbbb", StepErrorAction.Continue),
            Asked(Condition("condition.stepResult", Param("step", "bbbb"), Param("expected", "failed"))),
        ]);

        Assert.Equal("yes", store.Local.Values["answer"].AsText());
    }

    [Fact]
    public async Task A_step_the_run_has_not_reached_answers_not_reached_rather_than_no()
    {
        // The question is asked before the run gets anywhere near the step it is about, which is
        // the case a macro cannot answer for itself: "nothing has happened yet" and "it did not
        // work" are different answers, and a two-state condition would call both of them "no".
        var store = new VariableStore();
        await new MacroRunner(store, new SilentRunHost()).RunAsync(
        [
            Asked(Condition("condition.stepResult", Param("step", "dddd"), Param("expected", "skipped"))),
            new ExecutableStep { Type = "control.stop", Id = "cccc" },
            Set("dddd", "later", "1"),
        ]);

        Assert.Equal("yes", store.Local.Values["answer"].AsText());
        Assert.False(store.TryGet("later", out _));
        Assert.Equal("skipped", store.Local.Values["step.dddd.outcome"].AsText());
    }

    [Fact]
    public async Task A_condition_about_a_step_that_is_not_in_the_macro_fails_the_step()
    {
        var (holds, result) = await AskAsync(
            Condition("condition.stepResult", Param("step", "zzzz"), Param("expected", "ok")));

        Assert.False(holds);
        Assert.Equal("Run.NoSuchStep", result.Key);
    }

    [Fact]
    public async Task A_list_holding_a_value_is_found_whether_it_is_written_out_or_held_in_a_variable()
    {
        var store = new VariableStore();
        store.Local.SetList("items", [Value.FromText("a"), Value.FromNumber(2)]);

        var held = await AskAsync(
            Condition("condition.listContains", Param("list", "$items"), Param("value", "2")),
            variables: store);
        var written = await AskAsync(
            Condition("condition.listContains", Param("list", "a;b;c"), Param("value", "b")));
        var missing = await AskAsync(
            Condition("condition.listContains", Param("list", "$items"), Param("value", "z")),
            variables: store);

        Assert.True(held.Holds);
        Assert.True(written.Holds);
        Assert.False(missing.Holds);
    }

    [Fact]
    public async Task A_path_is_asked_about_the_way_it_was_written()
    {
        var devices = new FakeDeviceLayer();
        devices.Files[@"G:\fake\report.csv"] = "a,b";

        var there = await AskAsync(
            Condition("condition.pathExists", Param("path", @"G:\fake\report.csv")), devices: devices);
        var missing = await AskAsync(
            Condition("condition.pathExists", Param("path", @"G:\fake\nothing.csv")), devices: devices);

        Assert.True(there.Holds);
        Assert.False(missing.Holds);
    }

    [Fact]
    public async Task A_running_program_is_told_apart_from_one_that_is_not_running()
    {
        var devices = new FakeDeviceLayer();
        devices.Running["notepad"] = [7];

        var running = await AskAsync(
            Condition("condition.processRunning", Param("name", "notepad")), devices: devices);
        var stopped = await AskAsync(
            Condition("condition.processRunning", Param("name", "chrome")), devices: devices);

        Assert.True(running.Holds);
        Assert.False(stopped.Holds);
    }

    [Fact]
    public async Task A_window_is_looked_for_by_its_title_by_its_program_or_as_any_window_at_all()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(1, "Untitled - Notepad",
            new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));
        devices.WindowFacts[1] = ("notepad", "Notepad");

        var byTitle = await AskAsync(Condition("condition.windowExists",
            Param("match", "title"), Param("value", "notepad")), devices: devices);
        var byProgram = await AskAsync(Condition("condition.windowExists",
            Param("match", "process"), Param("value", "note")), devices: devices);
        var any = await AskAsync(Condition("condition.windowExists",
            Param("match", "title"), Param("value", string.Empty)), devices: devices);
        var none = await AskAsync(Condition("condition.windowExists",
            Param("match", "title"), Param("value", "excel")), devices: devices);

        Assert.True(byTitle.Holds);
        Assert.True(byProgram.Holds);
        Assert.True(any.Holds);
        Assert.False(none.Holds);
    }

    [Fact]
    public async Task A_range_counts_both_of_its_ends_and_they_may_be_written_either_way_round()
    {
        static VariableStore With(double value)
        {
            var store = new VariableStore();
            store.Local.SetNumber("n", value);
            return store;
        }

        var inside = await AskAsync(Condition("condition.valueInRange",
            Param("value", "$n"), Param("min", "1"), Param("max", "10")), variables: With(5));
        var lowEnd = await AskAsync(Condition("condition.valueInRange",
            Param("value", "$n"), Param("min", "1"), Param("max", "10")), variables: With(1));
        var highEnd = await AskAsync(Condition("condition.valueInRange",
            Param("value", "$n"), Param("min", "1"), Param("max", "10")), variables: With(10));
        var outside = await AskAsync(Condition("condition.valueInRange",
            Param("value", "$n"), Param("min", "1"), Param("max", "10")), variables: With(11));
        var backwards = await AskAsync(Condition("condition.valueInRange",
            Param("value", "$n"), Param("min", "10"), Param("max", "1")), variables: With(5));

        Assert.True(inside.Holds);
        Assert.True(lowEnd.Holds);
        Assert.True(highEnd.Holds);
        Assert.False(outside.Holds);
        Assert.True(backwards.Holds);
    }

    [Fact]
    public async Task Two_dates_are_compared_and_the_time_of_day_can_be_left_out_of_it()
    {
        var before = await AskAsync(Condition("condition.dateCompare",
            Param("left", "2020-01-01"), Param("operator", "before"), Param("right", "2021-01-01")));
        var after = await AskAsync(Condition("condition.dateCompare",
            Param("left", "today()"), Param("operator", "after"), Param("right", "2000-01-01")));
        var wrong = await AskAsync(Condition("condition.dateCompare",
            Param("left", "2020-01-01"), Param("operator", "after"), Param("right", "2021-01-01")));

        // Two moments on the same day are not the same moment, which is what "the same day" is for.
        var sameDay = await AskAsync(Condition("condition.dateCompare",
            Param("left", "09:00"), Param("operator", "sameDay"), Param("right", "today()")));
        var sameMoment = await AskAsync(Condition("condition.dateCompare",
            Param("left", "09:00"), Param("operator", "same"), Param("right", "today()")));

        Assert.True(before.Holds);
        Assert.True(after.Holds);
        Assert.False(wrong.Holds);
        Assert.True(sameDay.Holds);
        Assert.False(sameMoment.Holds);
    }

    [Fact]
    public async Task A_date_that_cannot_be_read_fails_the_step_rather_than_answering_no()
    {
        var (holds, result) = await AskAsync(Condition("condition.dateCompare",
            Param("left", "not a date"), Param("operator", "before"), Param("right", "2021-01-01")));

        Assert.False(holds);
        Assert.Equal("Run.BadExpression", result.Key);
    }
}
