using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// What stops a step: a check that refuses it, the rule it leaves behind, a retry, a timeout.
public partial class MacroRunnerTests
{
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

    [Theory]
    [InlineData("control.calculate")]
    [InlineData("control.delayRandom")]
    [InlineData("control.listCreate")]
    public async Task A_removed_flow_action_is_reported_as_unsupported(string type)
    {
        var (result, _) = await RunAsync([Step(type)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.Unsupported, result.Key);
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
        Assert.True(watch.Elapsed.TotalMilliseconds < 4000,
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
        Assert.True(watch.Elapsed.TotalMilliseconds < 4000,
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
    public async Task A_fail_step_can_report_a_user_key()
    {
        var (failed, _) = await RunAsync(
        [
            Step("control.fail", Param("failureKey", "User.NoScreen"), Param("reason", "the screen moved")),
        ]);

        Assert.Equal("User.NoScreen", failed.Key);
    }

    [Fact]
    public async Task A_fail_step_refuses_a_key_outside_the_user_namespace()
    {
        var (failed, _) = await RunAsync(
        [
            Step("control.fail", Param("failureKey", "Run.NotMine")),
        ]);

        Assert.Equal(RunKeys.BadUserFailureKey, failed.Key);
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
}
