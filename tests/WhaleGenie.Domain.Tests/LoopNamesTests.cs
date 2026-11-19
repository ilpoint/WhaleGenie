using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// The names a loop keeps for its rounds. The check reads them to warn about a loop nested inside
/// another one, and the dialog that writes a new counting loop reads them to start that loop on a
/// name nothing around it is using — so the two have to agree on what counts as a loop's own name.
/// </summary>
public class LoopNamesTests
{
    [Fact]
    public void A_counting_loop_names_the_counter_it_counts_in()
    {
        Assert.Equal(["i"], LoopNames.Of(Step("control.for", Name("name", "i"))));
        Assert.Equal(["row"], LoopNames.Of(Step("control.for", Name("name", "row"))));

        // A counting loop whose counter field is empty writes the name the catalogue starts it
        // with. Treating that as "no name" would let an inner loop borrow a name the outer one
        // really writes.
        Assert.Equal(["i"], LoopNames.Of(Step("control.for")));
        Assert.Equal(["i"], LoopNames.Of(Step("control.for", Name("name", "  "))));
    }

    [Fact]
    public void A_for_each_names_its_item_and_its_round_number()
    {
        Assert.Equal(["item", "index"], LoopNames.Of(Step("control.forEach",
            Name("itemVariable", "item"), Name("indexVariable", "index"))));

        // The round number is opt-in: a loop that names no round number keeps none.
        Assert.Equal(["entry"], LoopNames.Of(Step("control.forEach",
            Name("itemVariable", "entry"))));

        // The item is not: an empty one is the item the engine hands over by default.
        Assert.Equal(["item"], LoopNames.Of(Step("control.forEach")));
    }

    [Fact]
    public void A_repeat_or_a_while_keeps_no_name_of_the_macros()
    {
        Assert.Empty(LoopNames.Of(Step("control.repeat")));
        Assert.Empty(LoopNames.Of(Step("control.while")));
        Assert.Empty(LoopNames.Of(Step("control.delay")));
    }

    [Fact]
    public void Only_the_counter_field_of_a_counting_loop_writes_a_name()
    {
        Assert.True(LoopNames.WritesCounter("control.for", "name"));

        // The same field name belongs to other actions, and a "for each" names its variables in
        // fields of their own.
        Assert.False(LoopNames.WritesCounter("control.setVariable", "name"));
        Assert.False(LoopNames.WritesCounter("control.forEach", "name"));
        Assert.False(LoopNames.WritesCounter("control.for", "rangeFrom"));
        Assert.False(LoopNames.WritesCounter(null, "name"));
    }

    [Fact]
    public void The_free_counter_skips_a_name_the_loops_around_it_are_using()
    {
        Assert.Equal("i", LoopNames.Free([], "i"));
        Assert.Equal("i", LoopNames.Free(["j"], "i"));

        // The second loop reads i2 rather than i1: the number says "another one of these", and a
        // one would read as the first.
        Assert.Equal("i2", LoopNames.Free(["i"], "i"));

        // Two names, and case is not a difference: the variable table does not tell them apart.
        Assert.Equal("i2", LoopNames.Free(["I"], "i"));
        Assert.Equal("i3", LoopNames.Free(["i", "i2"], "i"));
        Assert.Equal("i3", LoopNames.Free(["I", "I2"], "i"));
    }

    /// <summary>
    /// The two answers meet here: a loop written on the name the others are using is reported, and
    /// the name this class hands the dialog is not — which is what the dialog is for.
    /// </summary>
    [Fact]
    public void A_counter_named_by_this_rule_is_not_reported_as_shadowed()
    {
        var borrowed = Counter("i", Counter("i"));
        var borrowedInner = borrowed.StepLists.Single().Steps.Single();

        Assert.Contains(Shadowed(borrowed), problem => problem.Step == borrowedInner);

        // The same two loops, with the inner one on the name the first is not using.
        var named = Counter("i", Counter(LoopNames.Free(LoopNames.Of(borrowed), "i")));
        Assert.Empty(Shadowed(named));
    }

    private static IEnumerable<MacroProblem> Shadowed(MacroStep step)
        => Problems(step).Where(problem =>
            problem.Code == MacroProblemCode.LoopVariableShadowed);

    private static MacroStep Counter(string name, params MacroStep[] body)
        => Step("control.for", Name("name", name), Body(body));

    private static IReadOnlyList<MacroProblem> Problems(MacroStep step)
        => MacroCheck.Inspect([step], null, [], [], browserShared: false);

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static StepParameter Name(string name, string value)
        => new()
        {
            Name = name,
            Kind = ActionParameterKind.Text,
            Written = Formula.Name(value),
        };

    private static StepParameter Body(params MacroStep[] steps)
        => new()
        {
            Name = "body",
            Kind = ActionParameterKind.Steps,
            Steps = [.. steps],
        };
}
