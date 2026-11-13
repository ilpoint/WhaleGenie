using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// Switches and conditions: which case answers, how values compare, and what a branch sees.
public partial class MacroRunnerTests
{

    private static ExecutableStep Case(string values, params ExecutableStep[] steps)
        => Step("control.case",
            new ExecutableParameter { Name = "values", Written = CaseValues(values) },
            Body("body", steps));

    /// <summary>The values one branch answers to, each of them its own piece of the list.</summary>
    private static ExpressionNode CaseValues(string values)
        => new ListNode([.. values
            .Split(';')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Select(Formula.Text)]);

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
            Step("control.switch", Param("switchValue", "$kind"), Param("switchMode", "notContains"),
                Body("cases", Case("slow", Set("plain", "yes")))),
            Step("control.switch", Param("switchValue", "$n"), Param("switchMode", "greaterThan"),
                Body("cases", Case("1", Set("big", "yes")))),
            Step("control.switch", Param("switchValue", "$n"), Param("switchMode", "lessOrEqual"),
                Body("cases", Case("2", Set("small", "yes")))),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("fast", store.Local.Values["hit"].AsText());
        Assert.Equal("yes", store.Local.Values["other"].AsText());
        Assert.Equal("yes", store.Local.Values["plain"].AsText());
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
        var branch = Step("control.if",
            Param("condition", "($a > 5) or ($b == 2)"),
            Body("then", Set("hit", "yes")));
        var (_, _) = await RunAsync([branch], variables: store);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task Comparisons_work_on_text_and_numbers()
    {
        var store = Store();
        store.Local.SetText("name", "Ada");

        var (_, _) = await RunAsync(
            [
                Step("control.if", Param("condition", "contains($name, \"ad\")"),
                    Body("then", Set("c", "1"))),
                Step("control.if", Param("condition", "match($name, \"^A.*a$\")"),
                    Body("then", Set("r", "1"))),
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

        ExecutableStep Compare(string name, string expression)
            => Step("control.if", Param("condition", expression),
                Body("then", Set(name, "1")));

        var (result, _) = await RunAsync(
            [
                Compare("strict", "compare($padded, \"equals\", \"a\", true, false)"),
                Compare("trimmed", "compare($padded, \"equals\", \"a\", true, true)"),
                Step("control.if", Param("condition", "compare($mixed, \"contains\", \"save\")"),
                    Body("then", Set("folded", "1"))),
                Step("control.if", Param("condition", "compare($mixed, \"contains\", \"save\", true)"),
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
                Param("condition", "compare($long, \"regex\", \"(a+)+$\")"),
                Body("then", Set("never", "1")),
            ],
            Meta = new StepMeta { OnError = StepErrorAction.Continue },
        };

        var (result, _) = await RunAsync([slow, Set("after", "1")], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("1", store.Local.Values["after"].AsText());
        Assert.False(store.TryGet("never", out _));
        Assert.Equal("failed", store.Local.Values["step.rx01.outcome"].AsText());
    }
}
