using System.Collections.Generic;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What each step leaves behind for the conditions written after it:
/// <c>step.&lt;id&gt;.outcome</c>, reading <c>ok</c>, <c>failed</c> or <c>skipped</c>.
/// </summary>
public class StepOutcomeTests
{
    private static ExecutableStep Step(string type, string id, params ExecutableParameter[] parameters)
        => new() { Type = type, Id = id, Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };

    private static ExecutableParameter When(string name, ExecutableStep step)
        => new() { Name = name, Condition = step };

    private static ExecutableStep Set(string id, string name, string value)
        => Step("control.setVariable", id, Param("name", name), Param("scope", "local"),
            Param("value", value));

    /// <summary>A step that fails however it is run: a key press with no keyboard to press it on.</summary>
    private static ExecutableStep Broken(string id, StepErrorAction action = StepErrorAction.Stop)
        => new()
        {
            Type = "input.keyPress",
            Id = id,
            Parameters = [Param("key", "F5")],
            Meta = new StepMeta { OnError = action },
        };

    private static async Task<VariableStore> RunAsync(ExecutableStep[] steps,
        IMacroLibrary? macros = null, VariableStore? variables = null)
    {
        var store = variables ?? new VariableStore();
        await new MacroRunner(store, new SilentRunHost(), null, 1, macros).RunAsync(steps);
        return store;
    }

    private static VariableStore Store(params (string Name, double Value)[] numbers)
    {
        var store = new VariableStore();
        foreach (var (name, value) in numbers)
        {
            store.Local.SetNumber(name, value);
        }

        return store;
    }

    private static string Outcome(VariableStore store, string id)
        => store.Local.Values[$"step.{id}.outcome"].AsText();

    [Fact]
    public async Task A_step_that_does_its_work_reads_ok()
    {
        var store = await RunAsync([Set("k3f9", "n", "1")]);

        Assert.Equal("ok", Outcome(store, "k3f9"));
    }

    [Fact]
    public async Task A_step_that_fails_reads_failed_even_when_the_run_carries_on()
    {
        var stop = await RunAsync([Broken("aaaa")]);
        var carry = await RunAsync([Broken("bbbb", StepErrorAction.Continue), Set("cccc", "n", "1")]);

        Assert.Equal("failed", Outcome(stop, "aaaa"));
        Assert.Equal("failed", Outcome(carry, "bbbb"));
        Assert.Equal("ok", Outcome(carry, "cccc"));
        Assert.Equal("1", carry.Local.Values["n"].AsText());
    }

    [Fact]
    public async Task A_step_the_run_never_reaches_reads_skipped()
    {
        var store = await RunAsync(
        [
            Step("control.stop", "aaaa"),
            Set("bbbb", "n", "1"),
        ]);

        Assert.Equal("ok", Outcome(store, "aaaa"));
        Assert.Equal("skipped", Outcome(store, "bbbb"));
        Assert.False(store.TryGet("n", out _));
    }

    [Fact]
    public async Task A_step_that_is_switched_off_reads_skipped()
    {
        var off = new ExecutableStep
        {
            Type = "control.setVariable",
            Id = "aaaa",
            Parameters = [Param("name", "n"), Param("scope", "local"), Param("value", "1")],
            Meta = StepMeta.Empty.WithEnabled(false),
        };

        var store = await RunAsync([off]);

        Assert.Equal("skipped", Outcome(store, "aaaa"));
    }

    [Fact]
    public async Task The_steps_of_a_branch_that_was_not_taken_read_skipped()
    {
        var store = await RunAsync(
        [
            Step("control.if", "aaaa",
                When("condition", Step("condition.compare", string.Empty,
                    Param("variable", "n"), Param("operator", "greaterThan"), Param("value", "3"))),
                Body("then", Set("bbbb", "hit", "yes")),
                Body("else", Set("cccc", "miss", "yes"))),
        ],
            variables: Store(("n", 0)));

        Assert.Equal("ok", Outcome(store, "aaaa"));
        Assert.Equal("skipped", Outcome(store, "bbbb"));
        Assert.Equal("ok", Outcome(store, "cccc"));
    }

    [Fact]
    public async Task A_condition_is_not_a_step_and_gets_no_outcome_of_its_own()
    {
        var store = await RunAsync(
        [
            Step("control.if", "aaaa",
                When("condition", Step("condition.compare", "dddd",
                    Param("variable", "n"), Param("operator", "equals"), Param("value", "0"))),
                Body("then", Set("bbbb", "hit", "yes"))),
        ],
            variables: Store(("n", 0)));

        Assert.False(store.TryGet("step.dddd.outcome", out _));
        Assert.Equal("ok", Outcome(store, "bbbb"));
    }

    [Fact]
    public async Task A_step_that_works_on_a_later_attempt_reads_ok()
    {
        var store = new VariableStore();
        // The call is only there from the second ask onwards, which is what a retry is for.
        var step = new ExecutableStep
        {
            Type = "control.runMacro",
            Id = "aaaa",
            Parameters =
            [
                Param("macro", "later"),
                Param("arguments", string.Empty),
                Param("results", string.Empty),
            ],
            Meta = new StepMeta { RetryCount = 1, RetryDelayMs = 0, OnError = StepErrorAction.Continue },
        };

        await new MacroRunner(store, new SilentRunHost(), null, 1, new OnceMissing()).RunAsync([step]);

        Assert.Equal("ok", Outcome(store, "aaaa"));
    }

    [Fact]
    public async Task A_block_reads_failed_when_a_step_inside_it_failed()
    {
        var store = await RunAsync(
        [
            Step("control.sequence", "aaaa",
                Body("steps", Broken("bbbb"), Set("cccc", "n", "1"))),
            Set("dddd", "after", "1"),
        ]);

        Assert.Equal("failed", Outcome(store, "bbbb"));
        Assert.Equal("failed", Outcome(store, "aaaa"));
        Assert.Equal("skipped", Outcome(store, "cccc"));
        Assert.Equal("skipped", Outcome(store, "dddd"));
    }

    [Fact]
    public async Task A_block_that_handled_a_failure_reads_ok()
    {
        var store = await RunAsync(
        [
            Step("control.sequence", "aaaa",
                Body("steps", Broken("bbbb", StepErrorAction.Continue))),
            Set("cccc", "after", "1"),
        ]);

        Assert.Equal("failed", Outcome(store, "bbbb"));
        Assert.Equal("ok", Outcome(store, "aaaa"));
        Assert.Equal("ok", Outcome(store, "cccc"));
    }

    [Fact]
    public async Task The_catch_of_a_try_can_read_what_the_failed_step_left_behind()
    {
        var store = await RunAsync(
        [
            Step("control.try", "aaaa",
                Param("errorVariable", "why"),
                Body("body", Broken("bbbb")),
                Body("catch", Set("cccc", "seen", "$step.bbbb.outcome")),
                Body("finally", Set("dddd", "tidied", "1"))),
        ]);

        Assert.Equal("failed", Outcome(store, "bbbb"));
        Assert.Equal("failed", store.Local.Values["seen"].AsText());
        Assert.Equal("ok", Outcome(store, "cccc"));
        Assert.Equal("ok", Outcome(store, "dddd"));
        // The attempt failed and the catch answered it, so the try itself did its job.
        Assert.Equal("ok", Outcome(store, "aaaa"));
    }

    [Fact]
    public async Task A_macro_that_is_called_keeps_its_own_outcomes_to_itself()
    {
        var store = new VariableStore();
        var called = Set("aaaa", "inside", "1");
        var library = new Library(("other", [called]));

        await new MacroRunner(store, new SilentRunHost(), null, 1, library).RunAsync(
        [
            Step("control.runMacro", "aaaa",
                Param("macro", "other"), Param("arguments", string.Empty),
                Param("results", string.Empty)),
            Set("bbbb", "after", "1"),
        ]);

        // The call ran a step called "aaaa" of its own; that is not the caller's "aaaa".
        Assert.Equal("ok", Outcome(store, "aaaa"));
        Assert.False(store.TryGet("inside", out _));
    }

    [Fact]
    public async Task A_step_without_a_name_is_left_alone()
    {
        var store = await RunAsync(
        [
            new ExecutableStep
            {
                Type = "control.setVariable",
                Parameters = [Param("name", "n"), Param("scope", "local"), Param("value", "1")],
            },
        ]);

        Assert.False(store.TryGet("step..outcome", out _));
        Assert.Equal("1", store.Local.Values["n"].AsText());
    }

    private sealed class Library(params (string Name, ExecutableStep[] Steps)[] macros)
        : IMacroLibrary
    {
        private readonly Dictionary<string, IReadOnlyList<ExecutableStep>> _byName =
            macros.ToDictionary(macro => macro.Name, macro => (IReadOnlyList<ExecutableStep>)macro.Steps);

        public IReadOnlyList<string> Names => [.. _byName.Keys];

        public IReadOnlyList<ExecutableStep>? Steps(string name)
            => _byName.TryGetValue(name, out var steps) ? steps : null;
    }

    /// <summary>A library whose one macro turns up only on the second ask.</summary>
    private sealed class OnceMissing : IMacroLibrary
    {
        private int _asks;

        public IReadOnlyList<string> Names => ["later"];

        public IReadOnlyList<ExecutableStep>? Steps(string name)
            => ++_asks == 1
                ? null
                : [Set("eeee", "called", "1")];
    }
}
